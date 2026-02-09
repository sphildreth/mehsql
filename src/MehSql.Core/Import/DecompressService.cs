using System.Formats.Tar;
using System.IO.Compression;
using Serilog;

namespace MehSql.Core.Import;

/// <summary>
/// Handles transparent decompression of compressed import files.
/// Supports .gz, .zip, .tar.gz/.tgz formats using built-in .NET APIs.
/// </summary>
public sealed class DecompressService
{
    /// <summary>
    /// Result of a decompression operation.
    /// </summary>
    /// <param name="ExtractedPath">Path to the extracted file or directory.</param>
    /// <param name="TempDirectory">Temp directory that should be cleaned up, or null if no temp was used.</param>
    /// <param name="OriginalPath">The original compressed file path.</param>
    public sealed record DecompressResult(string ExtractedPath, string? TempDirectory, string OriginalPath);

    /// <summary>
    /// Returns true if the file appears to be a compressed archive.
    /// </summary>
    public static bool IsCompressed(string filePath)
    {
        var lower = filePath.ToLowerInvariant();
        return lower.EndsWith(".gz") || lower.EndsWith(".zip") ||
               lower.EndsWith(".tgz") || lower.EndsWith(".tar.gz");
    }

    /// <summary>
    /// Decompress a file to a working directory. Returns the path to the extracted content.
    /// If the file is not compressed, returns it unchanged.
    /// </summary>
    public async Task<DecompressResult> DecompressAsync(
        string filePath, string tempBase, IProgress<ImportProgress>? progress = null, CancellationToken ct = default)
    {
        var lower = filePath.ToLowerInvariant();

        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz"))
            return await ExtractTarGzAsync(filePath, tempBase, progress, ct);

        if (lower.EndsWith(".gz"))
            return await ExtractGzAsync(filePath, tempBase, progress, ct);

        if (lower.EndsWith(".zip"))
            return await ExtractZipAsync(filePath, tempBase, progress, ct);

        // Not compressed — return as-is
        return new DecompressResult(filePath, null, filePath);
    }

    private static async Task<DecompressResult> ExtractGzAsync(
        string filePath, string tempBase, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var tempDir = CreateTempDir(tempBase);
        // Remove .gz extension to get the inner filename
        var innerName = Path.GetFileNameWithoutExtension(filePath);
        var outputPath = Path.Combine(tempDir, innerName);

        progress?.Report(new ImportProgress
        {
            Phase = ImportPhase.Analyzing,
            Message = $"Decompressing {Path.GetFileName(filePath)}..."
        });

        Log.Information("Decompressing GZ: {Source} → {Dest}", filePath, outputPath);

        await using var inputStream = File.OpenRead(filePath);
        await using var gzStream = new GZipStream(inputStream, CompressionMode.Decompress);
        await using var outputStream = File.Create(outputPath);
        await gzStream.CopyToAsync(outputStream, ct);

        return new DecompressResult(outputPath, tempDir, filePath);
    }

    private static async Task<DecompressResult> ExtractZipAsync(
        string filePath, string tempBase, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var tempDir = CreateTempDir(tempBase);

        progress?.Report(new ImportProgress
        {
            Phase = ImportPhase.Analyzing,
            Message = $"Extracting {Path.GetFileName(filePath)}..."
        });

        Log.Information("Extracting ZIP: {Source} → {Dest}", filePath, tempDir);

        await Task.Run(() => ZipFile.ExtractToDirectory(filePath, tempDir), ct);

        // If the zip contains a single file, return that file's path
        var files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
        if (files.Length == 1)
            return new DecompressResult(files[0], tempDir, filePath);

        // If it contains a single directory, return that directory
        var dirs = Directory.GetDirectories(tempDir);
        if (dirs.Length == 1 && files.All(f => f.StartsWith(dirs[0])))
            return new DecompressResult(dirs[0], tempDir, filePath);

        // Multiple files — return the temp dir itself
        return new DecompressResult(tempDir, tempDir, filePath);
    }

    private static async Task<DecompressResult> ExtractTarGzAsync(
        string filePath, string tempBase, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        var tempDir = CreateTempDir(tempBase);

        progress?.Report(new ImportProgress
        {
            Phase = ImportPhase.Analyzing,
            Message = $"Extracting {Path.GetFileName(filePath)}..."
        });

        Log.Information("Extracting TAR.GZ: {Source} → {Dest}", filePath, tempDir);

        await using var inputStream = File.OpenRead(filePath);
        await using var gzStream = new GZipStream(inputStream, CompressionMode.Decompress);

        await TarFile.ExtractToDirectoryAsync(gzStream, tempDir, overwriteFiles: true, cancellationToken: ct);

        // If the tar contains a single top-level directory, return that
        var dirs = Directory.GetDirectories(tempDir);
        if (dirs.Length == 1)
            return new DecompressResult(dirs[0], tempDir, filePath);

        return new DecompressResult(tempDir, tempDir, filePath);
    }

    private static string CreateTempDir(string tempBase)
    {
        var dir = Path.Combine(tempBase, $"mehsql-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Clean up a temp directory created during decompression.
    /// </summary>
    public static void Cleanup(string? tempDirectory)
    {
        if (string.IsNullOrEmpty(tempDirectory)) return;
        try
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
            Log.Debug("Cleaned up temp directory: {Path}", tempDirectory);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to clean up temp directory: {Path}", tempDirectory);
        }
    }
}
