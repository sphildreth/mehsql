# MehSQL – Coding Agent Implementation Guide

## Non-Negotiable Rules
- Follow SPEC.md and existing ADRs.
- Do NOT introduce new frameworks or architectural patterns without an ADR.
- Do NOT bind full result sets to the UI; paging + virtualization is mandatory.
- No UI thread blocking. All DB calls are async and cancellable.
- Code must build on Windows, macOS, and Linux in CI.

## Required Stack
- .NET 10
- Avalonia 11.x
- MVVM (simple; avoid heavy frameworks unless an ADR approves)
- DecentDB + existing Dapper binding

## What Requires a New ADR
- New package dependencies
- Paging strategy change
- Result model change
- New threading model (Schedulers, channels, custom dispatchers)
- Packaging/distribution decisions

## Acceptance Criteria (per feature slice)
- Build succeeds in CI matrix
- Unit tests cover Core behavior
- Large result set interactions remain responsive (no freeze)
- Performance panel shows timings and does not regress

## Definition of Done
- Feature implemented
- Tests updated/added
- ADR added if a real decision was made
- CI green
