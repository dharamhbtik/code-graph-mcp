using System.Xml.Linq;
using CodeGraphMcp.Core.Domain;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Parsing;

public sealed class XamlParser(ILogger<XamlParser> logger) : ILanguageParser
{
    public Language Language => Language.Xaml;

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
                Language = Language.Xaml,
                StartLine = 1,
                EndLine   = source.Split('\n').Length,
            };
            nodes.Add(fileNode);

            // Support both WPF/Silverlight (2006) and MAUI/UWP (2009) x: namespace
            var xNs2006 = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var xNs2009 = XNamespace.Get("http://schemas.microsoft.com/winfx/2009/xaml");
            var xClass = doc.Root?.Attribute(xNs2006 + "Class")?.Value
                      ?? doc.Root?.Attribute(xNs2009 + "Class")?.Value;
            if (xClass is not null)
            {
                var viewNode = new CodeNode
                {
                    Id       = CodeNode.MakeId(filePath, xClass),
                    Kind     = NodeKind.XamlView,
                    Name     = xClass.Split('.').Last(),
                    FullName = xClass,
                    FilePath = filePath,
                    Language = Language.Xaml,
                    StartLine = 1,
                    EndLine   = source.Split('\n').Length,
                };
                nodes.Add(viewNode);
                edges.Add(new CodeEdge
                {
                    Id       = CodeEdge.MakeId(fileNode.Id, viewNode.Id, RelationKind.Contains),
                    SourceId = fileNode.Id,
                    TargetId = viewNode.Id,
                    Kind     = RelationKind.Contains,
                });

                // Convention: OrderView → OrderViewModel
                var vmName = xClass.Replace("View", "ViewModel");
                var vmId   = CodeNode.MakeId(string.Empty, vmName);
                edges.Add(new CodeEdge
                {
                    Id       = CodeEdge.MakeId(viewNode.Id, vmId, RelationKind.Binds),
                    SourceId = viewNode.Id,
                    TargetId = vmId,
                    Kind     = RelationKind.Binds,
                });
            }

            // x:Name attributes — named elements (support both namespace versions)
            foreach (var el in doc.Descendants().Where(e => 
                e.Attribute(xNs2006 + "Name") != null || e.Attribute(xNs2009 + "Name") != null))
            {
                var xName = (el.Attribute(xNs2006 + "Name") ?? el.Attribute(xNs2009 + "Name"))!.Value;
                var res = new CodeNode
                {
                    Id       = CodeNode.MakeId(filePath, $"{filePath}::{xName}"),
                    Kind     = NodeKind.XamlResource,
                    Name     = xName,
                    FullName = $"{fileName}::{xName}",
                    FilePath = filePath,
                    Language = Language.Xaml,
                };
                nodes.Add(res);
                edges.Add(new CodeEdge
                {
                    Id       = CodeEdge.MakeId(fileNode.Id, res.Id, RelationKind.Contains),
                    SourceId = fileNode.Id,
                    TargetId = res.Id,
                    Kind     = RelationKind.Contains,
                });
            }

            return new ParseResult(nodes, edges);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse XAML file {File}", filePath);
            return ParseResult.Empty;
        }
    }
}
