# ADR-0010: Editor Tabs, Query History, and Safe Default LIMIT

## Status
Accepted

## Context
`design/NEW_FEATURES.md` requires practical SQL editor workflows:
- Multiple query tabs per database.
- Open/save SQL file workflows with recent SQL files.
- Per-database query history with rerun/open-in-new-tab actions.
- Run selection/current statement/whole editor.
- A toggleable safe default `LIMIT` that is visibly reported when applied.

These features need to work without binding full result sets to the UI and without changing core layering boundaries.

## Decision
- Introduce a tabbed query session model in `MainWindowViewModel` using `QueryTabViewModel`.
- Persist recent SQL files and per-database query history in existing YAML settings (`SettingsService` / `UserSettings`).
- Add `SqlExecutionPlanner` in `MehSql.Core.Querying` to determine execution scope:
  - highlighted selection,
  - current statement at caret,
  - whole editor fallback.
- Extend paging query options and results metadata with `ApplyDefaultLimit` and `DefaultLimitApplied` so the UI can both control and surface safe-limit behavior.
- Keep schema actions best-effort and non-destructive by default (e.g., drop actions generate executable SQL in a tab).

## Alternatives Considered
- Keep a single editor document and store tab state only in view code-behind.
  - Rejected: harder to test and violates MVVM intent.
- Add a full SQL parser dependency for statement extraction/formatting.
  - Rejected: unnecessary complexity for near-term scope.
- Execute destructive schema actions immediately from context menu.
  - Rejected: unsafe default without robust confirmation/error UX.

## Consequences
- Editor/session workflows are now testable at ViewModel/Core layers.
- Settings file now includes recent SQL files and query history records.
- Users can run narrower SQL scopes quickly and understand when a safety limit changed result shape.
- DDL/properties/drop outputs remain best-effort where DecentDB introspection is limited.
