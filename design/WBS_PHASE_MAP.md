# MehSQL – Phase Map & Work Breakdown Structure

## Phase Map
- [ ] Phase 0 – Project Scaffold & CI
- [ ] Phase 1 – Core Query Execution + Timing
- [ ] Phase 2 – Basic UI: Editor + Run/Cancel + Errors
- [ ] Phase 3 – Virtualized Results + Paging + Cache
- [ ] Phase 4 – Schema Browser
- [ ] Phase 5 – Explain/Analyze + Performance Panel
- [ ] Phase 6 – Exports + Polishing

---

## Phase 0 – Project Scaffold & CI
- .NET 10 solution
- Avalonia 11 app project
- CI matrix for Windows/macOS/Linux:
  - restore/build/test
  - basic smoke tests
- ADRs directory present and documented

## Phase 1 – Core Query Execution + Timing
- Connection/open database
- Execute SQL with cancellation
- Streaming fetch and timing capture
- Tests around execution with a perf fixture

## Phase 2 – Basic UI
- SQL editor textbox
- Run and Cancel buttons
- Error panel
- Results placeholder + performance placeholder

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
