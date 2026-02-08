# AGENTS.md — MehSQL

This document defines **how automated coding agents (LLMs) are expected to work in this repository**.
It exists to minimize architectural drift, reduce “slop,” and ensure consistent, reviewable progress.

If you are an agent: **read this entire file before writing code.**

---

## 1. Core Principles

- **MehSQL is intentionally simple.**
  Do not over‑engineer. “Decent enough” is the design philosophy.
- **Correctness > cleverness.**
- **Performance transparency matters.**
  We showcase DecentDB speed; do not hide or conflate timings.
- **All real decisions are documented.**
  If you make a decision, you probably owe an ADR.

---

## 2. Architecture (Non‑Negotiable)

- Language: **C#**
- Runtime: **.NET 10 (LTS)**
- UI: **Avalonia 11.x**
- Pattern: **MVVM (lightweight)**
- Tests: **xUnit + Moq**
- CI: GitHub Actions (Windows / macOS / Linux)

### Project Boundaries

```
src/MehSql.Core   → DB access, paging, timing (NO UI references)
src/MehSql.App    → Avalonia UI only
tests/*           → Unit tests (no UI automation)
design/adr        → Architecture Decision Records (mandatory)
```

Violating these boundaries requires an ADR.

### DecentDB API Preference (Very Important)

**Always prefer `DecentDB.MicroOrm` over `DecentDB.AdoNet`.**

- MehSQL is a **showcase app** for the C# DecentDB.MicroOrm API
- Use `DecentDBContext` as the primary entry point for database operations
- For typed/entity queries, use `DbSet<T>` through `context.Set<T>()`
- For raw SQL execution (required for a SQL editor), use `DecentDBConnection` directly through a factory pattern
- Encapsulate all database access behind abstractions (`IDbContextFactory`, `IQueryExecutor`) to allow flexibility

### DecentDB SQL Syntax

**DecentDB uses PostgreSQL-compatible syntax.**

When writing raw SQL queries for DecentDB:
- Use PostgreSQL syntax (not SQLite, not SQL Server, not MySQL)
- Use `LIMIT` and `OFFSET` for pagination (not `TOP` or `FETCH FIRST`)
- Use `||` for string concatenation
- Use `TRUE`/`FALSE` for boolean literals
- Use `SERIAL` or `GENERATED ALWAYS AS IDENTITY` for auto-increment columns
- Use `TIMESTAMP` or `TIMESTAMPTZ` for date/time types
- Avoid SQLite-specific features like `AUTOINCREMENT` or `INTEGER PRIMARY KEY` rowid aliases

---

## 3. ADR Rules (Very Important)

**ADRs are mandatory** for architectural or technical decisions.

### You MUST create an ADR when:
- Adding or changing dependencies
- Changing paging / virtualization strategy
- Changing result data models
- Introducing new async / threading mechanisms
- Making packaging or distribution decisions

### ADR Location & Format
- Directory: `design/adr`
- Naming: `ADR-XXXX-short-title.md`
- Template: `ADR-0001-template.md`

If you are unsure whether an ADR is required:
➡️ **Create one. Over‑documenting is preferred to silent decisions.**

---

## 4. Performance Rules

### Absolutely forbidden
- Binding full result sets (10k+ / 100k+ rows) to UI collections
- Blocking the UI thread with DB or I/O work
- Measuring UI slowness instead of DB execution time

### Required behavior
- Results are **paged** (default page size: 500)
- UI loads pages incrementally
- Performance panel shows **separate timings**:
  - DB execution time
  - Fetch/materialization time
  - UI bind/render time (best effort)

See:
- `ADR-0003-results-paging-and-virtualization.md`

---

## 5. What Agents Should Implement

Agents should work **one phase at a time**, as defined in `WBS_PHASE_MAP.md`.

### Typical safe tasks
- Implement Core paging logic behind existing interfaces
- Extend ViewModels (no heavy UI logic)
- Add unit tests for Core and ViewModels
- Improve performance instrumentation accuracy
- Improve error handling and messaging

### Out-of-scope without explicit instruction
- Major UI redesigns
- New plugin systems
- Replacing Avalonia/MVVM frameworks
- “Enterprise” features (roles, permissions, teams)

---

## 6. Testing Expectations

Every meaningful change must include tests.

### Minimum expectations
- Core logic has unit tests
- ViewModels are tested with mocks (xUnit + Moq)
- Tests must pass on all OSes in CI

Avoid:
- UI automation tests
- Snapshot/UI visual tests (for now)

---

## 7. Definition of Done

A change is considered **done** only when:

- Code builds on all platforms
- Tests pass
- No UI thread blocking introduced
- Performance characteristics preserved or improved
- ADR added if a real decision was made

---

## 8. Tone & Style

MehSQL is intentionally low‑drama.

- Prefer clarity over clever abstractions
- Prefer boring solutions that work
- Prefer small PRs
- Prefer comments that explain *why*, not *what*

> “It’s fine” is not an excuse for sloppy code — it’s permission to avoid unnecessary complexity.

---

## 9. When in Doubt

If an agent is uncertain:
1. Re‑read SPEC.md and existing ADRs
2. Choose the simplest option
3. Document the choice in an ADR
4. Proceed incrementally

End of file.
