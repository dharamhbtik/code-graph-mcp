using CodeGraphMcp.Core.Domain;
using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Core.Parsing;
using CodeGraphMcp.Core.Scanning;
using CodeGraphMcp.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Orchestration;

public sealed class GraphOrchestrator(
    RepositoryScanner scanner,
    GraphStore store,
    IEnumerable<ILanguageParser> parsers,
    ILogger<GraphOrchestrator> logger)
{
    private readonly Dictionary<Language, ILanguageParser> _parsers =
        parsers.ToDictionary(p => p.Language);

    public GraphStore Store => store;

    // ── Full build ───────────────────────────────────────────────────────────

    public async Task BuildAsync(string rootPath, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        logger.LogInformation("Full graph build starting for {Root}", rootPath);

        var files = await scanner.ScanAsync(rootPath, ct);
        var semaphore = new SemaphoreSlim(8);

        await Parallel.ForEachAsync(files, ct, async (file, innerCt) =>
        {
            await semaphore.WaitAsync(innerCt);
            try { await ParseAndUpsert(file.FilePath, innerCt); }
            finally { semaphore.Release(); }
        });

        var (n, e) = store.GetStats();
        logger.LogInformation(
            "Build complete: {Files} files, {Nodes} nodes, {Edges} edges in {Ms}ms",
            files.Count, n, e, sw.ElapsedMilliseconds);
    }

    // ── Incremental rebuild of a single file ─────────────────────────────────

    public async Task RebuildFileAsync(string filePath, CancellationToken ct = default)
    {
        logger.LogDebug("Incremental rebuild: {File}", filePath);
        store.RemoveFileNodes(filePath);
        await ParseAndUpsert(filePath, ct);
    }

    // ── Rename ──────────────────────────────────────────────────────────────

    public async Task RenameFileAsync(string oldPath, string newPath, CancellationToken ct = default)
    {
        store.RemoveFileNodes(oldPath);
        if (File.Exists(newPath))
            await ParseAndUpsert(newPath, ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task ParseAndUpsert(string filePath, CancellationToken ct)
    {
        var lang = LanguageDetector.Detect(filePath);
        if (!_parsers.TryGetValue(lang, out var parser)) return;

        var result = await parser.ParseAsync(filePath, ct);

        foreach (var node in result.Nodes) store.UpsertNode(node);
        foreach (var edge in result.Edges) store.UpsertEdge(edge);
    }
}
