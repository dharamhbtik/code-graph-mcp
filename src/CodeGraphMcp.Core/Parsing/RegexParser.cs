using System.Text.RegularExpressions;
using CodeGraphMcp.Core.Domain;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Parsing;

/// <summary>
/// A unified regex-based parser that extracts nodes and edges from source files
/// for languages that don't have a dedicated Roslyn/Tree-sitter parser.
/// Supports: Java, Kotlin, Swift, C, C++, Objective-C, PHP, Go, Rust, Python, Ruby, Dart, SQL, HTML, CSS/SCSS, Shell, YAML.
/// </summary>
public sealed class RegexParser(ILogger<RegexParser> logger) : ILanguageParser
{
    // This parser handles multiple languages — the Language property
    // is set dynamically via the ParseForLanguage method.
    // The ILanguageParser.Language returns Unknown; use the typed parsers below.
    public Language Language => Language.Unknown;

    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => ParseForLanguageAsync(filePath, Utilities.LanguageDetector.Detect(filePath), ct);

    public async Task<ParseResult> ParseForLanguageAsync(string filePath, Language language, CancellationToken ct = default)
    {
        try
        {
            var source = await File.ReadAllTextAsync(filePath, ct);
            var lines = source.Split('\n');
            var nodes = new List<CodeNode>();
            var edges = new List<CodeEdge>();
            var fileName = Path.GetFileName(filePath);

            // File node
            var fileNode = new CodeNode
            {
                Id        = CodeNode.MakeId(filePath, filePath),
                Kind      = NodeKind.File,
                Name      = fileName,
                FullName  = filePath,
                FilePath  = filePath,
                Language  = language,
                StartLine = 1,
                EndLine   = lines.Length,
            };
            nodes.Add(fileNode);

            var patterns = GetPatterns(language);

            // Extract imports/includes
            foreach (var pattern in patterns.ImportPatterns)
            {
                foreach (Match m in Regex.Matches(source, pattern, RegexOptions.Multiline))
                {
                    var modulePath = m.Groups[1].Value;
                    var moduleId = CodeNode.MakeId(modulePath, modulePath);
                    edges.Add(new CodeEdge
                    {
                        Id       = CodeEdge.MakeId(fileNode.Id, moduleId, RelationKind.Imports),
                        SourceId = fileNode.Id,
                        TargetId = moduleId,
                        Kind     = RelationKind.Imports,
                    });
                }
            }

            // Extract namespaces/packages
            foreach (var pattern in patterns.NamespacePatterns)
            {
                foreach (Match m in Regex.Matches(source, pattern, RegexOptions.Multiline))
                {
                    var nsName = m.Groups[1].Value;
                    var nsNode = new CodeNode
                    {
                        Id        = CodeNode.MakeId(filePath, nsName),
                        Kind      = NodeKind.Namespace,
                        Name      = nsName,
                        FullName  = nsName,
                        FilePath  = filePath,
                        Language  = language,
                        StartLine = LineOf(source, m.Index),
                        EndLine   = LineOf(source, m.Index),
                    };
                    nodes.Add(nsNode);
                    edges.Add(MakeEdge(fileNode.Id, nsNode.Id, RelationKind.Contains));
                }
            }

            // Extract classes/types
            foreach (var (pattern, kind) in patterns.TypePatterns)
            {
                foreach (Match m in Regex.Matches(source, pattern, RegexOptions.Multiline))
                {
                    var name = m.Groups["name"].Value;
                    var fullName = $"{filePath}::{name}";
                    var line = LineOf(source, m.Index);
                    var typeNode = new CodeNode
                    {
                        Id        = CodeNode.MakeId(filePath, fullName),
                        Kind      = kind,
                        Name      = name,
                        FullName  = fullName,
                        FilePath  = filePath,
                        Language  = language,
                        StartLine = line,
                        EndLine   = line,
                    };
                    nodes.Add(typeNode);
                    edges.Add(MakeEdge(fileNode.Id, typeNode.Id, RelationKind.Contains));

                    // Inheritance if captured
                    if (m.Groups["base"].Success && !string.IsNullOrWhiteSpace(m.Groups["base"].Value))
                    {
                        var baseName = m.Groups["base"].Value.Trim();
                        // May be comma-separated
                        foreach (var b in baseName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            var cleanBase = b.Split('<')[0].Trim(); // Strip generics
                            if (string.IsNullOrEmpty(cleanBase)) continue;
                            var baseId = CodeNode.MakeId(filePath, cleanBase);
                            var relKind = cleanBase.StartsWith('I') && char.IsUpper(cleanBase.ElementAtOrDefault(1))
                                ? RelationKind.Implements
                                : RelationKind.Inherits;
                            edges.Add(MakeEdge(typeNode.Id, baseId, relKind));
                        }
                    }
                }
            }

            // Extract functions/methods
            foreach (var pattern in patterns.FunctionPatterns)
            {
                foreach (Match m in Regex.Matches(source, pattern, RegexOptions.Multiline))
                {
                    var name = m.Groups["name"].Value;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var fullName = $"{filePath}::{name}";
                    var line = LineOf(source, m.Index);
                    var funcNode = new CodeNode
                    {
                        Id        = CodeNode.MakeId(filePath, fullName),
                        Kind      = NodeKind.Function,
                        Name      = name,
                        FullName  = fullName,
                        FilePath  = filePath,
                        Language  = language,
                        StartLine = line,
                        EndLine   = line,
                    };
                    nodes.Add(funcNode);
                    edges.Add(MakeEdge(fileNode.Id, funcNode.Id, RelationKind.Contains));
                }
            }

            // Extract SQL-specific (tables, procedures)
            foreach (var (pattern, kind) in patterns.SqlPatterns)
            {
                foreach (Match m in Regex.Matches(source, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase))
                {
                    var name = m.Groups["name"].Value;
                    var fullName = $"{filePath}::{name}";
                    var line = LineOf(source, m.Index);
                    var sqlNode = new CodeNode
                    {
                        Id        = CodeNode.MakeId(filePath, fullName),
                        Kind      = kind,
                        Name      = name,
                        FullName  = fullName,
                        FilePath  = filePath,
                        Language  = language,
                        StartLine = line,
                        EndLine   = line,
                    };
                    nodes.Add(sqlNode);
                    edges.Add(MakeEdge(fileNode.Id, sqlNode.Id, RelationKind.Contains));
                }
            }

            return new ParseResult(nodes, edges);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse {Language} file {File}", language, filePath);
            return ParseResult.Empty;
        }
    }

