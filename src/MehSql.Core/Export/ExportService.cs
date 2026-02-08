using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MehSql.Core.Querying;

namespace MehSql.Core.Export;

/// <summary>
/// Service for exporting query results to various formats.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Exports results to CSV format using streaming to handle large datasets.
    /// </summary>
    Task ExportToCsvAsync(
        IAsyncEnumerable<QueryPage> pages,
        Stream outputStream,
        ExportOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Exports results to JSON format using streaming to handle large datasets.
    /// </summary>
    Task ExportToJsonAsync(
        IAsyncEnumerable<QueryPage> pages,
        Stream outputStream,
        ExportOptions options,
        CancellationToken ct = default);
}

/// <summary>
/// Options for export operations.
/// </summary>
public sealed class ExportOptions
{
    public bool IncludeHeaders { get; set; } = true;
    public string? DateTimeFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
    public bool FormatDatesAsIso { get; set; } = true;
    public bool PrettyPrintJson { get; set; } = false;
}

/// <summary>
/// Default implementation of the export service.
/// </summary>
public sealed class ExportService : IExportService
{
    public async Task ExportToCsvAsync(
        IAsyncEnumerable<QueryPage> pages,
        Stream outputStream,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(options);

        using var writer = new StreamWriter(outputStream, Encoding.UTF8, leaveOpen: true);
        var headersWritten = false;
        IReadOnlyList<ColumnInfo>? columns = null;

        await foreach (var page in pages.WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();

            // Write headers on first page if enabled
            if (!headersWritten && options.IncludeHeaders)
            {
                columns = page.Columns;
                var headerLine = string.Join(",", columns.Select(c => EscapeCsvField(c.Name)));
                await writer.WriteLineAsync(headerLine);
                headersWritten = true;
            }

            // Write data rows
            foreach (var row in page.Rows)
            {
                ct.ThrowIfCancellationRequested();
                var values = columns != null
                    ? columns.Select(c => FormatCsvValue(row.GetValueOrDefault(c.Name), options))
                    : row.Values.Select(v => FormatCsvValue(v, options));
                var line = string.Join(",", values);
                await writer.WriteLineAsync(line);
            }

            // Flush periodically to avoid memory pressure
            await writer.FlushAsync();
        }

        await writer.FlushAsync();
    }

    public async Task ExportToJsonAsync(
        IAsyncEnumerable<QueryPage> pages,
        Stream outputStream,
        ExportOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(outputStream);
        ArgumentNullException.ThrowIfNull(options);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = options.PrettyPrintJson,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await using var jsonWriter = new Utf8JsonWriter(outputStream, new JsonWriterOptions
        {
            Indented = options.PrettyPrintJson
        });

        jsonWriter.WriteStartArray();

        await foreach (var page in pages.WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();

            foreach (var row in page.Rows)
            {
                ct.ThrowIfCancellationRequested();
                jsonWriter.WriteStartObject();

                foreach (var kvp in row)
                {
                    var key = kvp.Key;
                    var value = kvp.Value;

                    if (value == null)
                    {
                        jsonWriter.WriteNull(key);
                    }
                    else if (value is string str)
                    {
                        jsonWriter.WriteString(key, str);
                    }
                    else if (value is bool b)
                    {
                        jsonWriter.WriteBoolean(key, b);
                    }
                    else if (value is DateTime dt)
                    {
                        jsonWriter.WriteString(key, dt.ToString(options.DateTimeFormat));
                    }
                    else if (value is DateTimeOffset dto)
                    {
                        jsonWriter.WriteString(key, dto.ToString(options.DateTimeFormat));
                    }
                    else if (value is int i)
                    {
                        jsonWriter.WriteNumber(key, i);
                    }
                    else if (value is long l)
                    {
                        jsonWriter.WriteNumber(key, l);
                    }
                    else if (value is double d)
                    {
                        jsonWriter.WriteNumber(key, d);
                    }
                    else if (value is decimal dec)
                    {
                        jsonWriter.WriteNumber(key, dec);
                    }
                    else if (value is float f)
                    {
                        jsonWriter.WriteNumber(key, f);
                    }
                    else if (value is byte[] bytes)
                    {
                        jsonWriter.WriteBase64String(key, bytes);
                    }
                    else
                    {
                        // Fall back to string representation
                        jsonWriter.WriteString(key, value.ToString());
                    }
                }

                jsonWriter.WriteEndObject();
            }

            // Flush periodically
            await jsonWriter.FlushAsync();
        }

        jsonWriter.WriteEndArray();
        await jsonWriter.FlushAsync();
    }

    private static string EscapeCsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        // Escape quotes by doubling them
        var escaped = value.Replace("\"", "\"\"");

        // Wrap in quotes if contains comma, quote, or newline
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            escaped = $"\"{escaped}\"";
        }

        return escaped;
    }

    private static string FormatCsvValue(object? value, ExportOptions options)
    {
        if (value == null)
        {
            return "";
        }

        if (value is DateTime dt && options.FormatDatesAsIso)
        {
            return EscapeCsvField(dt.ToString(options.DateTimeFormat));
        }

        if (value is DateTimeOffset dto && options.FormatDatesAsIso)
        {
            return EscapeCsvField(dto.ToString(options.DateTimeFormat));
        }

        if (value is bool b)
        {
            return b ? "true" : "false";
        }

        return EscapeCsvField(value.ToString());
    }
}
