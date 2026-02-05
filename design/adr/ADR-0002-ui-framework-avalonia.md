# ADR-0002: Use Avalonia for the MehSQL Desktop UI

## Status
Accepted

## Context
MehSQL is a cross-platform desktop SQL GUI/editor for DecentDB. The project’s top priority is
correctness and low “slop” when generating and evolving code with LLM assistance. The team
also wants maximum reuse of existing DecentDB .NET libraries (including a Dapper binding),
plus the ability to profile/optimize within a single runtime.

Cross-platform UI options included Electron/Tauri and .NET desktop frameworks.

## Decision
Use **Avalonia 11.x** for the MehSQL desktop UI and target **.NET 10** for the application runtime.

## Alternatives Considered
- Electron + React + TypeScript (strong ecosystem but introduces JS/Node + separate UI/runtime concerns)
- Tauri + Web UI (adds a Rust boundary + more IPC surface)
- MAUI (platform support and complexity trade-offs)
- WPF/WinUI (not cross-platform)

## Consequences
- Single-language, single-runtime end-to-end C#/.NET reduces integration complexity and LLM error modes.
- UI performance must be managed carefully (virtualization/paging) to keep large result sets responsive.
- Some templates may default to net8/net9; project files will explicitly target net10.0.
