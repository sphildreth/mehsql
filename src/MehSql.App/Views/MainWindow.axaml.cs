using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MehSql.App.Services;
using MehSql.App.ViewModels;
using MehSql.Core.Import;
using MehSql.Core.Querying;
using ReactiveUI;
using Serilog;

namespace MehSql.App.Views;

public partial class MainWindow : Window
{
    private readonly ThemeManager _themeManager;
    private double[] _columnWidths = [];
    private IReadOnlyList<ColumnInfo> _currentColumns = [];
    private bool _scrollSyncAttached;

    public MainWindow() : this(new ThemeManager())
    {
    }

    public MainWindow(ThemeManager themeManager)
    {
        _themeManager = themeManager ?? throw new ArgumentNullException(nameof(themeManager));
        Log.Logger.Information("MainWindow constructor starting");
        InitializeComponent();
        Log.Logger.Information("InitializeComponent completed");

        // Enable drag and drop
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        // Set up SQL syntax highlighting
        InitializeSqlEditor();
        ApplyCurrentTheme();

        // Register global keyboard shortcuts
        RegisterKeyboardShortcuts();

        // Restore saved window position/size and save on close
        Opened += OnWindowOpened;
        Closing += OnWindowClosing;
        
        Log.Logger.Information("MainWindow constructor completed");

        // Rebuild results table when Columns changes; populate recent files menus
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Results.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(vm.Results.Columns))
                    {
                        RebuildResultsTable(vm.Results);
                    }
                };

                // Sync initial values from ViewModel to editor
                SqlEditor.Text = vm.SqlText ?? "";
                SqlEditor.AutocompleteCache = vm.AutocompleteCache;

                // ViewModel → Editor sync
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(vm.SqlText))
                    {
                        Log.Logger.Debug("SqlText property changed to: {SqlText}", vm.SqlText?.Substring(0, Math.Min(vm.SqlText?.Length ?? 0, 100)));
                        if (SqlEditor.Text != vm.SqlText)
                        {
                            Log.Logger.Debug("Updating SqlEditor.Text from ViewModel");
                            SqlEditor.Text = vm.SqlText ?? "";
                        }
                    }
                    else if (args.PropertyName == nameof(vm.AutocompleteCache))
                    {
                        SqlEditor.AutocompleteCache = vm.AutocompleteCache;
                    }
                    else if (args.PropertyName == nameof(vm.PendingFindText) &&
                             !string.IsNullOrWhiteSpace(vm.PendingFindText))
                    {
                        FindTextBox.Text = vm.PendingFindText;
                        ShowFindPanel(showReplace: false);
                        SqlEditor.FindNext(vm.PendingFindText);
                        vm.ClearPendingFindText();
                    }
                };

                RebuildRecentFilesMenu(vm);
                vm.RecentFiles.CollectionChanged += (_, _) => RebuildRecentFilesMenu(vm);
                RebuildRecentSqlFilesMenu(vm);
                vm.RecentSqlFiles.CollectionChanged += (_, _) => RebuildRecentSqlFilesMenu(vm);
            }
        };
    }

    private void InitializeSqlEditor()
    {
        // Event handlers are now attached in XAML
        // Loaded, PointerPressed, and GotFocus will call their respective handlers
    }

    private void OnSqlEditorGotFocus(object? sender, RoutedEventArgs e)
    {
        Log.Logger.Information("SqlEditor GotFocus event fired");
    }

    private void OnSqlEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Log.Logger.Information("SqlEditor PointerPressed event fired");
        SqlEditor.Focus();
    }

    private void OnSqlEditorLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Controls.HighlightedSqlEditor editor)
        {
            editor.AddKeyBinding(new KeyBinding
            {
                Gesture = new KeyGesture(Key.Enter, KeyModifiers.Control),
                Command = ReactiveCommand.CreateFromTask(async () => await ExecuteRunFromEditorAsync())
            });
            editor.AddKeyBinding(new KeyBinding
            {
                Gesture = new KeyGesture(Key.F5),
                Command = ReactiveCommand.CreateFromTask(async () => await ExecuteRunFromEditorAsync())
            });
            editor.AddKeyBinding(new KeyBinding
            {
                Gesture = new KeyGesture(Key.F, KeyModifiers.Control),
                Command = ReactiveCommand.Create(() => ShowFindPanel(showReplace: false))
            });
            editor.AddKeyBinding(new KeyBinding
            {
                Gesture = new KeyGesture(Key.H, KeyModifiers.Control),
                Command = ReactiveCommand.Create(() => ShowFindPanel(showReplace: true))
            });
            editor.AddKeyBinding(new KeyBinding
            {
                Gesture = new KeyGesture(Key.F, KeyModifiers.Control | KeyModifiers.Shift),
                Command = ReactiveCommand.Create(() => OnFormatSqlClick(null, new RoutedEventArgs()))
            });
        }
    }

    private void RegisterKeyboardShortcuts()
    {
        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.N, KeyModifiers.Control),
            Command = ReactiveCommand.Create(() => OnNewDatabaseClick(null, new RoutedEventArgs()))
        });

        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.O, KeyModifiers.Control),
            Command = ReactiveCommand.Create(() => OnOpenDatabaseClick(null, new RoutedEventArgs()))
        });

        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.O, KeyModifiers.Control | KeyModifiers.Shift),
            Command = ReactiveCommand.Create(() => OnOpenSqlFileClick(null, new RoutedEventArgs()))
        });

        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.S, KeyModifiers.Control),
            Command = ReactiveCommand.Create(() => OnSaveSqlFileClick(null, new RoutedEventArgs()))
        });

        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift),
            Command = ReactiveCommand.Create(() => OnSaveSqlFileAsClick(null, new RoutedEventArgs()))
        });

        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Control),
            Command = ReactiveCommand.Create(() => OnPreferencesClick(null, new RoutedEventArgs()))
        });

        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.T, KeyModifiers.Control | KeyModifiers.Alt),
            Command = ReactiveCommand.Create(() => OnToggleThemeClick(null, new RoutedEventArgs()))
        });

        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.T, KeyModifiers.Control),
            Command = ReactiveCommand.Create(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.AddNewQueryTab();
                }
            })
        });

        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.W, KeyModifiers.Control),
            Command = ReactiveCommand.Create(() => OnCloseCurrentTabClick(null, new RoutedEventArgs()))
        });

        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Escape),
            Command = ReactiveCommand.Create(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.CancelQueryCommand.Execute(null);
                }
            })
        });

        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Enter, KeyModifiers.Control),
            Command = ReactiveCommand.CreateFromTask(async () => await ExecuteRunFromEditorAsync())
        });

        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.E, KeyModifiers.Control),
            Command = ReactiveCommand.Create(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.Results.ExplainQueryCommand.Execute(null);
                }
            })
        });

        this.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.E, KeyModifiers.Control | KeyModifiers.Shift),
            Command = ReactiveCommand.Create(() =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.Results.ExplainAnalyzeCommand.Execute(null);
                }
            })
        });
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var bounds = vm.SettingsService.Settings.Window;
        if (bounds is null)
            return;

        // Restore maximized state
        if (Enum.TryParse<WindowState>(bounds.State, out var state) && state == WindowState.Maximized)
        {
            // Position first so it maximizes on the correct screen
            Position = new PixelPoint((int)bounds.X, (int)bounds.Y);
            WindowState = WindowState.Maximized;
            return;
        }

        // Validate the saved position is on a visible screen
        var screens = Screens;
        var savedCenter = new PixelPoint((int)(bounds.X + bounds.Width / 2), (int)(bounds.Y + bounds.Height / 2));
        var onScreen = screens.All.Any(s => s.Bounds.Contains(savedCenter));

        if (onScreen)
        {
            Position = new PixelPoint((int)bounds.X, (int)bounds.Y);
            Width = bounds.Width;
            Height = bounds.Height;
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        // Save position from the Normal state bounds (not maximized bounds)
        var wb = new WindowBounds
        {
            Width = Width,
            Height = Height,
            State = WindowState.ToString()
        };

        // When maximized, save the position before maximizing so we restore to the right screen
        if (WindowState == WindowState.Maximized)
        {
            // Use current position — on most platforms this is the maximized screen origin
            wb.X = Position.X;
            wb.Y = Position.Y;
        }
        else
        {
            wb.X = Position.X;
            wb.Y = Position.Y;
        }

        vm.SettingsService.Settings.Window = wb;
        vm.SettingsService.Save();
    }

    private void RebuildRecentFilesMenu(MainWindowViewModel vm)
    {
        RecentFilesMenu.Items.Clear();

        if (vm.RecentFiles.Count == 0)
        {
            RecentFilesMenu.IsEnabled = false;
            RecentFilesMenu.Items.Add(new MenuItem { Header = "(none)" });
            return;
        }

        RecentFilesMenu.IsEnabled = true;
        foreach (var path in vm.RecentFiles)
        {
            var item = new MenuItem
            {
                Header = System.IO.Path.GetFileName(path),
                Tag = path
            };
            // Show full path as tooltip
            ToolTip.SetTip(item, path);
            item.Click += async (_, _) =>
            {
                if (item.Tag is string filePath)
                {
                    await vm.OpenDatabaseAsync(filePath);
                }
            };
            RecentFilesMenu.Items.Add(item);
        }
    }

    private void RebuildRecentSqlFilesMenu(MainWindowViewModel vm)
    {
        RecentSqlFilesMenu.Items.Clear();

        if (vm.RecentSqlFiles.Count == 0)
        {
            RecentSqlFilesMenu.IsEnabled = false;
            RecentSqlFilesMenu.Items.Add(new MenuItem { Header = "(none)" });
            return;
        }

        RecentSqlFilesMenu.IsEnabled = true;
        foreach (var path in vm.RecentSqlFiles)
        {
            var item = new MenuItem
            {
                Header = System.IO.Path.GetFileName(path),
                Tag = path
            };

            ToolTip.SetTip(item, path);
            item.Click += async (_, _) =>
            {
                if (item.Tag is string filePath)
                {
                    await vm.OpenSqlFileInTabAsync(filePath, openInNewTab: true);
                }
            };

            RecentSqlFilesMenu.Items.Add(item);
        }
    }

    private void RebuildResultsTable(ResultsViewModel results)
    {
        var sw = Stopwatch.StartNew();

        ResultsHeaderRow.Children.Clear();
        ResultsItemsControl.ItemsSource = null;
        ResultsItemsControl.ItemTemplate = null;
        _scrollSyncAttached = false;

        if (results.Columns.Count == 0) return;

        _currentColumns = results.Columns;
        _columnWidths = ComputeColumnWidths(_currentColumns, results.Rows);

        BuildHeader();
        BuildItemTemplate();
        ResultsItemsControl.ItemsSource = results.Rows;
        SyncHeaderScroll();

        sw.Stop();
        results.SetUiBindTime(sw.Elapsed);
    }

    private void BuildHeader()
    {
        ResultsHeaderRow.Children.Clear();
        var resizeGripColor = _themeManager.CurrentTheme == ThemeMode.Light ? "#C8C8C8" : "#555555";
        for (var i = 0; i < _currentColumns.Count; i++)
        {
            // Column header text
            ResultsHeaderRow.Children.Add(new TextBlock
            {
                Text = _currentColumns[i].Name,
                Width = _columnWidths[i],
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(6, 4),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            // Resize grip between columns
            var grip = new Border
            {
                Width = 6,
                Cursor = new Cursor(StandardCursorType.SizeWestEast),
                Background = Brushes.Transparent,
                Tag = i,
            };
            var gripLine = new Border
            {
                Width = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Color.Parse(resizeGripColor))
            };
            grip.Child = gripLine;
            grip.PointerPressed += OnGripPointerPressed;
            grip.PointerMoved += OnGripPointerMoved;
            grip.PointerReleased += OnGripPointerReleased;
            ResultsHeaderRow.Children.Add(grip);
        }
    }

    private void BuildItemTemplate()
    {
        var widths = _columnWidths;
        var columns = _currentColumns;
        ResultsItemsControl.ItemTemplate = new FuncDataTemplate<Dictionary<string, object?>>((row, _) =>
        {
            if (row is null) return new TextBlock();
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            for (var i = 0; i < columns.Count; i++)
            {
                var val = row.TryGetValue(columns[i].Name, out var v) ? v?.ToString() ?? "" : "";
                var textBlock = new TextBlock
                {
                    Text = val,
                    Padding = new Thickness(6, 3),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                var cell = new Border
                {
                    Width = widths[i],
                    Child = textBlock
                };

                var contextMenu = new ContextMenu();
                var copyCell = new MenuItem { Header = "Copy Cell" };
                copyCell.Click += async (_, _) => await CopyToClipboardAsync(val);
                var copyRowTsv = new MenuItem { Header = "Copy Row (TSV)" };
                copyRowTsv.Click += async (_, _) => await CopyToClipboardAsync(BuildDelimitedRow(row, columns, "\t"));
                var copyRowCsv = new MenuItem { Header = "Copy Row (CSV)" };
                copyRowCsv.Click += async (_, _) => await CopyToClipboardAsync(BuildDelimitedRow(row, columns, ","));

                contextMenu.Items.Add(copyCell);
                contextMenu.Items.Add(copyRowTsv);
                contextMenu.Items.Add(copyRowCsv);
                cell.ContextMenu = contextMenu;
                panel.Children.Add(cell);

                // Spacer matching the grip width
                if (i < columns.Count - 1)
                {
                    panel.Children.Add(new Border { Width = 6 });
                }
            }
            return panel;
        });
    }

    /// <summary>
    /// Syncs the header ScrollViewer with the ListBox's internal horizontal scroll.
    /// </summary>
    private void SyncHeaderScroll()
    {
        if (_scrollSyncAttached) return;

        ResultsItemsControl.TemplateApplied += (_, _) =>
        {
            var sv = ResultsItemsControl.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();
            if (sv is null) return;

            sv.ScrollChanged += (_, _) =>
            {
                HeaderScrollViewer.Offset = new Vector(sv.Offset.X, 0);
            };
        };
        _scrollSyncAttached = true;
    }

    #region Column Resize

    private int _resizingColumnIndex = -1;
    private Point _resizeStartPoint;
    private double _resizeStartWidth;

    private void OnGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border grip || grip.Tag is not int colIndex) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _resizingColumnIndex = colIndex;
        _resizeStartPoint = e.GetPosition(this);
        _resizeStartWidth = _columnWidths[colIndex];
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    private void OnGripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resizingColumnIndex < 0) return;

        var current = e.GetPosition(this);
        var delta = current.X - _resizeStartPoint.X;
        var newWidth = Math.Max(40, _resizeStartWidth + delta);
        _columnWidths[_resizingColumnIndex] = newWidth;

        // Update header TextBlock width (children alternate: TextBlock, Grip, TextBlock, Grip, ...)
        var headerChild = ResultsHeaderRow.Children[_resizingColumnIndex * 2];
        if (headerChild is TextBlock tb)
        {
            tb.Width = newWidth;
        }

        // Update all visible row cells
        UpdateVisibleRowWidths(_resizingColumnIndex, newWidth);
        e.Handled = true;
    }

    private void OnGripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizingColumnIndex < 0) return;
        e.Pointer.Capture(null);
        _resizingColumnIndex = -1;
        e.Handled = true;
    }

    /// <summary>
    /// Updates the width of a specific column in all currently realized ListBox items.
    /// </summary>
    private void UpdateVisibleRowWidths(int colIndex, double newWidth)
    {
        // Each row panel has: Border(Cell), Spacer, Border(Cell), Spacer, ...
        var childIndex = colIndex > 0 ? colIndex * 2 : 0;

        foreach (var container in ResultsItemsControl.GetVisualDescendants().OfType<ListBoxItem>())
        {
            var presenter = container.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault();
            var panel = presenter?.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault();
            if (panel is null || childIndex >= panel.Children.Count) continue;

            if (panel.Children[childIndex] is Border border)
            {
                border.Width = newWidth;
            }
        }
    }

    #endregion

    /// <summary>
    /// Computes column widths by sampling the first 50 rows to find a reasonable width.
    /// </summary>
    private static double[] ComputeColumnWidths(
        IReadOnlyList<ColumnInfo> columns,
        ObservableCollection<Dictionary<string, object?>> rows)
    {
        const double minWidth = 60;
        const double maxWidth = 400;
        const double charWidth = 7.5;
        const double padding = 16;

        var widths = new double[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            // Start with header text width
            var maxLen = columns[i].Name.Length;

            // Sample first 50 rows
            var sampleCount = Math.Min(rows.Count, 50);
            for (var r = 0; r < sampleCount; r++)
            {
                if (rows[r].TryGetValue(columns[i].Name, out var val))
                {
                    var len = val?.ToString()?.Length ?? 0;
                    if (len > maxLen) maxLen = len;
                }
            }

            widths[i] = Math.Clamp(maxLen * charWidth + padding, minWidth, maxWidth);
        }

        return widths;
    }

    private static string BuildDelimitedRow(
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyList<ColumnInfo> columns,
        string delimiter)
    {
        var values = new List<string>(columns.Count);
        foreach (var column in columns)
        {
            var value = row.TryGetValue(column.Name, out var raw) ? raw?.ToString() ?? string.Empty : string.Empty;
            if (delimiter == ",")
            {
                var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
                values.Add($"\"{escaped}\"");
            }
            else
            {
                values.Add(value);
            }
        }

        return string.Join(delimiter, values);
    }

    private async Task CopyToClipboardAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text ?? string.Empty);
        }
    }

    private async void OnOpenDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Database",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("DecentDB Database") { Patterns = ["*.ddb"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0)
        {
            await vm.OpenDatabaseAsync(files[0].Path.LocalPath);
        }
    }

    private async void OnOpenSqlFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open SQL File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("SQL Files") { Patterns = ["*.sql"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0)
        {
            try
            {
                await vm.OpenSqlFileInTabAsync(files[0].Path.LocalPath, openInNewTab: true);
            }
            catch (Exception ex)
            {
                Serilog.Log.Logger.Warning(ex, "Failed to read SQL file: {Path}", files[0].Path.LocalPath);
            }
        }
    }

    private async void OnSaveSqlFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (!string.IsNullOrWhiteSpace(vm.ActiveSqlFilePath))
        {
            await vm.SaveActiveSqlFileAsync(vm.ActiveSqlFilePath);
            return;
        }

        await SaveSqlAsAsync(vm);
    }

    private async void OnSaveSqlFileAsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        await SaveSqlAsAsync(vm);
    }

    private async Task SaveSqlAsAsync(MainWindowViewModel vm)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save SQL File",
            DefaultExtension = "sql",
            SuggestedFileName = vm.SelectedQueryTab?.Title is { Length: > 0 } title ? $"{title}.sql" : "query.sql",
            FileTypeChoices =
            [
                new FilePickerFileType("SQL Files") { Patterns = ["*.sql"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (file is not null)
        {
            await vm.SaveActiveSqlFileAsync(file.Path.LocalPath);
        }
    }

    private async void OnOpenInExternalEditorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedQueryTab is null)
        {
            return;
        }

        var path = vm.ActiveSqlFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(Path.GetTempPath(), $"mehsql_{Guid.NewGuid():N}.sql");
            await vm.SaveActiveSqlFileAsync(path);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("External Editor", $"Unable to open file in external editor:\n{ex.Message}");
        }
    }

    private async void OnRunQueryClick(object? sender, RoutedEventArgs e)
    {
        await ExecuteRunFromEditorAsync();
    }

    private async Task ExecuteRunFromEditorAsync()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var request = SqlExecutionPlanner.Create(SqlEditor.Text, SqlEditor.SelectedText, SqlEditor.CaretIndex);
        await vm.ExecutePlannedQueryAsync(request);
    }

    private void OnExplainClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Results.ExplainQueryCommand.Execute(null);
        }
    }

    private void OnExplainAnalyzeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Results.ExplainAnalyzeCommand.Execute(null);
        }
    }

    private void OnShowFindClick(object? sender, RoutedEventArgs e)
    {
        ShowFindPanel(showReplace: false);
    }

    private void OnShowReplaceClick(object? sender, RoutedEventArgs e)
    {
        ShowFindPanel(showReplace: true);
    }

    private void ShowFindPanel(bool showReplace)
    {
        FindReplacePanel.IsVisible = true;
        ReplaceTextBox.IsVisible = showReplace;
        FindTextBox.Focus();
        if (!string.IsNullOrWhiteSpace(SqlEditor.SelectedText))
        {
            FindTextBox.Text = SqlEditor.SelectedText;
        }
    }

    private void OnCloseFindReplaceClick(object? sender, RoutedEventArgs e)
    {
        FindReplacePanel.IsVisible = false;
    }

    private void OnFindNextClick(object? sender, RoutedEventArgs e)
    {
        if (!SqlEditor.FindNext(FindTextBox.Text ?? string.Empty))
        {
            _ = ShowErrorDialogAsync("Find", "No match found.");
        }
    }

    private void OnReplaceClick(object? sender, RoutedEventArgs e)
    {
        SqlEditor.ReplaceNext(FindTextBox.Text ?? string.Empty, ReplaceTextBox.Text ?? string.Empty);
    }

    private void OnReplaceAllClick(object? sender, RoutedEventArgs e)
    {
        var replaced = SqlEditor.ReplaceAll(FindTextBox.Text ?? string.Empty, ReplaceTextBox.Text ?? string.Empty);
        if (replaced == 0)
        {
            _ = ShowErrorDialogAsync("Replace", "No matches found.");
        }
    }

    private void OnFormatSqlClick(object? sender, RoutedEventArgs e)
    {
        if (SqlEditor.HasSelection)
        {
            var formattedSelection = SimpleSqlFormatter.Format(SqlEditor.SelectedText);
            SqlEditor.ReplaceSelection(formattedSelection);
            return;
        }

        SqlEditor.Text = SimpleSqlFormatter.Format(SqlEditor.Text);
    }

    private void OnCloseCurrentTabClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CloseQueryTab(vm.SelectedQueryTab);
        }
    }

    private async void OnRenameTabClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not MenuItem { DataContext: QueryTabViewModel tab }) return;

        var newName = await ShowInputDialogAsync("Rename Tab", "Tab name:", tab.Title);
        if (!string.IsNullOrWhiteSpace(newName))
        {
            vm.RenameQueryTab(tab, newName);
        }
    }

    private async void OnNewDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create New Database",
            DefaultExtension = "ddb",
            FileTypeChoices =
            [
                new FilePickerFileType("DecentDB Database") { Patterns = ["*.ddb"] }
            ]
        });

        if (file != null)
        {
            await vm.CreateDatabaseAsync(file.Path.LocalPath);
        }
    }

    private void OnToggleThemeClick(object? sender, RoutedEventArgs e)
    {
        _themeManager.ToggleTheme();
        ApplyThemeChanges();
        SaveSelectedTheme();
    }

    private async void OnImportDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Step 1: Pick file
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Database or Dump File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("All Supported Formats") { Patterns = ["*.db", "*.sqlite", "*.sqlite3", "*.sql", "*.gz", "*.zip", "*.tar.gz", "*.tgz"] },
                new FilePickerFileType("SQLite Databases") { Patterns = ["*.db", "*.sqlite", "*.sqlite3"] },
                new FilePickerFileType("SQL Dump Files") { Patterns = ["*.sql"] },
                new FilePickerFileType("Compressed Archives") { Patterns = ["*.gz", "*.zip", "*.tar.gz", "*.tgz"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count == 0) return;
        var filePath = files[0].Path.LocalPath;

        await ImportFromPathAsync(vm, filePath);
    }

    private async void OnImportDumpFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select MySQL Shell Dump Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;
        var folderPath = folders[0].Path.LocalPath;

        await ImportFromPathAsync(vm, folderPath);
    }

    private async Task ImportFromPathAsync(MainWindowViewModel vm, string inputPath)
    {
        string? tempDir = null;
        try
        {
            var extractedPath = inputPath;

            // Step 2: Decompress if needed
            if (File.Exists(inputPath) && DecompressService.IsCompressed(inputPath))
            {
                var tempBase = vm.SettingsService.Settings.TempFolder ?? Path.GetTempPath();
                var decompressor = new DecompressService();
                var result = await decompressor.DecompressAsync(inputPath, tempBase);
                extractedPath = result.ExtractedPath;
                tempDir = result.TempDirectory;
            }

            // Step 3: Detect format
            var format = ImportFormatDetector.Detect(extractedPath);
            if (format == ImportFormat.Unknown)
            {
                await ShowErrorDialogAsync("Import Error", $"Could not detect the import format for:\n{inputPath}");
                return;
            }

            // Step 4: Create appropriate import source and analyze
            IImportSource source = format switch
            {
                ImportFormat.SQLite => new SqliteImportService(),
                ImportFormat.PgDump => new PgDumpImportSource(),
                ImportFormat.MysqlDump => new MysqlDumpImportSource(),
                ImportFormat.MysqlShellDump => new MysqlShellDumpImportSource(),
                _ => throw new NotSupportedException($"Unsupported format: {format}")
            };

            GenericAnalysisResult analysis;
            try
            {
                analysis = await Task.Run(() => source.AnalyzeAsync(extractedPath));
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to analyze import source: {Path}", extractedPath);
                await ShowErrorDialogAsync("Import Error", $"Failed to analyze import source:\n{ex.Message}");
                return;
            }

            // Step 5: Show options dialog
            var optionsDialog = new ImportOptionsDialog();
            optionsDialog.Initialize(extractedPath, analysis);
            await optionsDialog.ShowDialog(this);

            if (optionsDialog.GenericResult is not { } importOptions) return;

            // Step 6: Show progress dialog and run import
            var progressDialog = new ImportProgressDialog();
            var showTask = progressDialog.ShowDialog(this);
            await progressDialog.RunImportAsync(source, importOptions);
            await showTask;

            // Step 7: If successful, open the new .ddb file
            if (progressDialog.Report is not null)
            {
                await vm.OpenDatabaseAsync(importOptions.DecentDbPath);
            }
        }
        finally
        {
            // Cleanup temp directory
            DecompressService.Cleanup(tempDir);
        }
    }

    private async Task ShowErrorDialogAsync(string title, string message)
    {
        var errorWin = new Window
        {
            Title = title,
            Width = 420, Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.Black,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, Foreground = Avalonia.Media.Brushes.White, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Padding = new Avalonia.Thickness(20, 6) }
                }
            }
        };
        ((Button)((StackPanel)errorWin.Content).Children[1]).Click += (_, _) => errorWin.Close();
        await errorWin.ShowDialog(this);
    }

    private async Task<string?> ShowInputDialogAsync(string title, string prompt, string initialValue)
    {
        var input = new TextBox { Width = 320, Text = initialValue };
        string? result = null;

        var okButton = new Button
        {
            Content = "OK",
            Padding = new Thickness(16, 6)
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(16, 6)
        };

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = prompt },
                    input,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, okButton }
                    }
                }
            }
        };

        okButton.Click += (_, _) =>
        {
            result = input.Text;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
        return result;
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnPreferencesClick(object? sender, RoutedEventArgs e)
    {
        var settingsService = (DataContext as MainWindowViewModel)?.SettingsService;
        var dialog = new PreferencesDialog(_themeManager, settingsService);
        await dialog.ShowDialog(this);
        ApplyThemeChanges();
    }

    private void ApplyCurrentTheme()
    {
        _themeManager.SetTheme(_themeManager.CurrentTheme);
        ApplyThemeChanges();
    }

    private void ApplyThemeChanges()
    {
        SqlEditor.IsDarkTheme = _themeManager.CurrentTheme == ThemeMode.Dark;

        if (DataContext is MainWindowViewModel vm && vm.Results.Columns.Count > 0)
        {
            RebuildResultsTable(vm.Results);
        }
    }

    private void SaveSelectedTheme()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        vm.SettingsService.Settings.Theme = _themeManager.CurrentTheme.ToString();
        vm.SettingsService.Save();
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog();
        await dialog.ShowDialog(this);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files != null && files.Any(f =>
                f.Name.EndsWith(".ddb", StringComparison.OrdinalIgnoreCase) ||
                f.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)))
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles()?.ToList();
            if (files != null)
            {
                // Handle .ddb files — open as database
                var ddbFile = files.FirstOrDefault(f => f.Name.EndsWith(".ddb", StringComparison.OrdinalIgnoreCase));
                if (ddbFile != null)
                {
                    await vm.OpenDatabaseAsync(ddbFile.Path.LocalPath);
                }

                // Handle .sql files — load contents into query editor
                var sqlFile = files.FirstOrDefault(f => f.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));
                if (sqlFile != null)
                {
                    try
                    {
                        await vm.OpenSqlFileInTabAsync(sqlFile.Path.LocalPath, openInNewTab: true);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Logger.Warning(ex, "Failed to read SQL file: {Path}", sqlFile.Path.LocalPath);
                    }
                }
            }
        }

        e.Handled = true;
    }

    private async void OnCopyRowsAsTsvClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.Results.Rows.Count == 0)
        {
            return;
        }

        var header = string.Join("\t", vm.Results.Columns.Select(c => c.Name));
        var lines = new List<string> { header };
        lines.AddRange(vm.Results.Rows.Select(r => BuildDelimitedRow(r, vm.Results.Columns, "\t")));
        await CopyToClipboardAsync(string.Join(Environment.NewLine, lines));
    }

    private async void OnCopyRowsAsCsvClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.Results.Rows.Count == 0)
        {
            return;
        }

        var header = string.Join(",", vm.Results.Columns.Select(c => $"\"{c.Name.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));
        var lines = new List<string> { header };
        lines.AddRange(vm.Results.Rows.Select(r => BuildDelimitedRow(r, vm.Results.Columns, ",")));
        await CopyToClipboardAsync(string.Join(Environment.NewLine, lines));
    }

    private async void OnExportCsvClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to CSV",
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV Files") { Patterns = ["*.csv"] }
            ]
        });

        if (file != null)
        {
            await vm.Results.ExportToCsvAsync(file.Path.LocalPath, CancellationToken.None);
        }
    }

    private async void OnExportJsonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to JSON",
            DefaultExtension = "json",
            FileTypeChoices =
            [
                new FilePickerFileType("JSON Files") { Patterns = ["*.json"] }
            ]
        });

        if (file != null)
        {
            await vm.Results.ExportToJsonAsync(file.Path.LocalPath, CancellationToken.None);
        }
    }
}
