using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MehSql.Core.Schema;
using ReactiveUI;

namespace MehSql.App.ViewModels;

public static class SchemaActionNames
{
    public const string ShowProperties = "ShowProperties";
    public const string NewQueryTab = "NewQueryTab";
    public const string RefreshSchema = "RefreshSchema";
    public const string ViewDdl = "ViewDdl";
    public const string GenerateCrud = "GenerateCrud";
    public const string DropObject = "DropObject";
    public const string FindReferences = "FindReferences";
}

public sealed record SchemaActionRequest(string Action, string NodeType, string Name, object? Model);

/// <summary>
/// ViewModel for the Schema Explorer tree view.
/// </summary>
public sealed class SchemaExplorerViewModel : ViewModelBase
{
    private ISchemaService _schemaService;
    private Action<string>? _onSelectTopRows;
    private Action<SchemaActionRequest>? _onNodeAction;

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

    public SchemaExplorerViewModel(
        ISchemaService schemaService,
        Action<string>? onSelectTopRows = null,
        Action<SchemaActionRequest>? onNodeAction = null)
    {
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        _onSelectTopRows = onSelectTopRows;
        _onNodeAction = onNodeAction;
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
    }

    public void UpdateSchemaService(
        ISchemaService schemaService,
        Action<string>? onSelectTopRows = null,
        Action<SchemaActionRequest>? onNodeAction = null)
    {
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        _onSelectTopRows = onSelectTopRows;
        _onNodeAction = onNodeAction;
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

            var databaseNode = new SchemaNodeViewModel("Database", "Database", schema, _onSelectTopRows, _onNodeAction, schema.Name);

            if (schema.Tables.Count > 0)
            {
                var tablesNode = new SchemaNodeViewModel("Tables", "Folder", null, _onSelectTopRows, _onNodeAction, "Tables");
                foreach (var table in schema.Tables)
                {
                    var tableVm = new SchemaNodeViewModel(table.Name, "Table", table, _onSelectTopRows, _onNodeAction, table.Name);

                    if (table.Columns.Count > 0)
                    {
                        var columnsNode = new SchemaNodeViewModel("Columns", "Folder", null, _onSelectTopRows, _onNodeAction, "Columns");
                        foreach (var col in table.Columns)
                        {
                            var colText = col.IsPrimaryKey ? $"PK {col.DisplayText}" : col.DisplayText;
                            columnsNode.Children.Add(new SchemaNodeViewModel(colText, "Column", col, _onSelectTopRows, _onNodeAction, col.Name));
                        }

                        tableVm.Children.Add(columnsNode);
                    }

                    if (table.ForeignKeys.Count > 0)
                    {
                        var fkNode = new SchemaNodeViewModel("Foreign Keys", "Folder", null, _onSelectTopRows, _onNodeAction, "Foreign Keys");
                        foreach (var fk in table.ForeignKeys)
                        {
                            fkNode.Children.Add(new SchemaNodeViewModel(fk.DisplayText, "ForeignKey", fk, _onSelectTopRows, _onNodeAction, fk.Name));
                        }

                        tableVm.Children.Add(fkNode);
                    }

                    if (table.Indexes.Count > 0)
                    {
                        var indexesNode = new SchemaNodeViewModel("Indexes", "Folder", null, _onSelectTopRows, _onNodeAction, "Indexes");
                        foreach (var idx in table.Indexes)
                        {
                            var idxText = idx.IsUnique ? $"Unique {idx.Name}" : idx.Name;
                            indexesNode.Children.Add(new SchemaNodeViewModel(idxText, "Index", idx, _onSelectTopRows, _onNodeAction, idx.Name));
                        }

                        tableVm.Children.Add(indexesNode);
                    }

                    if (table.Triggers.Count > 0)
                    {
                        var triggerNode = new SchemaNodeViewModel("Triggers", "Folder", null, _onSelectTopRows, _onNodeAction, "Triggers");
                        foreach (var trigger in table.Triggers)
                        {
                            triggerNode.Children.Add(new SchemaNodeViewModel(trigger.Name, "Trigger", trigger, _onSelectTopRows, _onNodeAction, trigger.Name));
                        }

                        tableVm.Children.Add(triggerNode);
                    }

                    tablesNode.Children.Add(tableVm);
                }

                databaseNode.Children.Add(tablesNode);
            }

            if (schema.Views.Count > 0)
            {
                var viewsNode = new SchemaNodeViewModel("Views", "Folder", null, _onSelectTopRows, _onNodeAction, "Views");
                foreach (var view in schema.Views)
                {
                    var viewVm = new SchemaNodeViewModel(view.Name, "View", view, _onSelectTopRows, _onNodeAction, view.Name);

                    if (view.Columns.Count > 0)
                    {
                        var columnsNode = new SchemaNodeViewModel("Columns", "Folder", null, _onSelectTopRows, _onNodeAction, "Columns");
                        foreach (var col in view.Columns)
                        {
                            columnsNode.Children.Add(new SchemaNodeViewModel(col.DisplayText, "Column", col, _onSelectTopRows, _onNodeAction, col.Name));
                        }

                        viewVm.Children.Add(columnsNode);
                    }

                    if (view.Triggers.Count > 0)
                    {
                        var triggersNode = new SchemaNodeViewModel("Triggers", "Folder", null, _onSelectTopRows, _onNodeAction, "Triggers");
                        foreach (var trigger in view.Triggers)
                        {
                            triggersNode.Children.Add(new SchemaNodeViewModel(trigger.Name, "Trigger", trigger, _onSelectTopRows, _onNodeAction, trigger.Name));
                        }

                        viewVm.Children.Add(triggersNode);
                    }

                    viewsNode.Children.Add(viewVm);
                }

                databaseNode.Children.Add(viewsNode);
            }

            RootNodes.Add(databaseNode);
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
    private readonly Action<SchemaActionRequest>? _onNodeAction;

    public string DisplayName { get; }
    public string NodeName { get; }
    public string NodeType { get; }
    public object? Model { get; }
    public ObservableCollection<SchemaNodeViewModel> Children { get; } = new();

    public ICommand? SelectTopRowsCommand { get; }
    public ICommand? ShowPropertiesCommand { get; }
    public ICommand? ViewDdlCommand { get; }
    public ICommand? GenerateCrudCommand { get; }
    public ICommand? DropCommand { get; }
    public ICommand? FindReferencesCommand { get; }
    public ICommand? NewQueryTabCommand { get; }
    public ICommand? RefreshSchemaCommand { get; }

    public SchemaNodeViewModel(
        string displayName,
        string nodeType,
        object? model,
        Action<string>? onSelectTopRows = null,
        Action<SchemaActionRequest>? onNodeAction = null,
        string? nodeName = null)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        NodeName = nodeName ?? displayName;
        NodeType = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
        Model = model;
        _onNodeAction = onNodeAction;

        if (NodeType == "Table" && onSelectTopRows is not null)
        {
            SelectTopRowsCommand = ReactiveCommand.Create(() => onSelectTopRows(NodeName));
        }

        ShowPropertiesCommand = CreateCommandForNode(SchemaActionNames.ShowProperties);
        ViewDdlCommand = CreateCommandForNode(SchemaActionNames.ViewDdl, "Table", "View", "Trigger", "Index");
        GenerateCrudCommand = CreateCommandForNode(SchemaActionNames.GenerateCrud, "Table");
        DropCommand = CreateCommandForNode(SchemaActionNames.DropObject, "Table", "View", "Trigger", "Index", "Column");
        FindReferencesCommand = CreateCommandForNode(SchemaActionNames.FindReferences, "Table", "Column", "View", "Index", "Trigger");
        NewQueryTabCommand = CreateCommandForNode(SchemaActionNames.NewQueryTab, "Database");
        RefreshSchemaCommand = CreateCommandForNode(SchemaActionNames.RefreshSchema, "Database");
    }

    private ICommand? CreateCommandForNode(string action, params string[] supportedTypes)
    {
        if (_onNodeAction is null)
        {
            return null;
        }

        if (supportedTypes.Length > 0 && Array.IndexOf(supportedTypes, NodeType) < 0)
        {
            return null;
        }

        return ReactiveCommand.Create(() => _onNodeAction(new SchemaActionRequest(action, NodeType, NodeName, Model)));
    }
}
