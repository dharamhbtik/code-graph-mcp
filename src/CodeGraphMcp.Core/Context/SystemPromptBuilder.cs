using System.Text;
using CodeGraphMcp.Core.Domain;
using CodeGraphMcp.Core.Utilities;

namespace CodeGraphMcp.Core.Context;

public sealed class SystemPromptBuilder(string rootPath)
{
    private const int MaxTokenBudget = 4000;

    public string Build(CodeGraph graph)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("## Repository context");
        sb.AppendLine($"- Root: `{rootPath}`");
        sb.AppendLine($"- Generated: {graph.GeneratedAt:u}");
        sb.AppendLine($"- Files: {graph.Nodes.Values.Count(n => n.Kind == NodeKind.File)}");
        sb.AppendLine($"- Symbols: {graph.Nodes.Values.Count(n => n.Kind is not NodeKind.File)}");
        sb.AppendLine($"- Relationships: {graph.Edges.Count}");

        var languages = graph.Nodes.Values
            .Select(n => n.Language.ToString())
            .Distinct()
            .Where(l => l != "Unknown")
            .OrderBy(l => l);
        sb.AppendLine($"- Languages: {string.Join(", ", languages)}");
        sb.AppendLine();

        // Top 8 entry points — include interfaces, structs, records alongside classes
        var edgeCounts = graph.Edges
            .GroupBy(e => e.SourceId)
            .ToDictionary(g => g.Key, g => g.Count());

        var incomingCounts = graph.Edges
            .GroupBy(e => e.TargetId)
            .ToDictionary(g => g.Key, g => g.Count());

        var topNodes = graph.Nodes.Values
            .Where(n => n.Kind is NodeKind.Class or NodeKind.Interface or NodeKind.Module
                                or NodeKind.Component or NodeKind.Struct or NodeKind.Record)
            .OrderByDescending(n =>
                edgeCounts.GetValueOrDefault(n.Id, 0) + incomingCounts.GetValueOrDefault(n.Id, 0))
            .Take(8)
            .ToList();

        if (topNodes.Count > 0)
        {
            sb.AppendLine("## Key entry points (most connected)");
            foreach (var n in topNodes)
            {
                var outCount = edgeCounts.GetValueOrDefault(n.Id, 0);
                var inCount  = incomingCounts.GetValueOrDefault(n.Id, 0);
                sb.AppendLine($"- `{n.FullName}` ({n.Kind}) → {Path.GetRelativePath(rootPath, n.FilePath)}:{n.StartLine} [{outCount} out, {inCount} in]");
            }
            sb.AppendLine();
        }

        // Key dependency chains — show the most important inheritance/implementation edges
        var structuralEdges = graph.Edges
            .Where(e => e.Kind is RelationKind.Inherits or RelationKind.Implements or RelationKind.DependsOn)
            .ToList();

        if (structuralEdges.Count > 0)
        {
            sb.AppendLine("## Key dependencies");
            var shown = 0;
            foreach (var edge in structuralEdges)
            {
                if (shown >= 15) break; // Cap to save tokens
                if (graph.Nodes.TryGetValue(edge.SourceId, out var src) &&
                    graph.Nodes.TryGetValue(edge.TargetId, out var tgt))
                {
                    sb.AppendLine($"- `{src.Name}` {edge.Kind.ToString().ToLower()} `{tgt.Name}`");
                    shown++;
                }
            }
            sb.AppendLine();
        }

        // File map
        sb.AppendLine("## File map");
        var fileNodes = graph.Nodes.Values
            .Where(n => n.Kind == NodeKind.File)
            .OrderBy(n => n.FilePath)
            .Take(100);

        foreach (var file in fileNodes)
        {
            var rel       = Path.GetRelativePath(rootPath, file.FilePath);
            var typeCount = graph.Nodes.Values.Count(n =>
                n.FilePath == file.FilePath &&
                n.Kind is NodeKind.Class or NodeKind.Interface or NodeKind.Function
                       or NodeKind.Component or NodeKind.Struct or NodeKind.Record);
            sb.AppendLine($"- `{rel}` | {file.Language} | {typeCount} symbol(s)");
        }
        sb.AppendLine();

        // Symbol index — public types only, capped at budget
        sb.AppendLine("## Symbol index");
        var symbols = graph.Nodes.Values
            .Where(n => n.Kind is NodeKind.Class or NodeKind.Interface or
                        NodeKind.Function or NodeKind.Component or NodeKind.Injectable
                        or NodeKind.Struct or NodeKind.Record or NodeKind.Enum)
            .OrderBy(n => n.FullName);

        foreach (var sym in symbols)
        {
            var line = $"- `{sym.FullName}` ({sym.Kind}) → `{Path.GetRelativePath(rootPath, sym.FilePath)}:{sym.StartLine}`\n";
            if (TokenEstimator.Estimate(sb.Length + line.Length) > MaxTokenBudget) break;
            sb.Append(line);
        }

        return sb.ToString();
    }
}