    private static int LineOf(string source, int index)
        => source[..index].Split('\n').Length;

    private static CodeEdge MakeEdge(string src, string tgt, RelationKind kind) => new()
    {
        Id       = CodeEdge.MakeId(src, tgt, kind),
        SourceId = src,
        TargetId = tgt,
        Kind     = kind,
    };

    // ── Pattern definitions per language ──────────────────────────────────────

    private static LanguagePatterns GetPatterns(Language lang) => lang switch
    {
        Language.Java       => JavaPatterns,
        Language.Kotlin     => KotlinPatterns,
        Language.Swift      => SwiftPatterns,
        Language.C          => CPatterns,
        Language.Cpp        => CppPatterns,
        Language.ObjectiveC => ObjCPatterns,
        Language.Php        => PhpPatterns,
        Language.Go         => GoPatterns,
        Language.Rust       => RustPatterns,
        Language.Python     => PythonPatterns,
        Language.Ruby       => RubyPatterns,
        Language.Dart       => DartPatterns,
        Language.Sql        => SqlPatterns,
        Language.Html       => HtmlPatterns,
        Language.Css        => CssPatterns,
        Language.Scss       => CssPatterns,
        Language.Shell      => ShellPatterns,
        Language.Yaml       => YamlPatterns,
        _                   => EmptyPatterns,
    };

    private record LanguagePatterns(
        string[] ImportPatterns,
        string[] NamespacePatterns,
        (string Pattern, NodeKind Kind)[] TypePatterns,
        string[] FunctionPatterns,
        (string Pattern, NodeKind Kind)[] SqlPatterns
    );

    private static readonly LanguagePatterns EmptyPatterns = new([], [], [], [], []);

