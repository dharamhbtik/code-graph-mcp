using System.ComponentModel;
using CodeGraphMcp.Core.Domain;
using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Startup;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

[McpServerToolType]
public static class GetSymbolTool
{
    [McpServerTool, Description("Searches for a symbol by name and returns its definition, file location, connected symbols with names, and code snippet.")]
    public static string GetSymbol(
        GraphStore store,
        AppSettings settings,
        [Description("Symbol name to search for (partial match, case-insensitive)")] string symbolName)
    {
        var nodes = store.SearchNodes(symbolName);
        if (nodes.Count == 0)
            return $"{{\"error\":\"Symbol '{symbolName}' not found in the code graph.\"}}";

        var root = settings.RootPath;

        // Batch-load all edges for all matched nodes in one query
        var allNodeIds = nodes.Select(n => n.Id).ToList();
        var allEdges   = store.GetEdgesForNodes(allNodeIds);

        // Collect all connected node IDs and batch-resolve them
        var connectedIds = allEdges
            .SelectMany(e => new[] { e.SourceId, e.TargetId })
            .Except(allNodeIds)
            .Distinct()
            .ToList();
        var connectedNodes = store.GetNodesByIds(connectedIds)
            .ToDictionary(n => n.Id);

        // Also map our own matched nodes for edge resolution
        var localMap = nodes.ToDictionary(n => n.Id);

        var enriched = nodes.Select(node =>
        {
            var nodeEdges = allEdges.Where(e => e.SourceId == node.Id || e.TargetId == node.Id).ToList();

            // Resolve edges to human-readable connection descriptions
            var connections = nodeEdges
                .Where(e => e.Kind != RelationKind.Contains) // Skip noisy containment
                .Select(e =>
                {
                    var isSource = e.SourceId == node.Id;
                    var otherId  = isSource ? e.TargetId : e.SourceId;

                    string otherName;
                    string otherKind;
                    string otherPath;

                    if (connectedNodes.TryGetValue(otherId, out var other))
                    {
                        otherName = other.Name;
                        otherKind = other.Kind.ToString();
                        otherPath = Path.GetRelativePath(root, other.FilePath);
                    }
                    else if (localMap.TryGetValue(otherId, out var local))
                    {
                        otherName = local.Name;
                        otherKind = local.Kind.ToString();
                        otherPath = Path.GetRelativePath(root, local.FilePath);
                    }
                    else
                    {
                        otherName = otherId;
                        otherKind = "Unknown";
                        otherPath = "";
                    }

                    return new
                    {
                        Direction = isSource ? "outgoing" : "incoming",
                        Relation  = e.Kind.ToString(),
                        Symbol    = otherName,
                        Kind      = otherKind,
                        Path      = otherPath,
                    };
                })
                .ToList();

            // Get a better snippet — use StartLine to EndLine for the full definition
            var snippet = TryGetSnippet(node.FilePath, node.StartLine, node.EndLine);

            return new
            {
                name     = node.Name,
                fullName = node.FullName,
                kind     = node.Kind.ToString(),
                path     = Path.GetRelativePath(root, node.FilePath),
                line     = node.StartLine,
                endLine  = node.EndLine,
                language = node.Language.ToString(),
                summary  = node.Summary,
                snippet,
                connections,
            };
        });

        return System.Text.Json.JsonSerializer.Serialize(enriched,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
    }

    /// <summary>
    /// Extracts a code snippet covering the symbol's full definition.
    /// Uses the node's start/end lines for accurate extraction, with a
    /// max cap to avoid dumping entire 500-line classes.
    /// </summary>
    private static string? TryGetSnippet(string filePath, int startLine, int endLine, int maxLines = 30)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var lines = File.ReadAllLines(filePath);

            // Include 2 lines before for context (decorators, attributes, doc comments)
            var from = Math.Max(0, startLine - 3);
            // Cap the snippet at maxLines to avoid dumping entire large classes
            var to   = Math.Min(lines.Length - 1, Math.Min(endLine - 1, from + maxLines));

            return string.Join('\n', lines[from..(to + 1)]);
        }
        catch { return null; }
    }
}
