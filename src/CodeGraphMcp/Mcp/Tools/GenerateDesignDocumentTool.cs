using System.ComponentModel;
using System.Text;
using CodeGraphMcp.Core.Domain;
using CodeGraphMcp.Core.Graph;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

[McpServerToolType]
public static class GenerateDesignDocumentTool
{
    [McpServerTool, Description("Generates design documents containing Mermaid diagrams (class diagrams or dependency/flow graphs) for a given symbol to visualize architecture.")]
    public static string GenerateDesignDocument(
        GraphStore store,
        [Description("Type of diagram: 'class' (for class structures and inheritance) or 'flow' (for dependencies, calls, and workflow)")] string diagramType,
        [Description("The exact or partial name of the class, module, or symbol to diagram.")] string symbolName,
        [Description("Depth of relations to traverse. Default is 1 for class, 2 for flow. Use -1 for auto.")] int depth = -1)
    {
        var targetNodes = store.SearchNodes(symbolName);
        if (targetNodes.Count == 0)
            return $"Error: Symbol '{symbolName}' not found in the code graph.";

        bool isFlow = !diagramType.Equals("class", StringComparison.OrdinalIgnoreCase);

        // Apply correct default depth based on diagram type
        if (depth < 0)
            depth = isFlow ? 2 : 1;

        // Prefer concrete classes/structs over interfaces so the flow graph
        // starts from the implementation, not the abstraction.
        var rootNode = targetNodes.FirstOrDefault(n => n.Kind is NodeKind.Class or NodeKind.Struct or NodeKind.Component or NodeKind.Record)
                    ?? targetNodes.FirstOrDefault(n => n.Kind is NodeKind.Interface)
                    ?? targetNodes[0];

        // Load the full graph — LoadGraph returns every node/edge in the DB
        var graph = store.LoadGraph(rootNode.FilePath);
        var nodeMap = graph.Nodes;

        var visitedNodes = new HashSet<string> { rootNode.Id };
        var activeEdges = new HashSet<CodeEdge>();
        var frontier = new Queue<string>();
        frontier.Enqueue(rootNode.Id);

        for (int i = 0; i < depth && frontier.Count > 0; i++)
        {
            var nextFrontier = new Queue<string>();
            while (frontier.Count > 0)
            {
                var currentId = frontier.Dequeue();

                // Find all edges connected to this node
                var edges = graph.Edges.Where(e => e.SourceId == currentId || e.TargetId == currentId).ToList();

                foreach (var edge in edges)
                {
                    // Filter edges based on diagram type
                    if (!isFlow)
                    {
                        if (edge.Kind is not (RelationKind.Inherits or RelationKind.Implements or RelationKind.Contains)) continue;
                    }
                    else // flow
                    {
                        if (edge.Kind is RelationKind.Contains) continue; // Skip internal members for flow diagrams
                    }

                    activeEdges.Add(edge);
                    var otherId = edge.SourceId == currentId ? edge.TargetId : edge.SourceId;

                    if (visitedNodes.Add(otherId))
                    {
                        nextFrontier.Enqueue(otherId);
                    }
                }
            }
            frontier = nextFrontier;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Design Document: {rootNode.Name}");
        sb.AppendLine();

        if (!isFlow)
        {
            sb.AppendLine("## Class Diagram");
            sb.AppendLine("```mermaid");
            sb.AppendLine("classDiagram");

            // Output node definitions
            foreach (var id in visitedNodes)
            {
                if (!nodeMap.TryGetValue(id, out var node)) continue;
                if (node.Kind is NodeKind.Method or NodeKind.Property or NodeKind.Field) continue; // Skip standalone members

                sb.AppendLine($"    class {SanitizeId(node.Name)} {{");
                sb.AppendLine($"        <<{node.Kind}>>");

                // Add properties/methods belonging to this class
                var members = activeEdges.Where(e => e.SourceId == id && e.Kind == RelationKind.Contains)
                                         .Select(e => nodeMap.TryGetValue(e.TargetId, out var m) ? m : null)
                                         .Where(m => m != null);

                foreach (var member in members)
                {
                    var prefix = member!.Kind is NodeKind.Method or NodeKind.Function ? "+" : "~";
                    var suffix = member.Kind is NodeKind.Method or NodeKind.Function ? "()" : "";
                    sb.AppendLine($"        {prefix}{SanitizeId(member.Name)}{suffix}");
                }
                sb.AppendLine("    }");
            }

            // Output relationships
            foreach (var edge in activeEdges)
            {
                if (edge.Kind == RelationKind.Contains) continue; // Handled inside class definition

                if (nodeMap.TryGetValue(edge.SourceId, out var src) && nodeMap.TryGetValue(edge.TargetId, out var tgt))
                {
                    var arrow = edge.Kind switch
                    {
                        RelationKind.Inherits => "<|--",
                        RelationKind.Implements => "<|..",
                        _ => "-->"
                    };
                    sb.AppendLine($"    {SanitizeId(tgt.Name)} {arrow} {SanitizeId(src.Name)} : {edge.Kind}");
                }
            }

            sb.AppendLine("```");
        }
        else // flow
        {
            sb.AppendLine("## Dependency & Call Flow");
            sb.AppendLine("```mermaid");
            sb.AppendLine("graph TD");

            foreach (var id in visitedNodes)
            {
                if (!nodeMap.TryGetValue(id, out var node)) continue;
                if (node.Kind == NodeKind.File) continue; // Skip files to keep it clean

                var safeId = MermaidNodeId(id);
                var safeLabel = SanitizeLabel(node.Name);
                var shape = node.Kind switch
                {
                    NodeKind.Interface   => $"{safeId}{{\"{safeLabel}\"}}",
                    NodeKind.Method or NodeKind.Function => $"{safeId}([\"{safeLabel}\"])",
                    NodeKind.Enum        => $"{safeId}[/\"{safeLabel}\"/]",
                    _                    => $"{safeId}[\"{safeLabel}\"]"
                };
                sb.AppendLine($"    {shape}");
            }

            foreach (var edge in activeEdges)
            {
                if (nodeMap.TryGetValue(edge.SourceId, out var src) && nodeMap.TryGetValue(edge.TargetId, out var tgt))
                {
                    if (src.Kind == NodeKind.File || tgt.Kind == NodeKind.File) continue;

                    var srcId = MermaidNodeId(edge.SourceId);
                    var tgtId = MermaidNodeId(edge.TargetId);
                    var line = edge.Kind switch
                    {
                        RelationKind.Calls      => "-->|calls|",
                        RelationKind.Imports     => "-.->|imports|",
                        RelationKind.Binds       => "==>|binds|",
                        RelationKind.Inherits    => "-->|inherits|",
                        RelationKind.Implements  => "-.->|implements|",
                        RelationKind.References  => "-->|references|",
                        RelationKind.DependsOn   => "==>|depends on|",
                        RelationKind.Declares    => "-->|declares|",
                        _                        => "-->|related|"
                    };
                    sb.AppendLine($"    {srcId} {line} {tgtId}");
                }
            }
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Creates a Mermaid-safe node identifier from a SHA hash.
    /// Prefixes with 'n' so the ID always starts with a letter.
    /// </summary>
    private static string MermaidNodeId(string hashId)
        => $"n{new string(hashId.Where(char.IsLetterOrDigit).ToArray())}";

    /// <summary>
    /// Sanitizes a name for use as a Mermaid class-diagram identifier.
    /// Replaces all non-alphanumeric characters with underscores.
    /// </summary>
    private static string SanitizeId(string name)
        => new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    /// <summary>
    /// Sanitizes a label for use inside Mermaid quoted strings.
    /// Converts angle brackets to parentheses to prevent HTML/tag injection
    /// (e.g. List&lt;string&gt; becomes List(string)).
    /// </summary>
    private static string SanitizeLabel(string name)
        => name.Replace("<", "(")
               .Replace(">", ")")
               .Replace("\"", "'")
               .Replace("&", "and")
               .Replace("\n", " ")
               .Replace("\r", "");
}
