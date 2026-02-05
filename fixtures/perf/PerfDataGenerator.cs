// Reference only: adapt connection creation to DecentDB provider specifics.
// Intention: generate perf_items rows without relying on SQL recursion support.
//
// Expected usage (example):
//   dotnet run --project tools/PerfGen -- --connection "<conn>" --rows 100000
//
// Keep this file as a reference until the concrete DecentDB connection API is finalized.

using System;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public static class PerfDataGenerator
{
    public static async Task GenerateAsync(DbConnection conn, int rows, CancellationToken ct)
    {
        await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
DROP TABLE IF EXISTS perf_items;
CREATE TABLE perf_items (
    id BIGINT PRIMARY KEY,
    created_at TIMESTAMP NOT NULL,
    category TEXT NOT NULL,
    score INTEGER NOT NULL,
    payload TEXT NOT NULL
);";
        await cmd.ExecuteNonQueryAsync(ct);

        // Insert in batches to reduce overhead.
        const int batchSize = 1000;
        var sw = Stopwatch.StartNew();

        for (int i = 1; i <= rows; i += batchSize)
        {
            int take = Math.Min(batchSize, rows - i + 1);

            using var insert = conn.CreateCommand();
            // NOTE: Replace with parameterized multi-values insert per provider.
            // This intentionally avoids provider-specific bulk APIs.
            insert.CommandText = BuildInsertSql(i, take);
            await insert.ExecuteNonQueryAsync(ct);
        }

        sw.Stop();
        Console.WriteLine($"Inserted {rows:N0} rows in {sw.Elapsed}");
    }

    private static string BuildInsertSql(int start, int count)
    {
        // VERY simple string build; replace with parameters in real code.
        // This exists as a generator reference.
        var sql = "INSERT INTO perf_items (id, created_at, category, score, payload) VALUES ";
        for (int k = 0; k < count; k++)
        {
            int n = start + k;
            string category = (n % 10) switch
            {
                0 => "alpha",
                1 => "bravo",
                2 => "charlie",
                3 => "delta",
                4 => "echo",
                5 => "foxtrot",
                6 => "golf",
                7 => "hotel",
                8 => "india",
                _ => "juliet"
            };
            int score = n % 1000;
            string payload = $"payload-{n}-" + new string('x', 64);

            sql += $"({n}, TIMESTAMP '2020-01-01' + INTERVAL '{n} seconds', '{category}', {score}, '{payload}')";
            if (k < count - 1) sql += ",";
        }
        sql += ";";
        return sql;
    }
}
