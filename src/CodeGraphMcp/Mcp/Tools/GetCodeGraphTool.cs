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
    [McpServerTool, Description("Returns the full repository code graph as compact JSON for AI agent context. Automatically compresses if over token budget.")]
    public static string GetCodeGraph(
        GraphStore store,
        AppSettings settings,
        [Description("Max token budget. Defaults to 80000.")] int maxTokens = 80000)
    {
        var graph  = store.LoadGraph(settings.RootPath);
        var json   = graph.ToCompactJson();
        var tokens = TokenEstimator.Estimate(json);

        if (tokens <= maxTokens)
            return json;

        // Over budget: return a meaningful compressed summary that preserves
        // type nodes (classes, interfaces, etc.) — not just files.
        var root = settings.RootPath;

        // Group nodes by kind for structured output
        var typeNodes = graph.Nodes.Values
            .Where(n => n.Kind is not (NodeKind.File or NodeKind.Property or NodeKind.Field))
            .Select(n => new
            {
                n.Id,
                Kind = n.Kind.ToString(),
                n.Name,
                n.FullName,
                Path = n.RelativePath(root),
                n.StartLine,
            })
            .ToList();

        var fileNodes = graph.Nodes.Values
            .Where(n => n.Kind == NodeKind.File)
            .Select(n => new
            {
                Path     = n.RelativePath(root),
                Language = n.Language.ToString(),
                Symbols  = graph.Nodes.Values.Count(cn =>
                    cn.FilePath == n.FilePath &&
                    cn.Kind is NodeKind.Class or NodeKind.Interface or NodeKind.Function or NodeKind.Component),
            })
            .ToList();

        // Include the most important edges — sort by weight descending
        // and prioritize structural relationships over containment
        var importantEdges = graph.Edges
            .Where(e => e.Kind is not RelationKind.Contains)
            .OrderByDescending(e => e.Weight)
            .Take(2000)
            .Select(e =>
            {
                var srcName = graph.Nodes.TryGetValue(e.SourceId, out var s) ? s.Name : e.SourceId;
                var tgtName = graph.Nodes.TryGetValue(e.TargetId, out var t) ? t.Name : e.TargetId;
                return new
                {
                    Source   = srcName,
                    Target   = tgtName,
                    Relation = e.Kind.ToString(),
                };
            })
            .ToList();

        var summary = new
        {
            _note     = "Graph compressed — exceeded token budget. Use GetFileContext or GetSymbol for details.",
            rootPath  = root,
            generated = graph.GeneratedAt,
            stats     = new
            {
                totalNodes      = graph.Nodes.Count,
                totalEdges      = graph.Edges.Count,
                fullGraphTokens = tokens,
                budget          = maxTokens,
            },
            files = fileNodes,
            types = typeNodes,
            edges = importantEdges,
        };

        return System.Text.Json.JsonSerializer.Serialize(summary,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
    }
}
