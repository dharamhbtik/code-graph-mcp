using System.Security.Cryptography;
using CodeGraphMcp.Core.Domain;
using CodeGraphMcp.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Scanning;

public sealed class RepositoryScanner(ILogger<RepositoryScanner> logger)
{
    private static readonly HashSet<string> _ignoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".idea",
        "dist", "out", "coverage", "TestResults",
    };

    public async Task<IReadOnlyList<SourceFile>> ScanAsync(
        string rootPath,
        CancellationToken ct = default)
    {
        logger.LogInformation("Scanning repository at {Root}", rootPath);

        var allFiles = EnumerateTrackedFiles(rootPath);
        var semaphore = new SemaphoreSlim(8);
        var results = new System.Collections.Concurrent.ConcurrentBag<SourceFile>();

        await Parallel.ForEachAsync(allFiles, ct, async (filePath, innerCt) =>
        {
            await semaphore.WaitAsync(innerCt);
            try
            {
                var hash = await ComputeHashAsync(filePath, innerCt);
                var lang = LanguageDetector.Detect(filePath);
                var rel  = Path.GetRelativePath(rootPath, filePath);
                var modified = File.GetLastWriteTimeUtc(filePath);
                results.Add(new SourceFile(filePath, rel, lang, hash, modified));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read {File}", filePath);
            }
            finally
            {
                semaphore.Release();
            }
        });

        logger.LogInformation("Discovered {Count} tracked files", results.Count);
        return results.ToList();
    }

    private static IEnumerable<string> EnumerateTrackedFiles(string root)
    {
        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !IsInIgnoredDir(f, root))
            .Where(f => LanguageDetector.IsTracked(f));
    }

    private static bool IsInIgnoredDir(string filePath, string root)
    {
        var relative = Path.GetRelativePath(root, filePath);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => _ignoredDirs.Contains(p));
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var bytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
