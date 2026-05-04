using CodeGraphMcp.Core.Domain;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Parsing;

public sealed class MarkdownParser(ILogger<MarkdownParser> logger) : ILanguageParser
{
    public Language Language => Language.Markdown;

    public async Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var source = await File.ReadAllTextAsync(filePath, ct);
            var lines  = source.Split('\n');
            var nodes  = new List<CodeNode>();
            var edges  = new List<CodeEdge>();
            var fileName = Path.GetFileName(filePath);

            var fileNode = new CodeNode
            {
                Id        = CodeNode.MakeId(filePath, filePath),
                Kind      = NodeKind.File,
                Name      = fileName,
                FullName  = filePath,
                FilePath  = filePath,
                Language  = Language.Markdown,
                StartLine = 1,
                EndLine   = lines.Length,
            };
            nodes.Add(fileNode);

            // Extract headings as DocumentSection nodes
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith('#'))
                {
                    var heading = line.TrimStart('#').Trim();
                    if (string.IsNullOrWhiteSpace(heading)) continue;

                    var sectionNode = new CodeNode
                    {
                        Id        = CodeNode.MakeId(filePath, $"{filePath}::{heading}"),
                        Kind      = NodeKind.DocumentSection,
                        Name      = heading,
                        FullName  = $"{fileName}::{heading}",
                        FilePath  = filePath,
                        Language  = Language.Markdown,
                        StartLine = i + 1,
                        EndLine   = i + 1,
                    };
                    nodes.Add(sectionNode);
                    edges.Add(new CodeEdge
                    {
                        Id       = CodeEdge.MakeId(fileNode.Id, sectionNode.Id, RelationKind.Contains),
                        SourceId = fileNode.Id,
                        TargetId = sectionNode.Id,
                        Kind     = RelationKind.Contains,
                    });
                }
            }

            return new ParseResult(nodes, edges);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse Markdown file {File}", filePath);
            return ParseResult.Empty;
        }
    }
}
