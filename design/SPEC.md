# MehSQL – Technical Specification (SPEC)

## Technology Stack
- Language: C#
- Runtime: .NET 10 (LTS)
- UI Framework: Avalonia 11.x
- Architecture: MVVM (lightweight; avoid heavy frameworks)
- Data Access: DecentDB + Dapper binding (existing in DecentDB)

## Solution Structure
```
/src
  /MehSql.Core
  /MehSql.App
/design
  /adr
/tests
/fixtures
```

## Core Layer (MehSql.Core)
Responsibilities:
- Open/connect to DecentDB
- Execute SQL (streaming)
- Provide paging API for large results
- Schema introspection queries
- Performance/timing capture

### Result Shape (standardized)
- Columns: `IReadOnlyList<ColumnInfo>`
- Rows: `IReadOnlyDictionary<string, object?>`
- Paging: keyset/seek preferred; OFFSET allowed as fallback when no deterministic order exists

### Threading
- Query execution must never block UI thread.
- Cancellation must be supported for long queries.

## UI Layer (MehSql.App)
Responsibilities:
- Avalonia UI (editor, grid, schema tree)
- ViewModels call Core interfaces only
- Results grid must be virtualized/paged (never bind full 100k+ set)

## Performance Instrumentation
Each execution records:
- `DbExecutionTime` (best-effort)
- `FetchTime`
- `UiBindTime` / `UiFirstPaintTime` (best-effort; may be approximate)

Display in a performance panel next to results.

## ADR Usage (MANDATORY)
Any of the following requires an ADR:
- Introducing new packages or toolchains
- Changing paging strategy or result model
- Changing async/threading model
- Packaging/distribution approach

ADR format: Context / Decision / Alternatives / Consequences
