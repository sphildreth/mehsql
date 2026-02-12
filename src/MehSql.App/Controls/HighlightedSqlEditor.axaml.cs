using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Serilog;
using MehSql.Core.Querying;
using System.Collections.Generic;

namespace MehSql.App.Controls;

public partial class HighlightedSqlEditor : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<HighlightedSqlEditor, string>(nameof(Text), defaultValue: string.Empty);

    public static readonly StyledProperty<bool> IsDarkThemeProperty =
        AvaloniaProperty.Register<HighlightedSqlEditor, bool>(nameof(IsDarkTheme), defaultValue: true);

    public static readonly StyledProperty<AutocompleteCache?> AutocompleteCacheProperty =
        AvaloniaProperty.Register<HighlightedSqlEditor, AutocompleteCache?>(
            nameof(AutocompleteCache), defaultValue: null);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsDarkTheme
    {
        get => GetValue(IsDarkThemeProperty);
        set => SetValue(IsDarkThemeProperty, value);
    }

    public AutocompleteCache? AutocompleteCache
    {
        get => GetValue(AutocompleteCacheProperty);
        set => SetValue(AutocompleteCacheProperty, value);
    }

    private bool _isUpdatingText;
    private System.Timers.Timer? _updateTimer;

    // Autocomplete fields
    private System.Timers.Timer? _autocompleteTimer;
    private CancellationTokenSource? _autocompleteCts;
    private readonly ISqlParser _sqlParser;
    private readonly IAutocompleteService _autocompleteService;
    private SqlContext? _lastContext;
    private List<AutocompleteItem>? _cachedCandidates;

    public ObservableCollection<AutocompleteItemViewModel> AutocompleteSuggestions { get; } = new();

    public HighlightedSqlEditor()
    {
        InitializeComponent();

        // Syntax highlighting timer (150ms debounce)
        _updateTimer = new System.Timers.Timer(150);
        _updateTimer.AutoReset = false;
        _updateTimer.Elapsed += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                UpdateHighlighting();
            });
        };

        // Autocomplete timer (30ms debounce)
        _autocompleteTimer = new System.Timers.Timer(30);
        _autocompleteTimer.AutoReset = false;
        _autocompleteTimer.Elapsed += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                _ = UpdateAutocompleteFilterAsync());
        };

        _sqlParser = new SqlParser();
        _autocompleteService = new AutocompleteService(_sqlParser);

        var editor = this.FindControl<TextBox>("Editor");

        if (editor is not null)
        {
            editor.TextChanged += (_, _) =>
            {
                if (!_isUpdatingText)
                {
                    Log.Debug("Editor TextChanged, scheduling update");
                    Text = editor.Text ?? string.Empty;

                    // Debounce syntax highlighting updates
                    _updateTimer?.Stop();
                    _updateTimer?.Start();

                    // Defer autocomplete to next UI tick so CaretIndex is updated
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        _ = HandleAutocompleteAsync(editor));
                }
            };

            // Use tunneling event so we intercept Tab/Enter before AcceptsTab processes them
            editor.AddHandler(KeyDownEvent, OnEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            editor.LostFocus += OnEditorLostFocus;
        }

        // Wire up popup ListBox
        var suggestionsList = this.FindControl<ListBox>("SuggestionsList");
        if (suggestionsList is not null)
        {
            suggestionsList.ItemsSource = AutocompleteSuggestions;
            suggestionsList.Tapped += (_, _) => AcceptSelectedSuggestion();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            var editor = this.FindControl<TextBox>("Editor");
            if (editor is not null && editor.Text != Text)
            {
                _isUpdatingText = true;
                editor.Text = Text;
                _isUpdatingText = false;
                
                // Update highlighting immediately when Text property changes from outside
                UpdateHighlighting();
            }
        }
        else if (change.Property == IsDarkThemeProperty)
        {
            UpdateHighlighting();
        }
    }

    private async Task HandleAutocompleteAsync(TextBox editor)
    {
        if (AutocompleteCache is null)
        {
            Log.Debug("Autocomplete skipped: cache is null");
            return;
        }

        var text = editor.Text ?? string.Empty;
        var cursorPos = editor.CaretIndex;

        // Clamp cursor to text bounds
        if (cursorPos > text.Length)
            cursorPos = text.Length;
        if (cursorPos < 0)
            cursorPos = 0;

        if (IsSpecialTrigger(text, cursorPos))
        {
            Log.Debug("HandleAutocomplete: special trigger at pos={CursorPos}", cursorPos);
            await ShowAutocompleteAsync(text, cursorPos);
        }
        else if (IsAutocompleteVisible())
        {
            // Debounced filter while popup visible
            _autocompleteTimer?.Stop();
            _autocompleteTimer?.Start();
        }
    }

    private bool IsSpecialTrigger(string text, int cursorPos)
    {
        if (cursorPos <= 0 || cursorPos > text.Length)
            return false;

        var lastChar = text[cursorPos - 1];

        // Trigger on dot: alias.
        if (lastChar == '.')
            return true;

        // Trigger on quote after dot: alias."
        if (lastChar == '"' && cursorPos >= 2 && text[cursorPos - 2] == '.')
            return true;

        // Trigger after FROM/JOIN to show table names
        // Matches: "FROM " or "FROM \"" or "JOIN " or "JOIN \""
        if (lastChar == ' ' || lastChar == '"')
        {
            var searchPos = lastChar == '"' ? cursorPos - 1 : cursorPos;
            var beforeToken = text.Substring(0, searchPos).TrimEnd();
            if (beforeToken.EndsWith("FROM", StringComparison.OrdinalIgnoreCase) ||
                beforeToken.EndsWith("JOIN", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        var popup = this.FindControl<Popup>("AutocompletePopup");
        var suggestionsList = this.FindControl<ListBox>("SuggestionsList");

        if (popup?.IsOpen == true && suggestionsList is not null)
        {
            switch (e.Key)
            {
                case Key.Down:
                    SelectNext(suggestionsList);
                    e.Handled = true;
                    break;

                case Key.Up:
                    SelectPrevious(suggestionsList);
                    e.Handled = true;
                    break;

                case Key.Enter:
                case Key.Tab:
                    AcceptSelectedSuggestion();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    HideAutocomplete();
                    e.Handled = true;
                    break;
            }
        }
        else if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var editor = (TextBox)sender!;
            _ = ShowAutocompleteAsync(editor.Text ?? string.Empty, editor.CaretIndex);
            e.Handled = true;
        }
    }

    private async Task ShowAutocompleteAsync(string sql, int cursorPos)
    {
        if (AutocompleteCache is null)
            return;

        _autocompleteCts?.Cancel();
        _autocompleteCts = new CancellationTokenSource();
        var ct = _autocompleteCts.Token;

        try
        {
            var cache = AutocompleteCache;
            var suggestions = await Task.Run(() =>
            {
                var context = _sqlParser.GetContextAtPosition(sql, cursorPos);

                Log.Information("Autocomplete: clause={Clause}, alias={Alias}, partial=[{Partial}], cursorPos={CursorPos}, textLen={TextLen}, sql=[{Sql}]",
                    context.CurrentClause, context.CurrentAlias ?? "(none)", context.PartialWord,
                    cursorPos, sql.Length, sql);

                // Fast path: filter existing if context unchanged
                if (_lastContext?.IsMaterialChange(context) == false && _cachedCandidates is not null)
                {
                    return FilterSuggestions(_cachedCandidates, context.PartialWord);
                }

                // Slow path: regenerate
                _cachedCandidates = _autocompleteService.GetSuggestionsAsync(
                    sql, cursorPos, cache, ct).GetAwaiter().GetResult();
                _lastContext = context;

                Log.Information("Autocomplete generated {Count} candidates, types: {Types}",
                    _cachedCandidates.Count,
                    string.Join(",", _cachedCandidates.Select(c => c.Type).Distinct()));
                return _cachedCandidates;
            }, ct);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateAutocompleteUI(suggestions);
            }, Avalonia.Threading.DispatcherPriority.Normal, ct);
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Autocomplete request cancelled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error showing autocomplete");
        }
    }

    private async Task UpdateAutocompleteFilterAsync()
    {
        var editor = this.FindControl<TextBox>("Editor");
        if (editor is not null)
        {
            await ShowAutocompleteAsync(editor.Text ?? string.Empty, editor.CaretIndex);
        }
    }

    private List<AutocompleteItem> FilterSuggestions(List<AutocompleteItem> candidates, string filter)
    {
        if (string.IsNullOrEmpty(filter))
            return candidates.Take(100).ToList();

        return candidates
            .Where(c => c.DisplayText.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            .Take(100)
            .ToList();
    }

    private void UpdateAutocompleteUI(List<AutocompleteItem> suggestions)
    {
        AutocompleteSuggestions.Clear();

        if (!suggestions.Any())
        {
            HideAutocomplete();
            return;
        }

        foreach (var item in suggestions)
        {
            AutocompleteSuggestions.Add(new AutocompleteItemViewModel
            {
                DisplayText = item.DisplayText,
                InsertText = item.InsertText,
                Description = item.Description,
                Type = item.Type
            });
        }

        var popup = this.FindControl<Popup>("AutocompletePopup");
        var suggestionsList = this.FindControl<ListBox>("SuggestionsList");

        if (popup is not null && suggestionsList is not null)
        {
            suggestionsList.SelectedIndex = 0;
            popup.IsOpen = true;
        }
    }

    private void AcceptSelectedSuggestion()
    {
        var suggestionsList = this.FindControl<ListBox>("SuggestionsList");
        var editor = this.FindControl<TextBox>("Editor");

        if (suggestionsList?.SelectedItem is AutocompleteItemViewModel selected && editor is not null)
        {
            var cursorPos = editor.CaretIndex;
            var text = editor.Text ?? string.Empty;

            // Find start of partial word
            int wordStart = cursorPos;
            while (wordStart > 0 && (char.IsLetterOrDigit(text[wordStart - 1]) || text[wordStart - 1] == '_'))
            {
                wordStart--;
            }

            // Replace partial word with selected suggestion
            var before = text.Substring(0, wordStart);
            var after = cursorPos < text.Length ? text.Substring(cursorPos) : string.Empty;
            var newText = before + selected.InsertText + after;

            _isUpdatingText = true;
            editor.Text = newText;
            editor.CaretIndex = wordStart + selected.InsertText.Length;
            _isUpdatingText = false;

            Text = newText;
            HideAutocomplete();

            _updateTimer?.Stop();
            _autocompleteTimer?.Stop();
            _updateTimer?.Start();

            // Return focus to editor after accepting suggestion
            editor.Focus();
        }
    }

    private void SelectNext(ListBox list)
    {
        if (list.SelectedIndex < list.ItemCount - 1)
        {
            list.SelectedIndex++;
            list.ScrollIntoView(list.SelectedItem!);
        }
    }

    private void SelectPrevious(ListBox list)
    {
        if (list.SelectedIndex > 0)
        {
            list.SelectedIndex--;
            list.ScrollIntoView(list.SelectedItem!);
        }
    }

    private void OnEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Delay hiding so clicks on the popup ListBox can register first
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var popup = this.FindControl<Popup>("AutocompletePopup");
            var suggestionsList = this.FindControl<ListBox>("SuggestionsList");
            var editor = this.FindControl<TextBox>("Editor");

            // Don't hide if focus moved to the suggestion list
            if (suggestionsList?.IsFocused == true || suggestionsList?.IsPointerOver == true)
                return;

            HideAutocomplete();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void HideAutocomplete()
    {
        var popup = this.FindControl<Popup>("AutocompletePopup");
        if (popup is not null)
        {
            popup.IsOpen = false;
        }

        AutocompleteSuggestions.Clear();
        _lastContext = null;
        _cachedCandidates = null;
    }

    private bool IsAutocompleteVisible()
    {
        var popup = this.FindControl<Popup>("AutocompletePopup");
        return popup?.IsOpen == true;
    }

    private void UpdateHighlighting()
    {
        var highlightedText = this.FindControl<TextBlock>("HighlightedText");
        if (highlightedText is null)
            return;

        var segments = SqlSyntaxHighlighter.Highlight(Text);
        
        highlightedText.Inlines?.Clear();
        
        foreach (var segment in segments)
        {
            var run = new Run(segment.Text)
            {
                Foreground = SqlSyntaxHighlighter.GetBrush(segment.Type, IsDarkTheme)
            };
            highlightedText.Inlines?.Add(run);
        }

        Log.Debug("Updated syntax highlighting with {Count} segments", segments.Count);
    }

    public void Focus()
    {
        var editor = this.FindControl<TextBox>("Editor");
        editor?.Focus();
    }

    public void AddKeyBinding(KeyBinding binding)
    {
        var editor = this.FindControl<TextBox>("Editor");
        editor?.KeyBindings.Add(binding);
    }

    public int CaretIndex => this.FindControl<TextBox>("Editor")?.CaretIndex ?? 0;

    public string SelectedText => this.FindControl<TextBox>("Editor")?.SelectedText ?? string.Empty;

    public bool HasSelection
    {
        get
        {
            var editor = this.FindControl<TextBox>("Editor");
            return editor is not null && editor.SelectionStart != editor.SelectionEnd;
        }
    }

    public void ReplaceSelection(string replacement)
    {
        var editor = this.FindControl<TextBox>("Editor");
        if (editor is null)
        {
            return;
        }

        var text = editor.Text ?? string.Empty;
        var start = Math.Min(editor.SelectionStart, editor.SelectionEnd);
        var end = Math.Max(editor.SelectionStart, editor.SelectionEnd);
        var length = Math.Max(0, end - start);
        if (start > text.Length)
        {
            start = text.Length;
            length = 0;
        }

        var updated = text.Remove(start, Math.Min(length, text.Length - start)).Insert(start, replacement);
        _isUpdatingText = true;
        editor.Text = updated;
        editor.CaretIndex = start + replacement.Length;
        editor.SelectionStart = editor.CaretIndex;
        editor.SelectionEnd = editor.CaretIndex;
        _isUpdatingText = false;

        Text = updated;
        _updateTimer?.Stop();
        _updateTimer?.Start();
    }

    public bool FindNext(string searchText, bool matchCase = false)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            return false;
        }

        var editor = this.FindControl<TextBox>("Editor");
        if (editor is null)
        {
            return false;
        }

        var text = editor.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return false;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var startIndex = Math.Max(editor.SelectionEnd, 0);
        var found = text.IndexOf(searchText, startIndex, comparison);
        if (found < 0 && startIndex > 0)
        {
            found = text.IndexOf(searchText, 0, comparison);
        }

        if (found < 0)
        {
            return false;
        }

        editor.Focus();
        editor.SelectionStart = found;
        editor.SelectionEnd = found + searchText.Length;
        editor.CaretIndex = editor.SelectionEnd;
        return true;
    }

    public bool ReplaceNext(string searchText, string replacementText, bool matchCase = false)
    {
        var editor = this.FindControl<TextBox>("Editor");
        if (editor is null || string.IsNullOrEmpty(searchText))
        {
            return false;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!string.IsNullOrEmpty(editor.SelectedText) &&
            string.Equals(editor.SelectedText, searchText, comparison))
        {
            ReplaceSelection(replacementText);
        }

        return FindNext(searchText, matchCase);
    }

    public int ReplaceAll(string searchText, string replacementText, bool matchCase = false)
    {
        var editor = this.FindControl<TextBox>("Editor");
        if (editor is null || string.IsNullOrEmpty(searchText))
        {
            return 0;
        }

        var source = editor.Text ?? string.Empty;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var count = 0;
        var startIndex = 0;

        while (startIndex <= source.Length)
        {
            var found = source.IndexOf(searchText, startIndex, comparison);
            if (found < 0)
            {
                break;
            }

            source = source.Remove(found, searchText.Length).Insert(found, replacementText);
            startIndex = found + replacementText.Length;
            count++;
        }

        if (count == 0)
        {
            return 0;
        }

        _isUpdatingText = true;
        editor.Text = source;
        editor.CaretIndex = Math.Min(startIndex, source.Length);
        editor.SelectionStart = editor.CaretIndex;
        editor.SelectionEnd = editor.CaretIndex;
        _isUpdatingText = false;

        Text = source;
        _updateTimer?.Stop();
        _updateTimer?.Start();
        return count;
    }
}

/// <summary>
/// ViewModel for autocomplete items displayed in the popup.
/// </summary>
public class AutocompleteItemViewModel
{
    public string DisplayText { get; set; } = string.Empty;
    public string InsertText { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AutocompleteItemType Type { get; set; }

    public string Icon => Type switch
    {
        AutocompleteItemType.Table => "📋",
        AutocompleteItemType.Column => "📊",
        AutocompleteItemType.Keyword => "⚡",
        AutocompleteItemType.Function => "🔧",
        _ => "•"
    };
}
