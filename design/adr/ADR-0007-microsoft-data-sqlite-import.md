# ADR-0007: Microsoft.Data.Sqlite for SQLite Import

## Status
Accepted

## Context
MehSQL needs the ability to import SQLite databases into DecentDB format. This requires reading SQLite schema metadata (via PRAGMAs) and streaming row data from arbitrarily large SQLite files. We need a reliable, well-maintained SQLite client library for .NET.

## Decision
Add `Microsoft.Data.Sqlite` (v10.0.2) to `MehSql.Core`. This is Microsoft's official, lightweight ADO.NET provider for SQLite. It provides `DbConnection`/`DbCommand`/`DbDataReader` — the same patterns used by DecentDB's ADO.NET layer.

## Alternatives
- **System.Data.SQLite** — heavier, bundles its own SQLite native binary, more complex deployment.
- **Shell out to sqlite3 CLI** — fragile, hard to stream large result sets, platform-dependent.

## Consequences
- Adds a transitive dependency on `SQLitePCLRaw` (native SQLite bindings). This increases the deployment size slightly but is well-tested across Windows, macOS, and Linux.
- The dependency is only used by the import feature in `MehSql.Core/Import/`.
