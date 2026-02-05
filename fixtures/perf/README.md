# Perf Fixture

The goal of these fixtures is to create predictable datasets to test:
- paging/virtualization performance (10k / 100k / 1M rows)
- query execution time vs fetch time vs UI bind time

## Option A: Run SQL script inside DecentDB (preferred if supported)
Use `create_perf_tables.sql` to create and populate a table.

## Option B: Generate via the C# helper
Use `PerfDataGenerator.cs` as a reference implementation if DecentDB scripting differs.
