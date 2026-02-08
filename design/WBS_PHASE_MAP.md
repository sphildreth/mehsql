# MehSQL – Phase Map & Work Breakdown Structure

## Phase Map
- [X] Phase 0 – Project Scaffold & CI
- [X] Phase 1 – Core Query Execution + Timing
- [X] Phase 2 – Basic UI: Editor + Run/Cancel + Errors
- [X] Phase 3 – Virtualized Results + Paging + Cache
- [ ] Phase 4 – Schema Browser
- [ ] Phase 5 – Explain/Analyze + Performance Panel
- [ ] Phase 6 – Exports + Polishing
- [ ] Phase 7 - Open, New and Drag & Drop
- 
---

## Phase 0 – Project Scaffo~~~~ld & CI
- [X] .NET 10 solution
- [X] Avalonia 11 app project
- [X] CI matrix for Windows/macOS/Linux:
  - [X] restore/build/test
  - [X] basic smoke tests
- [X] ADRs directory present and documented

## Phase 1 – Core Query Execution + Timing
- [X] Connection/open database
- [X] Execute SQL with cancellation
- [X] Streaming fetch and timing capture
- [X] Tests around execution with a perf fixture

## Phase 2 – Basic UI
- [X] SQL editor textbox (multiline, monospace font, accepts tabs/newlines)
- [X] Run and Cancel buttons with command bindings
- [X] Error panel with visibility binding
- [X] Results DataGrid placeholder
- [X] Performance panel placeholder (timing display)
- [X] MainWindowViewModel with ReactiveUI commands
- [X] Updated ViewModelBase to inherit from ReactiveObject
- [X] 3-pane layout matching specification

### Layout (Phase 2)
```
+--------------------+---------------------------+
| Menu Bar                                       |
+--------------------+---------------------------+
|                    |                           |
|  Schema Explorer   |     SQL Editor            |
|  (Left Pane)       |     (Top Right)           |
|                    |                           |
|                    +---------------------------+
|                    |                           |
|                    |     Query Results         |
|                    |     (Bottom Right)        |
|                    |                           |
+--------------------+---------------------------+
```

- **Left Pane**: Schema Explorer (TreeView placeholder, 300px wide, resizable)
- **Right Top**: SQL Editor with toolbar (Run/Cancel buttons)
- **Right Bottom**: Query Results with DataGrid, error panel, performance metrics
- **Splitters**: Both vertical and horizontal GridSplitters for resizing

### UI Components
- **Menu Bar**: File, Edit, View, Help menus (placeholders)
- **Schema Explorer**: TreeView placeholder for schemas/tables/views
- **SQL Editor**: Multi-line TextBox with AcceptsReturn/AcceptsTab, Consolas font
- **Run/Cancel Buttons**: In editor toolbar with IsExecuting state
- **Error Panel**: Red background, conditional visibility, displays error messages
- **Results Grid**: DataGrid with auto-generated columns, row count display
- **Performance Panel**: Bottom panel showing DB execution time, fetch time, row count

## Phase 3 – Virtualized Results + Paging
- [X] Paging abstraction in Core (CachedQueryPager with LRU eviction)
- [X] ResultsViewModel that loads pages as needed
- [X] Cache last N pages (configurable via QueryOptions.MaxCachedPages)
- [X] No UI thread blocking (async/await throughout)
- [X] Cache statistics and hit tracking
- [X] Non-deterministic ordering warning UI

## Phase 4 – Schema Browser
- Introspection queries
- Tree view

## Phase 5 – Explain/Analyze + Performance Panel
- Explain/Analyze tab
- Timing breakdown panel (DB vs fetch vs UI)~~~~

## Phase 6 – Export + Polish
- Streaming export CSV/JSON
- Preferences (theme, page size)
- Keyboard shortcuts

## Phase 7 - Open, New and Drag & Drop
- Be able to use File -> Open to open an existing DecentDB File (*.ddb)
- Be able to use File -> New to create a new DecentDB File (*.ddb)
- Be able to drag a DecentDB File (*.ddb) onto the app to open it
