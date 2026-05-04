using CodeGraphMcp.Core.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Parsing;

public sealed class CSharpParser(ILogger<CSharpParser> logger) : ILanguageParser
{
    public Language Language => Language.CSharp;

    public async Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var source = await File.ReadAllTextAsync(filePath, ct);
            var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: ct);
            var root = await tree.GetRootAsync(ct);

            var nodes = new List<CodeNode>();
            var edges = new List<CodeEdge>();

            // File node
            var fileNode = MakeNode(filePath, NodeKind.File, Path.GetFileName(filePath),
                filePath, Language.CSharp, 1, source.Split('\n').Length);
            nodes.Add(fileNode);

            // Namespaces
            foreach (var ns in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                var nsName = ns.Name.ToString();
                var nsNode = MakeNode(filePath, NodeKind.Namespace, nsName, nsName,
                    Language.CSharp, GetLine(ns.SpanStart, root), GetLine(ns.Span.End, root));
                nodes.Add(nsNode);
                edges.Add(MakeEdge(fileNode.Id, nsNode.Id, RelationKind.Contains));
            }

            // Types (class, interface, enum, struct, record)
            foreach (var type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var kind = type switch
                {
                    ClassDeclarationSyntax  => NodeKind.Class,
                    InterfaceDeclarationSyntax => NodeKind.Interface,
                    EnumDeclarationSyntax   => NodeKind.Enum,
                    StructDeclarationSyntax => NodeKind.Struct,
                    RecordDeclarationSyntax => NodeKind.Record,
                    _                       => NodeKind.Class,
                };
                var fullName = GetFullName(type);
                var typeNode = MakeNode(filePath, kind, type.Identifier.Text, fullName,
                    Language.CSharp, GetLine(type.SpanStart, root), GetLine(type.Span.End, root));
                nodes.Add(typeNode);
                edges.Add(MakeEdge(fileNode.Id, typeNode.Id, RelationKind.Contains));

                // Inheritance
                if (type.BaseList is not null)
                {
                    foreach (var baseType in type.BaseList.Types)
                    {
                        var baseName = baseType.Type.ToString();
                        var baseId = CodeNode.MakeId(filePath, baseName);
                        var rel = baseName.StartsWith('I') ? RelationKind.Implements : RelationKind.Inherits;
                        edges.Add(MakeEdge(typeNode.Id, baseId, rel));
                    }
                }

                // Methods and Properties (only available on TypeDeclarationSyntax, not EnumDeclarationSyntax)
                if (type is TypeDeclarationSyntax typeDecl)
                {
                    foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
                    {
                        var sig = $"{fullName}.{method.Identifier.Text}({string.Join(",", method.ParameterList.Parameters.Select(p => p.Type?.ToString()))})";
                        var methodNode = MakeNode(filePath, NodeKind.Method, method.Identifier.Text, sig,
                            Language.CSharp, GetLine(method.SpanStart, root), GetLine(method.Span.End, root));
                        nodes.Add(methodNode);
                        edges.Add(MakeEdge(typeNode.Id, methodNode.Id, RelationKind.Contains));
                    }

                    foreach (var prop in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
                    {
                        var propFull = $"{fullName}.{prop.Identifier.Text}";
                        var propNode = MakeNode(filePath, NodeKind.Property, prop.Identifier.Text, propFull,
                            Language.CSharp, GetLine(prop.SpanStart, root), GetLine(prop.Span.End, root));
                        nodes.Add(propNode);
                        edges.Add(MakeEdge(typeNode.Id, propNode.Id, RelationKind.Contains));
                    }
                }
            }

            return new ParseResult(nodes, edges);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse C# file {File}", filePath);
            return ParseResult.Empty;
        }
    }

    private static CodeNode MakeNode(string filePath, NodeKind kind, string name,
        string fullName, Language lang, int start, int end) => new()
    {
        Id       = CodeNode.MakeId(filePath, fullName),
        Kind     = kind,
        Name     = name,
        FullName = fullName,
        FilePath = filePath,
        Language = lang,
        StartLine = start,
        EndLine   = end,
    };

    private static CodeEdge MakeEdge(string src, string tgt, RelationKind kind) => new()
    {
        Id       = CodeEdge.MakeId(src, tgt, kind),
        SourceId = src,
        TargetId = tgt,
        Kind     = kind,
    };

    private static string GetFullName(BaseTypeDeclarationSyntax type)
    {
        var parts = new List<string> { type.Identifier.Text };
        var parent = type.Parent;
        while (parent is BaseNamespaceDeclarationSyntax ns)
        {
            parts.Insert(0, ns.Name.ToString());
            parent = ns.Parent;
        }
        return string.Join(".", parts);
    }

    private static int GetLine(int position, SyntaxNode root)
    {
        var lineSpan = root.SyntaxTree.GetLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(position, 0));
        return lineSpan.StartLinePosition.Line + 1;
    }
}
