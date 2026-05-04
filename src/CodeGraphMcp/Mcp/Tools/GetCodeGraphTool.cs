using System.ComponentModel;
using CodeGraphMcp.Core.Domain;
using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Core.Utilities;
using CodeGraphMcp.Startup;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

public static class CodeNodeExtensions
{
    public static string RelativePath(this CodeNode node, string root)
    {
        return Path.GetRelativePath(root, node.FilePath);
    }
}

[McpServerToolType]
public static class GetCodeGraphTool
{
    [McpServerTool, Description("Returns the full repository code graph as compact JSON for AI agent context.")]
    public static string GetCodeGraph(
        GraphStore store,
        AppSettings settings,
        [Description("Max token budget. Defaults to 80000.")] int maxTokens = 80000)
    {
        var graph   = store.LoadGraph(settings.RootPath);
        var json    = graph.ToCompactJson();
        var tokens  = TokenEstimator.Estimate(json);
        var (n, e)  = store.GetStats();

        if (tokens <= maxTokens)
            return json;

        // Over budget: return a compressed summary (file list + edges only, no method nodes)
        var summary = new
        {
            rootPath  = graph.RootPath,
            generated = graph.GeneratedAt,
            stats     = new { nodeCount = n, edgeCount = e, estimatedTokens = tokens },
            files     = graph.Nodes.Values
                .Where(nd => nd.Kind == CodeGraphMcp.Core.Domain.NodeKind.File)
                .Select(nd => new { RelativePath = nd.RelativePath(settings.RootPath), nd.Language, nd.Name }),
            edges     = graph.Edges.Take(2000),
        };

        return System.Text.Json.JsonSerializer.Serialize(summary,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
    }
}
