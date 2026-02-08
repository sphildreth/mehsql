# MehSQL – Phase Map & Work Breakdown Structure

## Phase Map
- [X] Phase 0 – Project Scaffold & CI
- [X] Phase 1 – Core Query Execution + Timing
- [X] Phase 2 – Basic UI: Editor + Run/Cancel + Errors
- [X] Phase 3 – Virtualized Results + Paging + Cache
- [X] Phase 4 – Schema Browser
- [X] Phase 5 – Explain/Analyze + Performance Panel
- [X] Phase 6 – Exports + Polishing
- [X] Streaming CSV export with proper escaping
- [X] Streaming JSON export with Utf8JsonWriter
- [X] Export buttons with file save dialogs
- [X] Export status and progress indication
- [X] User preferences service (JSON persistence)
- [X] Keyboard shortcuts (F5 to run, Ctrl+Shift+C to cancel)
- [X] Menu accelerators and InputGesture support

## Phase 7 - Open, New and Drag & Drop
- Be able to use File -> Open to open an existing DecentDB File (*.ddb)
- Be able to use File -> New to create a new DecentDB File (*.ddb)
  - When a new table is created using CREATE TABLE, it should be added to the current.ddb file and show up in the schema explorer
  - When a new column is added to an existing table, it should be reflected in the schema explorer
  - When a new view is created using CREATE VIEW, it should be added to the current .ddb file and show up in the schema explorer
- Be able to drag a DecentDB File (*.ddb) onto the app to open it
