using System.ComponentModel;
using CodeGraphMcp.Core.Graph;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

[McpServerToolType]
public static class GetSymbolTool
{
    [McpServerTool, Description("Searches for a symbol by name and returns its definition, file location, and direct edges.")]
    public static string GetSymbol(
        GraphStore store,
        [Description("Symbol name to search for (partial match, case-insensitive)")] string symbolName)
    {
        var nodes = store.SearchNodes(symbolName);
        if (nodes.Count == 0)
            return $"{{\"error\":\"Symbol '{symbolName}' not found\"}}";

        var enriched = nodes.Select(node =>
        {
            var edges = store.GetEdgesForNode(node.Id);
            string? snippet = TryGetSnippet(node.FilePath, node.StartLine);
            return new { node, edges, snippet };
        });

        return System.Text.Json.JsonSerializer.Serialize(enriched,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
    }

    private static string? TryGetSnippet(string filePath, int startLine, int contextLines = 5)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            var lines = File.ReadAllLines(filePath);
            var from  = Math.Max(0, startLine - contextLines - 1);
            var to    = Math.Min(lines.Length - 1, startLine + contextLines - 1);
            return string.Join('\n', lines[from..(to + 1)]);
        }
        catch { return null; }
    }
}