    // ── Java ──────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns JavaPatterns = new(
        ImportPatterns: [@"^import\s+(?:static\s+)?(.+?);", @"^import\s+(.+?)\s*;"],
        NamespacePatterns: [@"^package\s+([\w.]+)\s*;"],
        TypePatterns:
        [
            (@"(?:public|protected|private)?\s*(?:abstract|final)?\s*class\s+(?<name>\w+)(?:\s+extends\s+(?<base>\w+))?", NodeKind.Class),
            (@"(?:public|protected|private)?\s*interface\s+(?<name>\w+)(?:\s+extends\s+(?<base>[\w,\s]+))?", NodeKind.Interface),
            (@"(?:public|protected|private)?\s*enum\s+(?<name>\w+)", NodeKind.Enum),
            (@"(?:public|protected|private)?\s*record\s+(?<name>\w+)", NodeKind.Record),
        ],
        FunctionPatterns:
        [
            @"(?:public|protected|private)\s+(?:static\s+)?(?:[\w<>\[\],\s]+?)\s+(?<name>\w+)\s*\(",
        ],
        SqlPatterns: []
    );

    // ── Kotlin ────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns KotlinPatterns = new(
        ImportPatterns: [@"^import\s+([\w.]+)"],
        NamespacePatterns: [@"^package\s+([\w.]+)"],
        TypePatterns:
        [
            (@"(?:open|abstract|sealed|data|inner)?\s*class\s+(?<name>\w+)(?:\s*:\s*(?<base>[\w,\s()]+))?", NodeKind.Class),
            (@"(?:fun\s+)?interface\s+(?<name>\w+)", NodeKind.Interface),
            (@"enum\s+class\s+(?<name>\w+)", NodeKind.Enum),
            (@"object\s+(?<name>\w+)", NodeKind.Module),
        ],
        FunctionPatterns: [@"(?:override\s+)?(?:suspend\s+)?fun\s+(?<name>\w+)\s*[\(<]"],
        SqlPatterns: []
    );

    // ── Swift ─────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns SwiftPatterns = new(
        ImportPatterns: [@"^import\s+([\w.]+)"],
        NamespacePatterns: [],
        TypePatterns:
        [
            (@"(?:public|open|internal|private|fileprivate)?\s*(?:final\s+)?class\s+(?<name>\w+)(?:\s*:\s*(?<base>[\w,\s]+))?", NodeKind.Class),
            (@"(?:public|internal|private|fileprivate)?\s*struct\s+(?<name>\w+)(?:\s*:\s*(?<base>[\w,\s]+))?", NodeKind.Struct),
            (@"(?:public|internal|private|fileprivate)?\s*protocol\s+(?<name>\w+)", NodeKind.Interface),
            (@"(?:public|internal|private|fileprivate)?\s*enum\s+(?<name>\w+)", NodeKind.Enum),
        ],
        FunctionPatterns: [@"(?:public|open|internal|private|fileprivate|override|static|class)?\s*func\s+(?<name>\w+)\s*[\(<]"],
        SqlPatterns: []
    );

    // ── C ─────────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns CPatterns = new(
        ImportPatterns: [@"^#include\s+[""<](.+?)[>""]"],
        NamespacePatterns: [],
        TypePatterns:
        [
            (@"typedef\s+struct\s+(?<name>\w+)", NodeKind.Struct),
            (@"struct\s+(?<name>\w+)\s*\{", NodeKind.Struct),
            (@"enum\s+(?<name>\w+)\s*\{", NodeKind.Enum),
        ],
        FunctionPatterns: [@"^(?:static\s+)?(?:inline\s+)?(?:const\s+)?(?:unsigned\s+)?(?:\w+[\s*]+)+(?<name>\w+)\s*\([^;]*\)\s*\{"],
        SqlPatterns: []
    );

    // ── C++ ───────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns CppPatterns = new(
        ImportPatterns: [@"^#include\s+[""<](.+?)[>""]"],
        NamespacePatterns: [@"namespace\s+([\w:]+)\s*\{"],
        TypePatterns:
        [
            (@"class\s+(?<name>\w+)(?:\s*:\s*(?:public|protected|private)\s+(?<base>[\w:]+))?", NodeKind.Class),
            (@"struct\s+(?<name>\w+)\s*(?::\s*(?:public|protected|private)\s+(?<base>[\w:]+))?\s*\{", NodeKind.Struct),
            (@"enum\s+(?:class\s+)?(?<name>\w+)", NodeKind.Enum),
        ],
        FunctionPatterns:
        [
            @"(?:virtual\s+)?(?:static\s+)?(?:inline\s+)?(?:const\s+)?(?:\w+[\s*&]+)+(?<name>\w+)\s*\([^;]*\)\s*(?:const\s*)?\{",
        ],
        SqlPatterns: []
    );

    // ── Objective-C ───────────────────────────────────────────────────────────
    private static readonly LanguagePatterns ObjCPatterns = new(
        ImportPatterns: [@"^#import\s+[""<](.+?)[>""]", @"^#include\s+[""<](.+?)[>""]"],
        NamespacePatterns: [],
        TypePatterns:
        [
            (@"@interface\s+(?<name>\w+)\s*(?::\s*(?<base>\w+))?", NodeKind.Class),
            (@"@implementation\s+(?<name>\w+)", NodeKind.Class),
            (@"@protocol\s+(?<name>\w+)", NodeKind.Interface),
        ],
        FunctionPatterns: [@"^[-+]\s*\([^)]+\)\s*(?<name>\w+)"],
        SqlPatterns: []
    );

    // ── PHP ───────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns PhpPatterns = new(
        ImportPatterns: [@"^use\s+([\w\\]+)"],
        NamespacePatterns: [@"^namespace\s+([\w\\]+)\s*;"],
        TypePatterns:
        [
            (@"(?:abstract\s+)?class\s+(?<name>\w+)(?:\s+extends\s+(?<base>\w+))?", NodeKind.Class),
            (@"interface\s+(?<name>\w+)", NodeKind.Interface),
            (@"trait\s+(?<name>\w+)", NodeKind.Class),
            (@"enum\s+(?<name>\w+)", NodeKind.Enum),
        ],
        FunctionPatterns: [@"(?:public|protected|private|static)?\s*function\s+(?<name>\w+)\s*\("],
        SqlPatterns: []
    );

    // ── Go ────────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns GoPatterns = new(
        ImportPatterns: [@"""([\w./\-]+)"""],
        NamespacePatterns: [@"^package\s+(\w+)"],
        TypePatterns:
        [
            (@"type\s+(?<name>\w+)\s+struct\s*\{", NodeKind.Struct),
            (@"type\s+(?<name>\w+)\s+interface\s*\{", NodeKind.Interface),
        ],
        FunctionPatterns: [@"^func\s+(?:\(\w+\s+\*?\w+\)\s+)?(?<name>\w+)\s*\("],
        SqlPatterns: []
    );

    // ── Rust ──────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns RustPatterns = new(
        ImportPatterns: [@"^use\s+([\w:]+)"],
        NamespacePatterns: [@"^mod\s+(\w+)"],
        TypePatterns:
        [
            (@"(?:pub\s+)?struct\s+(?<name>\w+)", NodeKind.Struct),
            (@"(?:pub\s+)?enum\s+(?<name>\w+)", NodeKind.Enum),
            (@"(?:pub\s+)?trait\s+(?<name>\w+)", NodeKind.Interface),
        ],
        FunctionPatterns: [@"(?:pub\s+)?(?:async\s+)?fn\s+(?<name>\w+)\s*[\(<]"],
        SqlPatterns: []
    );

    // ── Python ────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns PythonPatterns = new(
        ImportPatterns: [@"^(?:from\s+([\w.]+)\s+)?import\s+([\w.]+)"],
        NamespacePatterns: [],
        TypePatterns:
        [
            (@"^class\s+(?<name>\w+)(?:\((?<base>[^)]+)\))?:", NodeKind.Class),
        ],
        FunctionPatterns: [@"^(?:\s*)(?:async\s+)?def\s+(?<name>\w+)\s*\("],
        SqlPatterns: []
    );

    // ── Ruby ──────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns RubyPatterns = new(
        ImportPatterns: [@"^require(?:_relative)?\s+['""](.+?)['""]"],
        NamespacePatterns: [@"^module\s+(\w+)"],
        TypePatterns:
        [
            (@"^class\s+(?<name>\w+)(?:\s*<\s*(?<base>\w+))?", NodeKind.Class),
        ],
        FunctionPatterns: [@"^\s*def\s+(?<name>\w+)"],
        SqlPatterns: []
    );

    // ── Dart ──────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns DartPatterns = new(
        ImportPatterns: [@"^import\s+'(.+?)'"],
        NamespacePatterns: [],
        TypePatterns:
        [
            (@"(?:abstract\s+)?class\s+(?<name>\w+)(?:\s+extends\s+(?<base>\w+))?", NodeKind.Class),
            (@"enum\s+(?<name>\w+)", NodeKind.Enum),
            (@"mixin\s+(?<name>\w+)", NodeKind.Class),
        ],
        FunctionPatterns: [@"(?:Future|void|int|String|bool|double|dynamic|[\w<>]+)\s+(?<name>\w+)\s*\("],
        SqlPatterns: []
    );

    // ── SQL ───────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns SqlPatterns = new(
        ImportPatterns: [],
        NamespacePatterns: [],
        TypePatterns: [],
        FunctionPatterns: [],
        SqlPatterns:
        [
            (@"CREATE\s+(?:OR\s+REPLACE\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:\w+\.)?(?<name>\w+)", NodeKind.SqlTable),
            (@"CREATE\s+(?:OR\s+REPLACE\s+)?(?:PROCEDURE|PROC)\s+(?:\w+\.)?(?<name>\w+)", NodeKind.SqlProcedure),
            (@"CREATE\s+(?:OR\s+REPLACE\s+)?VIEW\s+(?:\w+\.)?(?<name>\w+)", NodeKind.SqlTable),
            (@"CREATE\s+(?:OR\s+REPLACE\s+)?FUNCTION\s+(?:\w+\.)?(?<name>\w+)", NodeKind.SqlProcedure),
        ]
    );

    // ── HTML ──────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns HtmlPatterns = new(
        ImportPatterns: [@"<script\s+src=""(.+?)""", @"<link\s+[^>]*href=""(.+?)"""],
        NamespacePatterns: [],
        TypePatterns: [],
        FunctionPatterns: [],
        SqlPatterns: []
    );

    // ── CSS/SCSS ──────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns CssPatterns = new(
        ImportPatterns: [@"@import\s+['""](.+?)['""]", @"@import\s+url\(['""](.+?)['""]\)"],
        NamespacePatterns: [],
        TypePatterns: [],
        FunctionPatterns: [],
        SqlPatterns: []
    );

    // ── Shell ─────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns ShellPatterns = new(
        ImportPatterns: [@"^(?:source|\.)?\s+(.+\.sh)"],
        NamespacePatterns: [],
        TypePatterns: [],
        FunctionPatterns: [@"^(?:function\s+)?(?<name>\w+)\s*\(\)\s*\{"],
        SqlPatterns: []
    );

    // ── YAML ──────────────────────────────────────────────────────────────────
    private static readonly LanguagePatterns YamlPatterns = new(
        ImportPatterns: [],
        NamespacePatterns: [],
        TypePatterns:
        [
            (@"^(?<name>[\w-]+)\s*:", NodeKind.ConfigKey),
        ],
        FunctionPatterns: [],
        SqlPatterns: []
    );
}

