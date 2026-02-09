using System.Text;
using Serilog;

namespace MehSql.Core.Import;

/// <summary>
/// Detects the import format from a file's content (magic bytes, header lines, directory structure).
/// </summary>
public static class ImportFormatDetector
{
    private static readonly byte[] SqliteMagic = "SQLite format 3\0"u8.ToArray();

    /// <summary>
    /// Detect the format of a file or directory.
    /// </summary>
    public static ImportFormat Detect(string path)
    {
        if (Directory.Exists(path))
            return DetectDirectory(path);

        if (!File.Exists(path))
            return ImportFormat.Unknown;

        // Check magic bytes first (binary formats)
        var magic = ReadMagicBytes(path, 16);
        if (magic.Length >= 16 && magic.AsSpan(0, 16).SequenceEqual(SqliteMagic))
            return ImportFormat.SQLite;

        // Text-based detection: read the first few significant lines
        var header = ReadHeaderLines(path, 30);
        return DetectFromHeader(header);
    }

    /// <summary>
    /// Detect format from header lines of a text dump file.
    /// </summary>
    internal static ImportFormat DetectFromHeader(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // PostgreSQL pg_dump markers
            if (trimmed.StartsWith("-- PostgreSQL database dump", StringComparison.OrdinalIgnoreCase))
                return ImportFormat.PgDump;
            if (trimmed.StartsWith("\\restrict") || trimmed.StartsWith("SET statement_timeout"))
                return ImportFormat.PgDump;
            if (trimmed.StartsWith("SELECT pg_catalog.set_config"))
                return ImportFormat.PgDump;

            // MySQL mysqldump markers
            if (trimmed.StartsWith("-- MySQL dump", StringComparison.OrdinalIgnoreCase))
                return ImportFormat.MysqlDump;
            if (trimmed.StartsWith("-- MariaDB dump", StringComparison.OrdinalIgnoreCase))
                return ImportFormat.MysqlDump;
            if (trimmed.Contains("/*!40101 SET"))
                return ImportFormat.MysqlDump;
            if (trimmed.StartsWith("-- Server version"))
                return ImportFormat.MysqlDump;
        }

        return ImportFormat.Unknown;
    }

    private static ImportFormat DetectDirectory(string path)
    {
        // MySQL Shell dump format: look for @.json metadata file
        if (File.Exists(Path.Combine(path, "@.json")))
            return ImportFormat.MysqlShellDump;

        // Might be extracted archive containing a single dump file
        var files = Directory.GetFiles(path, "*.sql", SearchOption.TopDirectoryOnly);
        if (files.Length == 1)
            return Detect(files[0]);

        // Check if there are .tsv.zst files (MySQL Shell data)
        var zstFiles = Directory.GetFiles(path, "*.tsv.zst", SearchOption.AllDirectories);
        if (zstFiles.Length > 0)
            return ImportFormat.MysqlShellDump;

        Log.Warning("Could not detect import format for directory: {Path}", path);
        return ImportFormat.Unknown;
    }

    private static byte[] ReadMagicBytes(string path, int count)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[count];
            var read = fs.Read(buf, 0, count);
            return buf[..read];
        }
        catch
        {
            return [];
        }
    }

    private static List<string> ReadHeaderLines(string path, int maxLines)
    {
        var lines = new List<string>();
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            for (var i = 0; i < maxLines && !reader.EndOfStream; i++)
            {
                var line = reader.ReadLine();
                if (line is not null)
                    lines.Add(line);
            }
        }
        catch
        {
            // Can't read as text — likely binary
        }
        return lines;
    }

    /// <summary>
    /// Returns a user-friendly display name for the format.
    /// </summary>
    public static string FormatDisplayName(ImportFormat format) => format switch
    {
        ImportFormat.SQLite => "SQLite Database",
        ImportFormat.PgDump => "PostgreSQL Dump (pg_dump)",
        ImportFormat.MysqlDump => "MySQL Dump (mysqldump)",
        ImportFormat.MysqlShellDump => "MySQL Shell Dump",
        _ => "Unknown Format"
    };
}
