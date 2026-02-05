# MehSQL - Copilot Instructions

## Build & Test Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test
dotnet test --filter "FullyQualifiedName~ResultsViewModelTests.RunAsync_ClearsRows_ThenLoadsFirstPage"

# Run the application
dotnet run --project src/MehSql.App
```

## Architecture

MehSQL is a cross-platform SQL GUI for DecentDB using Avalonia 11.x and .NET 10.

### Project Structure

- **MehSql.Core** - Database-agnostic business logic: query execution, paging, schema introspection, timing capture
- **MehSql.App** - Avalonia UI with MVVM pattern (ViewModels call Core interfaces only)
- **design/adr/** - Architecture Decision Records (ADRs) for significant decisions

### Key Interfaces

- `IQueryPager` - Core paging abstraction for query results
- `ResultsViewModel` - Loads result pages incrementally via `IQueryPager`

## Critical Conventions

### Paging & Virtualization (ADR-0003)

**Never bind full result sets to the UI.** All query results must use paging + virtualization:
- Default page size: 500 rows
- Prefer keyset/seek pagination when ORDER BY is available
- Fall back to OFFSET/LIMIT only when deterministic ordering isn't possible
- `ResultsViewModel` loads pages incrementally via `RunAsync()` and `LoadMoreAsync()`

### Threading

- All DB calls must be async and cancellable (accept `CancellationToken`)
- Never block the UI thread

### Performance Instrumentation

Every query execution must capture separate timings:
- `DbExecutionTime` - Time spent in database
- `FetchTime` - Time to fetch results
- `UiBindTime` / `UiFirstPaintTime` - UI rendering time (approximate)

### ADR Requirements

You **must** create an ADR in `design/adr/` before:
- Adding new package dependencies
- Changing paging strategy or result model
- Changing async/threading model
- Making packaging/distribution decisions

Use the template in `ADR-0001-template.md`. Format: Context / Decision / Alternatives / Consequences.

## Testing

- Tests use xUnit with Avalonia.Headless.XUnit for UI testing
- Use Moq for mocking interfaces
- Core behavior requires unit test coverage
- Large result set interactions must remain responsive (no freezes)
