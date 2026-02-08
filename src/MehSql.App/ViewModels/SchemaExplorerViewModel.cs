using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MehSql.Core.Schema;
using ReactiveUI;

namespace MehSql.App.ViewModels;

/// <summary>
/// ViewModel for the Schema Explorer tree view.
/// </summary>
public sealed class SchemaExplorerViewModel : ViewModelBase
{
    private readonly ISchemaService _schemaService;

    public ObservableCollection<SchemaNodeViewModel> RootNodes { get; } = new();

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private bool _hasError;
    public bool HasError
    {
        get => _hasError;
        private set => this.RaiseAndSetIfChanged(ref _hasError, value);
    }

    public ICommand RefreshCommand { get; }

    public SchemaExplorerViewModel(ISchemaService schemaService)
    {
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await RefreshAsync(ct);
    }

    private async Task RefreshAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = null;

        try
        {
            RootNodes.Clear();
            var schema = await _schemaService.GetSchemaAsync(ct);

            // Add Tables folder
            if (schema.Tables.Count > 0)
            {
                var tablesNode = new SchemaNodeViewModel("Tables", "Folder", null);
                foreach (var table in schema.Tables)
                {
                    var tableVm = new SchemaNodeViewModel(table.Name, "Table", table);

                    // Add columns
                    foreach (var col in table.Columns)
                    {
                        var colText = col.IsPrimaryKey ? $"🔑 {col.DisplayText}" : col.DisplayText;
                        tableVm.Children.Add(new SchemaNodeViewModel(colText, "Column", col));
                    }

                    // Add indexes folder
                    if (table.Indexes.Count > 0)
                    {
                        var indexesNode = new SchemaNodeViewModel("Indexes", "Folder", null);
                        foreach (var idx in table.Indexes)
                        {
                            var idxText = idx.IsUnique ? $"📌 {idx.Name}" : idx.Name;
                            indexesNode.Children.Add(new SchemaNodeViewModel(idxText, "Index", idx));
                        }
                        tableVm.Children.Add(indexesNode);
                    }

                    tablesNode.Children.Add(tableVm);
                }
                RootNodes.Add(tablesNode);
            }

            // Add Views folder
            if (schema.Views.Count > 0)
            {
                var viewsNode = new SchemaNodeViewModel("Views", "Folder", null);
                foreach (var view in schema.Views)
                {
                    var viewVm = new SchemaNodeViewModel(view.Name, "View", view);

                    foreach (var col in view.Columns)
                    {
                        viewVm.Children.Add(new SchemaNodeViewModel(col.DisplayText, "Column", col));
                    }

                    viewsNode.Children.Add(viewVm);
                }
                RootNodes.Add(viewsNode);
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to load schema: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

/// <summary>
/// ViewModel wrapper for a schema node in the tree.
/// </summary>
public sealed class SchemaNodeViewModel : ViewModelBase
{
    public string DisplayName { get; }
    public string NodeType { get; }
    public object? Model { get; }
    public ObservableCollection<SchemaNodeViewModel> Children { get; } = new();

    public SchemaNodeViewModel(string displayName, string nodeType, object? model)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        NodeType = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
        Model = model;
    }
}
