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

        var languages = graph.Nodes.Values
            .Select(n => n.Language.ToString())
            .Distinct()
            .Where(l => l != "Unknown")
            .OrderBy(l => l);
        sb.AppendLine($"- Languages: {string.Join(", ", languages)}");
        sb.AppendLine();

        // Top 5 entry points (nodes with most outgoing edges)
        var edgeCounts = graph.Edges
            .GroupBy(e => e.SourceId)
            .ToDictionary(g => g.Key, g => g.Count());

        var topNodes = graph.Nodes.Values
            .Where(n => n.Kind is NodeKind.Class or NodeKind.Module or NodeKind.Component)
            .OrderByDescending(n => edgeCounts.GetValueOrDefault(n.Id, 0))
            .Take(5)
            .ToList();

        if (topNodes.Count > 0)
        {
            sb.AppendLine("## Key entry points");
            foreach (var n in topNodes)
                sb.AppendLine($"- `{n.FullName}` ({n.Language}) → {Path.GetRelativePath(rootPath, n.FilePath)}:{n.StartLine}");
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
                n.Kind is NodeKind.Class or NodeKind.Interface or NodeKind.Function or NodeKind.Component);
            sb.AppendLine($"- `{rel}` | {file.Language} | {typeCount} symbol(s)");
        }
        sb.AppendLine();

        // Symbol index — public types only, capped at budget
        sb.AppendLine("## Symbol index");
        var symbols = graph.Nodes.Values
            .Where(n => n.Kind is NodeKind.Class or NodeKind.Interface or
                        NodeKind.Function or NodeKind.Component or NodeKind.Injectable)
            .OrderBy(n => n.FullName);

        foreach (var sym in symbols)
        {
            var line = $"- `{sym.FullName}` → `{Path.GetRelativePath(rootPath, sym.FilePath)}:{sym.StartLine}`\n";
            if (TokenEstimator.Estimate(sb.Length + line.Length) > MaxTokenBudget) break;
            sb.Append(line);
        }

        return sb.ToString();
    }
}
