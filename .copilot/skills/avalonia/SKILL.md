---
name: avalonia
description: Guidance for making safe, minimal-diff Avalonia UI changes in MehSQL with proper MVVM patterns and performance rules.
license: Proprietary
---

# Skill : Avalonia Developer Skill

You are an expert .NET coding agent working in the MehSQL repository.

## Mission

Implement the user’s requested change in .NET 10 and Avalonia with minimal diffs.

## Project Context

MehSQL is a cross-platform SQL GUI using:
- **Avalonia 11.x** for UI
- **.NET 10** runtime
- **MVVM pattern** (lightweight, no heavy frameworks)
- **xUnit + Moq** for testing

### Project Boundaries
```
src/MehSql.Core   → DB access, paging, timing (NO UI references)
src/MehSql.App    → Avalonia UI only (Views + ViewModels)
tests/*           → Unit tests (xUnit + Moq)
design/adr        → Architecture Decision Records
```

---

## Critical Performance Rules

### NEVER Do These
- **Never bind full result sets (10k+ rows) to UI collections** - always use paging
- **Never block the UI thread** with DB or I/O work
- **Never call synchronous DB methods** from UI code

### Always Do These
- Results must be **paged** (default page size: 500)
- UI loads pages **incrementally** via `LoadMoreAsync()`
- All DB calls must be **async** and accept `CancellationToken`
- Performance panel shows **separate timings**: DB execution vs fetch vs UI bind

---

## MVVM Pattern

### ViewModel Rules
- ViewModels inherit from `ViewModelBase`
- Use `RaisePropertyChanged()` for property change notification
- ViewModels call **Core interfaces only** (never direct DB access)
- Keep ViewModels testable with mocks

### View Rules
- AXAML files define UI layout
- Use data binding to ViewModels
- Code-behind (`.axaml.cs`) should be minimal
- Use `{Binding PropertyName}` syntax

### Example ViewModel Pattern
```csharp
public sealed class MyViewModel : ViewModelBase
{
    private bool _isBusy;
    public bool IsBusy 
    { 
        get => _isBusy; 
        private set { _isBusy = value; RaisePropertyChanged(); } 
    }

    public async Task DoWorkAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            // async work here
        }
        finally { IsBusy = false; }
    }
}
```

---

## AXAML Conventions

### Window/Control Structure
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="MehSql.App.Views.MyWindow">
  <!-- Content here -->
</Window>
```

### Common Layouts
- Use `Grid` with `RowDefinitions`/`ColumnDefinitions` for complex layouts
- Use `StackPanel` for simple vertical/horizontal stacking
- Use `DockPanel` for dock-based layouts

### Data Binding
```xml
<TextBlock Text="{Binding StatusMessage}"/>
<Button Command="{Binding RunCommand}" IsEnabled="{Binding !IsBusy}"/>
<DataGrid ItemsSource="{Binding Rows}"/>
```

---

## Results Grid & Virtualization

The results grid MUST use virtualization for large datasets:

1. Use `IQueryPager` interface for paging
2. `ResultsViewModel` manages incremental page loading
3. Call `RunAsync()` for first page, `LoadMoreAsync()` for subsequent pages
4. Never bind the full result set directly

### Key Interfaces
```csharp
// Core paging abstraction
public interface IQueryPager
{
    Task<QueryPage> ExecuteFirstPageAsync(string sql, QueryOptions opts, CancellationToken ct);
    Task<QueryPage> ExecuteNextPageAsync(string sql, QueryOptions opts, QueryPageToken token, CancellationToken ct);
}
```

---

## Testing Avalonia Components

### ViewModel Tests (Required)
```csharp
[Fact]
public async Task RunAsync_SetsIsBusy_DuringExecution()
{
    var mockPager = new Mock<IQueryPager>();
    // Setup mock...
    var vm = new ResultsViewModel(mockPager.Object);
    
    await vm.RunAsync(CancellationToken.None);
    
    // Assert expectations
}
```

### UI Tests
- Use `Avalonia.Headless.XUnit` for headless UI testing
- Avoid UI automation tests (not currently used)
- Avoid snapshot/visual tests

---

## ADR Requirements

You **MUST** create an ADR in `design/adr/` before:
- Adding new Avalonia packages/controls
- Changing virtualization/paging strategy
- Introducing new UI patterns or frameworks
- Making significant layout/architecture changes

Use template: `ADR-0001-template.md`

---

## Common Avalonia Controls

| Control | Use Case |
|---------|----------|
| `TextBox` | Text input |
| `TextBlock` | Display text |
| `Button` | Actions |
| `DataGrid` | Tabular data (with virtualization) |
| `TreeView` | Hierarchical data (schema browser) |
| `TabControl` | Multiple tabs (query tabs) |
| `Grid` | Complex layouts |
| `DockPanel` | Dock-based layouts |

---

## Build & Test Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run specific test
dotnet test --filter "FullyQualifiedName~TestName"

# Run application
dotnet run --project src/MehSql.App
```

---

## Style Guidelines

- Prefer clarity over clever abstractions
- Prefer boring solutions that work
- Comments explain *why*, not *what*
- Keep changes minimal and surgical
- "It's fine" = avoid unnecessary complexity

---

## Checklist Before Submitting Changes

- [ ] Code builds on all platforms (`dotnet build`)
- [ ] Tests pass (`dotnet test`)
- [ ] No UI thread blocking introduced
- [ ] Results use paging (never full binding)
- [ ] ADR created if architectural decision made
- [ ] ViewModel changes have unit tests
