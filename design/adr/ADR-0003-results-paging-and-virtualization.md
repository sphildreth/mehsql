# ADR-0003: Results Paging + Virtualization Strategy

## Status
Accepted

## Context
MehSQL must remain responsive when displaying large query results (100k+ rows).
Rendering or binding huge collections directly to the UI is a common source of freezes.
We also want to avoid measuring “UI pain” instead of DecentDB performance.

## Decision
- Always fetch results in **pages** (default page size: 500; configurable).
- UI displays results using **virtualization** and page caching; it never binds a full result set at once.
- Prefer **keyset/seek pagination** when an ORDER BY is available or can be inferred.
- Fall back to OFFSET/LIMIT only when deterministic ordering is not present and cannot be reasonably added.
- Performance panel shows separate timings: DB execution vs fetch vs UI bind.

## Alternatives Considered
- Bind full result set to UI (rejected: freezes/allocations)
- OFFSET-only paging (rejected as default: slow for deep pages)
- Always enforce ORDER BY (rejected: can change semantics; may not be desired)

## Consequences
- Core layer exposes a paging abstraction; UI consumes pages incrementally.
- Some queries without deterministic order may show unstable paging if users do not specify ORDER BY.
- Clear UI messaging is needed when paging may be non-deterministic.
