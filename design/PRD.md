# MehSQL – Product Requirements Document (PRD)

## Overview
MehSQL is a laid-back, desktop SQL GUI/editor designed specifically for DecentDB.
It embraces the philosophy that “decent enough” performance, usability, and features
are often exactly what developers need.

**[DecentDB](https://github.com/sphildreth/decentdb)** is an embedded database engine (like SQLite) with PostgreSQL dialect 
compatibility. There is no client-server architecture—MehSQL opens database files 
directly via the DecentDB engine.

MehSQL is **not** intended to be the most feature-rich or visually complex SQL client.
Instead, it prioritizes:
- Reliability
- Performance transparency
- Low drama, low ceremony workflows
- Clear architectural decisions documented via ADRs

## Goals
- Provide a simple, fast SQL editor for DecentDB (PostgreSQL dialect).
- Clearly showcase DecentDB query performance.
- Support large result sets without UI degradation.
- Remain LLM-friendly for ongoing development.
- Enforce architectural governance via ADRs.

## Non-Goals
- Competing with enterprise tools like DataGrip or DBeaver.
- Supporting non-PostgreSQL dialects (initially).
- Advanced ER diagramming or visual query builders.
- Multi-DB connection managers / cloud DB cataloguing.

## Target Users
- Developers evaluating or using DecentDB.
- Power users comfortable with SQL.
- OSS contributors and benchmarkers.

## Core Features
### Must Have (v1)
- Open/connect to DecentDB database files.
- SQL editor.
- Execute SQL queries with cancel support.
- Virtualized result grid (handles 100k+ rows via paging/virtualization).
- Clear error reporting (DB errors vs app errors).
- Performance transparency panel with timing breakdown:
  - DB execution time (best-effort)
  - Fetch/materialization time
  - UI bind/render time (best-effort)
- EXPLAIN support (prefer `EXPLAIN (ANALYZE, BUFFERS)` when supported).

### Should Have
- Schema browser (tables, columns, indexes).
- Query history (per-db).
- Export results (CSV, JSON) using streaming.
- Light/dark theme.
- Saved queries.
- Result filtering.
- Keyboard shortcuts (common ones: Run, Cancel, Format, Find).

## UX Principles
- Defaults should be safe and **configuration-driven** (e.g., default LIMIT/page size).
- UI should never freeze due to large datasets.
- "DecentDB is fast" should be demonstrable in-app via the timing panel.
- Keep interactions low-ceremony: open, type, run, see results.

## Configuration
MehSQL uses a simple configuration system for user preferences:
- Config file location: platform-appropriate (e.g., `~/.config/mehsql/config.json` on Linux)
- Key settings include:
  - `defaultLimit`: Default row limit for queries (default: 1000)
  - `pageSize`: Virtualization page size for result grids (default: 100)
  - `theme`: "light" | "dark" | "system"
  - `queryHistoryLimit`: Maximum queries to retain per database

## Architectural Governance (ADRs are mandatory)
All significant architectural/technical decisions **must** be captured as ADRs.

### ADR Requirements
- Directory: `/design/adr`
- Format: Markdown
- Naming: `ADR-XXXX-short-title.md`
- Required for:
  - Changes to query paging/virtualization strategy
  - Performance instrumentation approach
  - Introducing new dependencies
  - Packaging/distribution decisions
  - Major UI architecture changes (navigation, docking, editors)

## Success Metrics
- Queries returning 100k rows remain responsive in the UI (no UI lockups).
- Performance panel clearly separates DB time vs fetch vs UI.
- New contributors can understand key design decisions by reading ADRs.
- CI validates build + tests on Windows/macOS/Linux.

## Open Questions
- Distribution strategy (zip vs installer).
- ~~How to best detect/measure UI render time in a consistent way.~~
  - **Decision**: Use both approaches—`Stopwatch` instrumentation around virtualization 
    binding logic *and* UI framework render callbacks where available. This provides 
    both high-level timing and framework-specific insights.
