using System.ComponentModel;
using CodeGraphMcp.Core.Domain;
using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Startup;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

[McpServerToolType]
public static class GetFileContextTool
{
    [McpServerTool, Description("Returns all nodes and edges for a specific file path, including connected nodes up to hopDepth hops away. Accepts both relative and absolute paths.")]
    public static string GetFileContext(
        GraphStore store,
        AppSettings settings,
        [Description("Relative or absolute file path")] string filePath,
        [Description("Number of hops to traverse from the file's nodes. Default 2.")] int hopDepth = 2)
    {
        // Resolve relative paths against stored absolute paths
        var resolvedPath = store.ResolveFilePath(filePath);

        var fileNodes = store.GetNodesByFile(resolvedPath);
        if (fileNodes.Count == 0)
            return $"{{\"error\":\"No nodes found for '{filePath}'. Ensure the file has been indexed.\"}}";

        var root = settings.RootPath;
        var visitedNodeIds = new HashSet<string>(fileNodes.Select(n => n.Id));
        var allEdges       = new HashSet<CodeEdge>();

        // BFS using batch queries — one query per hop instead of one per node
        var frontierIds = new HashSet<string>(fileNodes.Select(n => n.Id));
        for (int hop = 0; hop < hopDepth && frontierIds.Count > 0; hop++)
        {
            var edges = store.GetEdgesForNodes(frontierIds.ToList());
            var nextFrontier = new HashSet<string>();

            foreach (var edge in edges)
            {
                allEdges.Add(edge);
                var otherId = frontierIds.Contains(edge.SourceId) ? edge.TargetId : edge.SourceId;
                if (visitedNodeIds.Add(otherId))
                    nextFrontier.Add(otherId);
            }

            frontierIds = nextFrontier;
        }

        // Batch-load all connected nodes so we can return their details
        var connectedNodeIds = visitedNodeIds.Except(fileNodes.Select(n => n.Id)).ToList();
        var connectedNodes   = store.GetNodesByIds(connectedNodeIds);

        // Build structured response with grouping by kind
        var fileSymbols = fileNodes
            .Where(n => n.Kind != NodeKind.File)
            .Select(n => new
            {
                n.Id,
                Kind = n.Kind.ToString(),
                n.Name,
                n.FullName,
                n.StartLine,
                n.EndLine,
            })
            .ToList();

        var fileInfo = fileNodes.FirstOrDefault(n => n.Kind == NodeKind.File);

        // Group connected nodes by relationship kind for clarity
        var connectedByKind = connectedNodes
            .GroupBy(n => n.Kind)
            .Select(g => new
            {
                Kind  = g.Key.ToString(),
                Nodes = g.Select(n => new
                {
                    n.Id,
                    n.Name,
                    n.FullName,
                    Path = Path.GetRelativePath(root, n.FilePath),
                    n.StartLine,
                }).ToList()
            })
            .ToList();

        // Resolve edge IDs to human-readable names
        var allNodeMap = fileNodes
            .Concat(connectedNodes)
            .DistinctBy(n => n.Id)
            .ToDictionary(n => n.Id);

        var readableEdges = allEdges
            .Where(e => e.Kind != RelationKind.Contains) // Contains is implied by file membership
            .DistinctBy(e => e.Id)
            .Select(e => new
            {
                Source   = allNodeMap.TryGetValue(e.SourceId, out var s) ? s.Name : e.SourceId,
                Target   = allNodeMap.TryGetValue(e.TargetId, out var t) ? t.Name : e.TargetId,
                Relation = e.Kind.ToString(),
            })
            .ToList();

        var result = new
        {
            file = new
            {
                path     = Path.GetRelativePath(root, resolvedPath),
                language = fileInfo?.Language.ToString() ?? "Unknown",
            },
            symbols        = fileSymbols,
            connectedNodes = connectedByKind,
            relationships  = readableEdges,
            stats = new
            {
                symbolsInFile   = fileSymbols.Count,
                connectedNodes  = connectedNodes.Count,
                totalEdges      = readableEdges.Count,
                hopsTraversed   = hopDepth,
            },
        };

        return System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
    }
}
