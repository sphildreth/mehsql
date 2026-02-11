using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Serilog;

namespace MehSql.App.Controls;

public partial class HighlightedSqlEditor : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<HighlightedSqlEditor, string>(nameof(Text), defaultValue: string.Empty);

    public static readonly StyledProperty<bool> IsDarkThemeProperty =
        AvaloniaProperty.Register<HighlightedSqlEditor, bool>(nameof(IsDarkTheme), defaultValue: true);

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

    private bool _isUpdatingText;

    public HighlightedSqlEditor()
    {
        InitializeComponent();
        
        var editor = this.FindControl<TextBox>("Editor");

        if (editor is not null)
        {
            editor.TextChanged += (_, _) =>
            {
                if (!_isUpdatingText)
                {
                    Text = editor.Text ?? string.Empty;
                    UpdateHighlighting();
                }
            };
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
            }
            UpdateHighlighting();
        }
        else if (change.Property == IsDarkThemeProperty)
        {
            UpdateHighlighting();
        }
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
}
