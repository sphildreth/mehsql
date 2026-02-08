# MehSQL – Phase Map & Work Breakdown Structure

## Phase Map
- [X] Phase 0 – Project Scaffold & CI
- [X] Phase 1 – Core Query Execution + Timing
- [ ] Phase 2 – Basic UI: Editor + Run/Cancel + Errors
- [ ] Phase 3 – Virtualized Results + Paging + Cache
- [ ] Phase 4 – Schema Browser
- [ ] Phase 5 – Explain/Analyze + Performance Panel
- [ ] Phase 6 – Exports + Polishing

---

## Phase 0 – Project Scaffold & CI
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

The application shall provide a standard menu bar with top-level menus such as File, Edit, View, and Help, following common desktop application conventions.

### Layout
- A 3 pane layout
  - left is explorer tree that shows schemas that is 2 panels high
  - right top panel is the editor
  - right bottom panel is the results

```text
+--------------------+---------------------------+
| Menu Bar                                       |
+--------------------+---------------------------+
|                    |                           |
|  Schema Explorer   |     SQL Editor            |
|  (Left Pane)       |     (Top Right Pane)      |
|                    |                           |
|                    +---------------------------+
|                    |                           |
|                    |     Query Results         |
|                    |     (Bottom Right Pane)   |
|                    |                           |
+--------------------+---------------------------+
```  
The UI shall implement a three-pane IDE-style layout using resizable split views, without docking or floating panes.

### Schema Explorer
Displays database structure in a tree view:
- Schemas
- Tables
- Views
- Columns
- Indexes
Vertically occupies the full height of the window.
Width is user-resizable.
Tree expansion should be lazy-loaded where possible.
Selecting items does not automatically execute queries.

### SQL Editor
Primary interaction area for writing SQL.
Supports:
- Multi-line text editing
- Running and cancelling queries
Occupies the upper portion of the right side.
Height is user-resizable relative to the results pane.
The editor must remain responsive even while queries are executing.

### Query Results
Displays the results of the most recent query execution.
Supports:
- Virtualized row display
- Paging for large result sets
- Error messages when queries fail
Occupies the lower portion of the right side.
Must not block the editor while results are loading or rendering.

### Laouyt Behavior Rules
- All panes must be resizable via splitters.
- Pane sizes should persist across application restarts (best effort).
- The editor and results panes must be independently scrollable.
- No pane may block UI interaction during query execution.

## Phase 3 – Virtualized Results + Paging
- Paging abstraction in Core
- ResultsViewModel that loads pages as needed
- Cache last N pages
- No UI thread blocking

## Phase 4 – Schema Browser
- Introspection queries
- Tree view

## Phase 5 – Explain/Analyze + Performance Panel
- Explain/Analyze tab
- Timing breakdown panel (DB vs fetch vs UI)

## Phase 6 – Export + Polish
- Streaming export CSV/JSON
- Preferences (theme, page size)
- Keyboard shortcuts
