using CodeGraphMcp.Core.Domain;

namespace CodeGraphMcp.Core.Parsing;

public interface ILanguageParser
{
    Language Language { get; }
    Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default);
}

public sealed record ParseResult(
    IReadOnlyList<CodeNode> Nodes,
    IReadOnlyList<CodeEdge> Edges
)
{
    public static ParseResult Empty => new([], []);
}
