# ADR-0008: ZstdSharp.Port for MySQL Shell Dump Import

## Status
Accepted

## Context
MehSQL supports importing from MySQL Shell dump directories (`mysqlsh util.dumpTables` / `util.dumpSchemas`). These dumps store table data in `.tsv.zst` files — tab-separated values compressed with the Zstandard (zstd) algorithm. .NET does not include built-in zstd decompression, so an external package is needed.

## Decision
Add `ZstdSharp.Port` (v0.8.7) to `MehSql.Core`. This is a fully managed C# port of the zstd library (no native binaries required). It provides `DecompressionStream` for streaming decompression of `.tsv.zst` data chunks.

## Alternatives
- **ZstdNet** — wraps the native libzstd; requires platform-specific native binaries, complicating cross-platform deployment.
- **Shell out to `zstd` CLI** — fragile, requires zstd installed on the user's machine, hard to stream.
- **Skip MySQL Shell dump support** — users would need to convert to mysqldump format first, which is inconvenient for large databases.

## Consequences
- Pure managed code — no native binary dependencies, works on all platforms (Windows, macOS, Linux) without extra setup.
- The dependency is only used by `MysqlShellDumpImportSource` in `MehSql.Core/Import/`.
- Small package footprint (~1.5 MB).
