using System.ComponentModel;
using CodeGraphMcp.Core.Graph;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

[McpServerToolType]
public static class GetFileContextTool
{
    [McpServerTool, Description("Returns all nodes and edges for a specific file path, including connected nodes up to hopDepth hops away.")]
    public static string GetFileContext(
        GraphStore store,
        [Description("Relative or absolute file path")] string filePath,
        [Description("Number of hops to traverse from the file's nodes. Default 2.")] int hopDepth = 2)
    {
        var fileNodes = store.GetNodesByFile(filePath);
        if (fileNodes.Count == 0)
            return $"{{\"error\":\"No nodes found for {filePath}\"}}";

        var visitedNodeIds = new HashSet<string>(fileNodes.Select(n => n.Id));
        var allEdges       = new List<CodeGraphMcp.Core.Domain.CodeEdge>();

        // BFS up to hopDepth
        var frontier = new Queue<string>(fileNodes.Select(n => n.Id));
        for (int hop = 0; hop < hopDepth && frontier.Count > 0; hop++)
        {
            var nextFrontier = new Queue<string>();
            while (frontier.Count > 0)
            {
                var id    = frontier.Dequeue();
                var edges = store.GetEdgesForNode(id);
                foreach (var edge in edges)
                {
                    allEdges.Add(edge);
                    var otherId = edge.SourceId == id ? edge.TargetId : edge.SourceId;
                    if (visitedNodeIds.Add(otherId)) nextFrontier.Enqueue(otherId);
                }
            }
            frontier = nextFrontier;
        }

        var result = new
        {
            filePath,
            nodes = visitedNodeIds.Count,
            edges = allEdges.Count,
            nodeList = fileNodes,
            edgeList = allEdges.DistinctBy(e => e.Id),
        };

        return System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
    }
}