// ── Typed wrappers for DI registration ────────────────────────────────────────

public sealed class JavaParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Java;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Java, ct);
}

public sealed class KotlinParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Kotlin;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Kotlin, ct);
}

public sealed class SwiftParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Swift;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Swift, ct);
}

public sealed class CParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.C;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.C, ct);
}

public sealed class CppParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Cpp;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Cpp, ct);
}

public sealed class ObjectiveCParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.ObjectiveC;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.ObjectiveC, ct);
}

public sealed class PhpParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Php;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Php, ct);
}

public sealed class GoParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Go;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Go, ct);
}

public sealed class RustParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Rust;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Rust, ct);
}

public sealed class PythonParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Python;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Python, ct);
}

public sealed class RubyParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Ruby;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Ruby, ct);
}

public sealed class DartParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Dart;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Dart, ct);
}

public sealed class SqlParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Sql;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Sql, ct);
}

public sealed class HtmlParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Html;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Html, ct);
}

public sealed class CssParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Css;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Css, ct);
}

public sealed class ScssParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Scss;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Scss, ct);
}

public sealed class ShellParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Shell;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Shell, ct);
}

public sealed class YamlParser(RegexParser inner) : ILanguageParser
{
    public Language Language => Language.Yaml;
    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => inner.ParseForLanguageAsync(filePath, Language.Yaml, ct);
}
