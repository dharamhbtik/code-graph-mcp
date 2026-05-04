using System.Xml.Linq;
using CodeGraphMcp.Core.Domain;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Parsing;

public sealed class ProjectFileParser(ILogger<ProjectFileParser> logger) : ILanguageParser
{
    public Language Language => Language.ProjectFile;

    public async Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var source = await File.ReadAllTextAsync(filePath, ct);
            var doc    = XDocument.Parse(source);
            var nodes  = new List<CodeNode>();
            var edges  = new List<CodeEdge>();
            var fileName = Path.GetFileName(filePath);

            var fileNode = new CodeNode
            {
                Id       = CodeNode.MakeId(filePath, filePath),
                Kind     = NodeKind.File,
                Name     = fileName,
                FullName = filePath,
                FilePath = filePath,
                Language = Language.ProjectFile,
                StartLine = 1,
                EndLine   = source.Split('\n').Length,
            };
            nodes.Add(fileNode);

            // Package references
            foreach (var pkg in doc.Descendants("PackageReference"))
            {
                var pkgName    = pkg.Attribute("Include")?.Value ?? string.Empty;
                var pkgVersion = pkg.Attribute("Version")?.Value;
                if (string.IsNullOrEmpty(pkgName)) continue;

                var pkgNode = new CodeNode
                {
                    Id       = CodeNode.MakeId(filePath, $"pkg::{pkgName}"),
                    Kind     = NodeKind.ConfigKey,
                    Name     = pkgName,
                    FullName = $"{pkgName}@{pkgVersion}",
                    FilePath = filePath,
                    Language = Language.ProjectFile,
                    Summary  = pkgVersion,
                };
                nodes.Add(pkgNode);
                edges.Add(new CodeEdge
                {
                    Id       = CodeEdge.MakeId(fileNode.Id, pkgNode.Id, RelationKind.DependsOn),
                    SourceId = fileNode.Id,
                    TargetId = pkgNode.Id,
                    Kind     = RelationKind.DependsOn,
                });
            }

            // Project references
            foreach (var proj in doc.Descendants("ProjectReference"))
            {
                var projPath = proj.Attribute("Include")?.Value ?? string.Empty;
                if (string.IsNullOrEmpty(projPath)) continue;

                var projId = CodeNode.MakeId(filePath, $"projref::{projPath}");
                edges.Add(new CodeEdge
                {
                    Id       = CodeEdge.MakeId(fileNode.Id, projId, RelationKind.DependsOn),
                    SourceId = fileNode.Id,
                    TargetId = projId,
                    Kind     = RelationKind.DependsOn,
                });
            }

            return new ParseResult(nodes, edges);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse project file {File}", filePath);
            return ParseResult.Empty;
        }
    }
}
