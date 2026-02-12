# NEW_FEATURES

Common SQL editor features that make sense for an embedded DecentDB workflow.

Constraints / scope:
- DecentDB is embedded (file-based). No server/session management features.
- DecentDB SQL dialect is PostgreSQL-compatible (use `LIMIT`/`OFFSET`, `$1` params, etc.).
- Avoid features that require binding full result sets to the UI (paging/virtualization is mandatory).

## Near-Term (High Value, Low Drama)

### Editor
- Open/save `.sql` files (including "Save As", recent files).
- Multiple query tabs per database file (close, rename, reorder).
- Basic find/replace in editor (Ctrl+F / Ctrl+H).
- Keyboard shortcuts: run, cancel, explain, format, new tab, close tab.
- SQL formatting (simple formatter; or "format selection" only).
- "Open in external editor" (optional, fallback when text editing is limiting).
- Find All References (jump to table/column usage).


### Execute + Results
- Run selection vs run whole editor (selection = highlighted text; fallback = current statement or whole editor).
- Clear "DB execution time" vs "fetch/materialize time" vs "UI bind/render time" in the performance panel.
- Cancel running query (already a must-have; ensure it behaves well even if cancellation is best-effort).
- Results:
  - Copy cell/row as TSV/CSV.
  - Export results to CSV/JSON using streaming.
  - Toggle "default LIMIT" (safe default) and surface when it is applied.

### Query History
- Per-db query history (bounded, searchable).
- "Re-run" and "Open in new tab" from history.

### Schema Browser (Tree)
- Right-click actions on database:
  - Properties (path, file size, page size if available, table count, index count, triggers count).
  - New query tab.
  - Refresh schema.
- Tables as expandable nodes with children:
  - Columns
  - Foreign Keys
  - Indexes
  - Triggers
  - Views (expandable to show triggers if defined)
- Right-click actions on a table:
  - New query: `SELECT * FROM <table> LIMIT 100;`
  - Generate CRUD snippets (INSERT/UPDATE/DELETE templates).
  - "View DDL" (show a best-effort rebuild script; copy/export).
- Right-click actions on columns/indexes/views/triggers:
  - Properties (best-effort; show what DecentDB exposes).
  - Drop (with confirmation) where supported.

## DecentDB-Specific Notes (Avoid Wishful Features)

### Triggers (Supported, But Narrow)
DecentDB supports triggers with a constrained surface:
- `CREATE TRIGGER` / `DROP TRIGGER`
- `AFTER` row triggers on tables
- `INSTEAD OF` row triggers on views
- `FOR EACH ROW` only
- Trigger action must be `EXECUTE FUNCTION decentdb_exec_sql('<single DML SQL>')`
- No `NEW`/`OLD` row references in trigger actions

This means the "New Trigger" UX should generate and validate only the supported subset.

### Views
- Views are read-only unless you define matching `INSTEAD OF` triggers for DML.

## Medium-Term (Good Editor/Tooling Features)

### Autocomplete (Pragmatic)
- Table/column name completion (from schema browser).
- Keyword completion.
- Simple completion triggers: dot (`alias.`), after `FROM`/`JOIN`/`WHERE`/`ORDER BY`.

### Object DDL
- This is a must-have feature (DBeaver-style "DDL" tab): show the full SQL script to rebuild the object.
- Output should be a single script users can copy/paste:
  - Optional header comments (generated timestamp, "synthesized" warning).
  - `DROP ...` (optional) then `CREATE ...`.
  - For tables: include related indexes as additional `CREATE INDEX` statements.
- Show best-effort DDL for:
  - tables (columns + constraints)
  - indexes (for selected table; or all indexes if viewing database-level DDL)
  - views
  - triggers (supported subset)
- Where DecentDB cannot provide the original source SQL, synthesize DDL from introspection and label it as synthesized.
- Versioning: clearly indicate when DDL is synthesized vs original source.
- Current DecentDB introspection is limited (practical implication for DDL generation):
  - Available today: columns (name/type/not-null/unique/primary key) and index metadata (name/columns/unique/kind).
  - Likely missing / not reliably reconstructable: original column defaults, CHECK expressions, FK details, partial/expression index predicates, trigger bodies, and view definitions.
  - When a detail cannot be reconstructed, omit it and annotate in comments rather than guessing.

### Data Tools
- Table "View Data" grid:
  - Paged reads with stable ordering when possible.
  - Optional filter row (simple `WHERE` builder; no visual query designer).
- Import helpers (already a theme in existing ADRs):
  - SQLite import improvements (progress, error surfacing).
  - Postgres dump / MySQL dump import UX polish.

## Out Of Scope (For Now)
- Server connection managers, SSH tunnels, roles/users, permissions.
- Advanced ER diagrams, schema compare, migrations, query builder UI.
- Debugger-like stepping, stored procedures, function editor.
- "Rebuild index" unless DecentDB exposes an equivalent operation.
