# CodeGraphMcp — Production Build Specification

> **How to use this document**
> Read this file top to bottom. Each phase is a self-contained task group. Complete every task in a phase before moving to the next. Every task has acceptance criteria — do not proceed until they pass. Code snippets are the authoritative implementation; do not deviate unless a later task explicitly revises them.

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Repository Structure](#2-repository-structure)
3. [Phase 1 — Solution Scaffold](#3-phase-1--solution-scaffold)
4. [Phase 2 — Shared Domain Model](#4-phase-2--shared-domain-model)
5. [Phase 3 — Repository Scanner](#5-phase-3--repository-scanner)
6. [Phase 4 — Language Parsers](#6-phase-4--language-parsers)
7. [Phase 5 — Graph Store (SQLite)](#7-phase-5--graph-store-sqlite)
8. [Phase 6 — Graph Orchestrator](#8-phase-6--graph-orchestrator)
9. [Phase 7 — FileSystemWatcher & Incremental Updates](#9-phase-7--filesystemwatcher--incremental-updates)
10. [Phase 8 — MCP Server & Tool Endpoints](#10-phase-8--mcp-server--tool-endpoints)
11. [Phase 9 — System Prompt Builder](#11-phase-9--system-prompt-builder)
12. [Phase 10 — Configuration & Startup](#12-phase-10--configuration--startup)
13. [Phase 11 — Integration Tests](#13-phase-11--integration-tests)
14. [Phase 12 — Production Hardening](#14-phase-12--production-hardening)
15. [Phase 13 — MCP Client Configuration](#15-phase-13--mcp-client-configuration)
16. [Appendix A — NuGet Package Reference](#appendix-a--nuget-package-reference)
17. [Appendix B — Acceptance Checklist](#appendix-b--acceptance-checklist)

---

## 1. Project Overview

### What this system does

`CodeGraphMcp` is a local MCP (Model Context Protocol) server written in .NET 10. It:

- Scans any code repository (C#, XAML, JavaScript, TypeScript, Angular, JSON, SQL, Markdown)
- Builds a **code graph** of nodes (files, classes, methods, functions) and edges (calls, imports, inherits, references)
- Stores the graph in a local SQLite database
- Exposes the graph to AI agents (Claude, Cursor, Copilot) via MCP tool endpoints
- **Automatically detects file changes** and incrementally patches the graph without a full rebuild
- Generates a compact system prompt context block so agents understand the repository with minimal token spend

### Design constraints

| Constraint | Value |
|---|---|
| Runtime | .NET 10 |
| Transport | stdio (MCP standard) |
| Graph storage | SQLite via `Microsoft.Data.Sqlite` |
| C# parsing | Roslyn (`Microsoft.CodeAnalysis.CSharp`) |
| JS/TS parsing | Tree-sitter via Node.js child process |
| Max graph context tokens | 80,000 (configurable) |
| Debounce window | 800 ms |
| Max parser concurrency | 8 files in parallel |

---

## 2. Repository Structure

Create this exact folder structure. All paths below are relative to the solution root.

```
CodeGraphMcp/
├── CodeGraphMcp.sln
├── src/
│   ├── CodeGraphMcp/                    # Main executable project
│   │   ├── CodeGraphMcp.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Production.json
│   │   ├── Mcp/
│   │   │   ├── McpServer.cs
│   │   │   └── Tools/
│   │   │       ├── GetCodeGraphTool.cs
│   │   │       ├── GetFileContextTool.cs
│   │   │       ├── GetSymbolTool.cs
│   │   │       └── GetSystemPromptTool.cs
│   │   ├── Watcher/
│   │   │   ├── FileChangeWatcher.cs
│   │   │   └── FileChangeEvent.cs
│   │   └── Startup/
│   │       └── ServiceRegistration.cs
│   ├── CodeGraphMcp.Core/               # Domain + graph logic
│   │   ├── CodeGraphMcp.Core.csproj
│   │   ├── Domain/
│   │   │   ├── CodeNode.cs
│   │   │   ├── CodeEdge.cs
│   │   │   ├── CodeGraph.cs
│   │   │   ├── NodeKind.cs
│   │   │   ├── RelationKind.cs
│   │   │   └── Language.cs
│   │   ├── Scanning/
│   │   │   ├── RepositoryScanner.cs
│   │   │   └── SourceFile.cs
│   │   ├── Parsing/
│   │   │   ├── ILanguageParser.cs
│   │   │   ├── CSharpParser.cs
│   │   │   ├── XamlParser.cs
│   │   │   ├── JavaScriptParser.cs
│   │   │   ├── ProjectFileParser.cs
│   │   │   ├── MarkdownParser.cs
│   │   │   └── NodeProcessRunner.cs
│   │   ├── Graph/
│   │   │   ├── GraphStore.cs
│   │   │   └── GraphStoreSchema.cs
│   │   ├── Orchestration/
│   │   │   └── GraphOrchestrator.cs
│   │   ├── Context/
│   │   │   └── SystemPromptBuilder.cs
│   │   └── Utilities/
│   │       ├── LanguageDetector.cs
│   │       └── TokenEstimator.cs
│   └── CodeGraphMcp.Tests/              # Integration tests
│       ├── CodeGraphMcp.Tests.csproj
│       ├── Fixtures/
│       │   └── SampleRepo/              # Tiny multi-language test repo
│       │       ├── src/
│       │       │   ├── OrderService.cs
│       │       │   ├── IOrderRepository.cs
│       │       │   └── OrderViewModel.cs
│       │       ├── ui/
│       │       │   ├── OrderView.xaml
│       │       │   └── app.component.ts
│       │       └── config/
│       │           └── appsettings.json
│       ├── OrchestratorTests.cs
│       ├── WatcherTests.cs
│       └── McpToolTests.cs
└── scripts/
    └── parse-js.mjs                     # Node.js tree-sitter parser script
```

---

## 3. Phase 1 — Solution Scaffold

### Task 1.1 — Create solution and projects

```bash
dotnet new sln -n CodeGraphMcp
dotnet new console -n CodeGraphMcp -o src/CodeGraphMcp --framework net10.0
dotnet new classlib -n CodeGraphMcp.Core -o src/CodeGraphMcp.Core --framework net10.0
dotnet new xunit -n CodeGraphMcp.Tests -o src/CodeGraphMcp.Tests --framework net10.0

dotnet sln add src/CodeGraphMcp/CodeGraphMcp.csproj
dotnet sln add src/CodeGraphMcp.Core/CodeGraphMcp.Core.csproj
dotnet sln add src/CodeGraphMcp.Tests/CodeGraphMcp.Tests.csproj

dotnet add src/CodeGraphMcp/CodeGraphMcp.csproj reference src/CodeGraphMcp.Core/CodeGraphMcp.Core.csproj
dotnet add src/CodeGraphMcp.Tests/CodeGraphMcp.Tests.csproj reference src/CodeGraphMcp.Core/CodeGraphMcp.Core.csproj
dotnet add src/CodeGraphMcp.Tests/CodeGraphMcp.Tests.csproj reference src/CodeGraphMcp/CodeGraphMcp.csproj
```

### Task 1.2 — Add NuGet packages

Add to `src/CodeGraphMcp/CodeGraphMcp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>CodeGraphMcp</AssemblyName>
    <RootNamespace>CodeGraphMcp</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="0.2.0-preview" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\CodeGraphMcp.Core\CodeGraphMcp.Core.csproj" />
  </ItemGroup>
</Project>
```

Add to `src/CodeGraphMcp.Core/CodeGraphMcp.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
  </ItemGroup>
</Project>
```

Add to `src/CodeGraphMcp.Tests/CodeGraphMcp.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="FluentAssertions" Version="6.12.2" />
  </ItemGroup>
</Project>
```

Run `dotnet restore` and confirm it succeeds with no errors.

**Acceptance criteria:** `dotnet build CodeGraphMcp.sln` exits with code 0 and zero errors.

---

## 4. Phase 2 — Shared Domain Model

Create all files in `src/CodeGraphMcp.Core/Domain/`.

### Task 2.1 — Enumerations

**`Language.cs`**
```csharp
namespace CodeGraphMcp.Core.Domain;

public enum Language
{
    Unknown,
    CSharp,
    Xaml,
    JavaScript,
    TypeScript,
    Angular,
    Json,
    Sql,
    Markdown,
    ProjectFile,
}
```

**`NodeKind.cs`**
```csharp
namespace CodeGraphMcp.Core.Domain;

public enum NodeKind
{
    File,
    Namespace,
    Class,
    Interface,
    Enum,
    Struct,
    Record,
    Method,
    Property,
    Field,
    Function,
    Module,
    Component,      // Angular @Component
    Injectable,     // Angular @Injectable
    NgModule,       // Angular @NgModule
    XamlView,
    XamlResource,
    ConfigKey,
    SqlTable,
    SqlProcedure,
    DocumentSection,
}
```

**`RelationKind.cs`**
```csharp
namespace CodeGraphMcp.Core.Domain;

public enum RelationKind
{
    Contains,
    Calls,
    Inherits,
    Implements,
    References,
    Imports,
    Binds,          // XAML x:Class → ViewModel
    DependsOn,      // project → project reference
    Declares,
}
```

### Task 2.2 — Core records

**`CodeNode.cs`**
```csharp
namespace CodeGraphMcp.Core.Domain;

public sealed record CodeNode
{
    public required string Id { get; init; }              // SHA256(FilePath + FullName)
    public required NodeKind Kind { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public required string FilePath { get; init; }
    public required Language Language { get; init; }
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public string? Summary { get; init; }                 // AI-generated or docstring; nullable

    public static string MakeId(string filePath, string fullName)
    {
        var raw = $"{filePath}::{fullName}";
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
```

**`CodeEdge.cs`**
```csharp
namespace CodeGraphMcp.Core.Domain;

public sealed record CodeEdge
{
    public required string Id { get; init; }              // SHA256(SourceId + TargetId + Kind)
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public required RelationKind Kind { get; init; }
    public float Weight { get; init; } = 1.0f;

    public static string MakeId(string sourceId, string targetId, RelationKind kind)
    {
        var raw = $"{sourceId}→{targetId}:{kind}";
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
```

**`CodeGraph.cs`**
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeGraphMcp.Core.Domain;

public sealed class CodeGraph
{
    public Dictionary<string, CodeNode> Nodes { get; } = new();
    public List<CodeEdge> Edges { get; } = new();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public string RootPath { get; set; } = string.Empty;
    public int TotalFilesScanned { get; set; }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToCompactJson() => JsonSerializer.Serialize(this, _jsonOptions);

    public int EstimateTokenCount()
    {
        // Rough heuristic: 4 chars ≈ 1 token
        var json = ToCompactJson();
        return json.Length / 4;
    }
}
```

**Acceptance criteria:** `dotnet build src/CodeGraphMcp.Core` succeeds. All types are in the correct namespace.

---

## 5. Phase 3 — Repository Scanner

### Task 3.1 — SourceFile record

**`src/CodeGraphMcp.Core/Scanning/SourceFile.cs`**
```csharp
using CodeGraphMcp.Core.Domain;

namespace CodeGraphMcp.Core.Scanning;

public sealed record SourceFile(
    string FilePath,
    string RelativePath,
    Language Language,
    string ContentHash,
    DateTimeOffset LastModified
);
```

### Task 3.2 — LanguageDetector utility

**`src/CodeGraphMcp.Core/Utilities/LanguageDetector.cs`**
```csharp
namespace CodeGraphMcp.Core.Utilities;

using CodeGraphMcp.Core.Domain;

public static class LanguageDetector
{
    private static readonly Dictionary<string, Language> _map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"]     = Language.CSharp,
        [".xaml"]   = Language.Xaml,
        [".js"]     = Language.JavaScript,
        [".mjs"]    = Language.JavaScript,
        [".cjs"]    = Language.JavaScript,
        [".ts"]     = Language.TypeScript,
        [".tsx"]    = Language.TypeScript,
        [".json"]   = Language.Json,
        [".sql"]    = Language.Sql,
        [".md"]     = Language.Markdown,
        [".csproj"] = Language.ProjectFile,
        [".sln"]    = Language.ProjectFile,
    };

    public static Language Detect(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (_map.TryGetValue(ext, out var lang)) return lang;

        // Angular-specific: check for .component.ts, .module.ts, .service.ts
        var name = Path.GetFileName(filePath);
        if (name.EndsWith(".component.ts", StringComparison.OrdinalIgnoreCase)) return Language.Angular;
        if (name.EndsWith(".module.ts", StringComparison.OrdinalIgnoreCase)) return Language.Angular;
        if (name.EndsWith(".service.ts", StringComparison.OrdinalIgnoreCase)) return Language.Angular;

        return Language.Unknown;
    }

    public static bool IsTracked(string filePath)
        => Detect(filePath) != Language.Unknown;
}
```

### Task 3.3 — RepositoryScanner

**`src/CodeGraphMcp.Core/Scanning/RepositoryScanner.cs`**
```csharp
using System.Security.Cryptography;
using CodeGraphMcp.Core.Domain;
using CodeGraphMcp.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Scanning;

public sealed class RepositoryScanner(ILogger<RepositoryScanner> logger)
{
    private static readonly HashSet<string> _ignoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".idea",
        "dist", "out", "coverage", "TestResults",
    };

    public async Task<IReadOnlyList<SourceFile>> ScanAsync(
        string rootPath,
        CancellationToken ct = default)
    {
        logger.LogInformation("Scanning repository at {Root}", rootPath);

        var allFiles = EnumerateTrackedFiles(rootPath);
        var semaphore = new SemaphoreSlim(8);
        var results = new System.Collections.Concurrent.ConcurrentBag<SourceFile>();

        await Parallel.ForEachAsync(allFiles, ct, async (filePath, innerCt) =>
        {
            await semaphore.WaitAsync(innerCt);
            try
            {
                var hash = await ComputeHashAsync(filePath, innerCt);
                var lang = LanguageDetector.Detect(filePath);
                var rel  = Path.GetRelativePath(rootPath, filePath);
                var modified = File.GetLastWriteTimeUtc(filePath);
                results.Add(new SourceFile(filePath, rel, lang, hash, modified));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read {File}", filePath);
            }
            finally
            {
                semaphore.Release();
            }
        });

        logger.LogInformation("Discovered {Count} tracked files", results.Count);
        return results.ToList();
    }

    private static IEnumerable<string> EnumerateTrackedFiles(string root)
    {
        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !IsInIgnoredDir(f, root))
            .Where(f => LanguageDetector.IsTracked(f));
    }

    private static bool IsInIgnoredDir(string filePath, string root)
    {
        var relative = Path.GetRelativePath(root, filePath);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => _ignoredDirs.Contains(p));
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var bytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
```

**Acceptance criteria:** Unit test `RepositoryScannerTests` (write in Phase 11) must pass: finds `.cs` files, skips `bin/obj/node_modules`, computes stable hashes.

---

## 6. Phase 4 — Language Parsers

### Task 4.1 — Parser interface

**`src/CodeGraphMcp.Core/Parsing/ILanguageParser.cs`**
```csharp
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
```

### Task 4.2 — C# parser (Roslyn)

**`src/CodeGraphMcp.Core/Parsing/CSharpParser.cs`**
```csharp
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

                // Methods
                foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
                {
                    var sig = $"{fullName}.{method.Identifier.Text}({string.Join(",", method.ParameterList.Parameters.Select(p => p.Type?.ToString()))})";
                    var methodNode = MakeNode(filePath, NodeKind.Method, method.Identifier.Text, sig,
                        Language.CSharp, GetLine(method.SpanStart, root), GetLine(method.Span.End, root));
                    nodes.Add(methodNode);
                    edges.Add(MakeEdge(typeNode.Id, methodNode.Id, RelationKind.Contains));
                }

                // Properties
                foreach (var prop in type.Members.OfType<PropertyDeclarationSyntax>())
                {
                    var propFull = $"{fullName}.{prop.Identifier.Text}";
                    var propNode = MakeNode(filePath, NodeKind.Property, prop.Identifier.Text, propFull,
                        Language.CSharp, GetLine(prop.SpanStart, root), GetLine(prop.Span.End, root));
                    nodes.Add(propNode);
                    edges.Add(MakeEdge(typeNode.Id, propNode.Id, RelationKind.Contains));
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
```

### Task 4.3 — XAML parser

**`src/CodeGraphMcp.Core/Parsing/XamlParser.cs`**
```csharp
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

            // x:Class → creates a XamlView node and edges to the ViewModel by naming convention
            var xNs   = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var xClass = doc.Root?.Attribute(xNs + "Class")?.Value;
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

            // x:Name attributes — named elements
            foreach (var el in doc.Descendants().Where(e => e.Attribute(xNs + "Name") != null))
            {
                var xName = el.Attribute(xNs + "Name")!.Value;
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
```

### Task 4.4 — Node.js tree-sitter subprocess runner

**`scripts/parse-js.mjs`** — place at the repository root, not inside any .NET project:

```javascript
// scripts/parse-js.mjs
// Called by NodeProcessRunner via stdin/stdout JSON protocol
// Requires: npm install -g tree-sitter tree-sitter-javascript tree-sitter-typescript

import { readFileSync } from "fs";

let inputBuf = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", (d) => { inputBuf += d; });
process.stdin.on("end", () => {
  try {
    const { filePath } = JSON.parse(inputBuf);
    const source = readFileSync(filePath, "utf8");
    const lines  = source.split("\n");

    // Lightweight regex-based extraction (no native tree-sitter binding required)
    const nodes = [];
    const edges = [];
    const fileId = hashId(filePath + "::" + filePath);
    nodes.push({ id: fileId, kind: "File", name: filePath.split("/").at(-1),
                 fullName: filePath, filePath, language: "JavaScript",
                 startLine: 1, endLine: lines.length });

    // Functions
    const fnRe = /^(?:export\s+)?(?:async\s+)?function\s+(\w+)/gm;
    let m;
    while ((m = fnRe.exec(source)) !== null) {
      const name = m[1];
      const line = lineOf(source, m.index);
      const id   = hashId(filePath + "::" + name);
      nodes.push({ id, kind: "Function", name, fullName: `${filePath}::${name}`,
                   filePath, language: "JavaScript", startLine: line, endLine: line });
      edges.push({ id: hashId(fileId + id + "Contains"),
                   sourceId: fileId, targetId: id, kind: "Contains", weight: 1 });
    }

    // Classes
    const classRe = /^(?:export\s+)?class\s+(\w+)(?:\s+extends\s+(\w+))?/gm;
    while ((m = classRe.exec(source)) !== null) {
      const name = m[1];
      const base = m[2];
      const line = lineOf(source, m.index);
      const id   = hashId(filePath + "::" + name);
      const kind = detectAngularKind(source, m.index) ?? "Class";
      nodes.push({ id, kind, name, fullName: `${filePath}::${name}`,
                   filePath, language: kind === "Class" ? "JavaScript" : "Angular",
                   startLine: line, endLine: line });
      edges.push({ id: hashId(fileId + id + "Contains"),
                   sourceId: fileId, targetId: id, kind: "Contains", weight: 1 });
      if (base) {
        const baseId = hashId(filePath + "::" + base);
        edges.push({ id: hashId(id + baseId + "Inherits"),
                     sourceId: id, targetId: baseId, kind: "Inherits", weight: 1 });
      }
    }

    // Imports
    const importRe = /^import\s+.+\s+from\s+['"](.+)['"]/gm;
    while ((m = importRe.exec(source)) !== null) {
      const modulePath = m[1];
      const moduleId   = hashId(modulePath + "::" + modulePath);
      edges.push({ id: hashId(fileId + moduleId + "Imports"),
                   sourceId: fileId, targetId: moduleId, kind: "Imports", weight: 1 });
    }

    process.stdout.write(JSON.stringify({ nodes, edges }));
  } catch (err) {
    process.stdout.write(JSON.stringify({ nodes: [], edges: [], error: err.message }));
  }
});

function lineOf(source, index) {
  return source.slice(0, index).split("\n").length;
}

function hashId(str) {
  let hash = 0;
  for (let i = 0; i < str.length; i++) {
    hash = ((hash << 5) - hash + str.charCodeAt(i)) | 0;
  }
  return Math.abs(hash).toString(16).padStart(16, "0");
}

function detectAngularKind(source, classIndex) {
  const before = source.slice(Math.max(0, classIndex - 200), classIndex);
  if (/@Component/.test(before))  return "Component";
  if (/@Injectable/.test(before)) return "Injectable";
  if (/@NgModule/.test(before))   return "NgModule";
  return null;
}
```

### Task 4.5 — NodeProcessRunner (.NET side)

**`src/CodeGraphMcp.Core/Parsing/NodeProcessRunner.cs`**
```csharp
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodeGraphMcp.Core.Domain;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Parsing;

public sealed class NodeProcessRunner(ILogger<NodeProcessRunner> logger)
{
    // Locate the parse-js.mjs script relative to this assembly
    private static readonly string ScriptPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "parse-js.mjs");

    public async Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo("node")
            {
                Arguments            = $"\"{Path.GetFullPath(ScriptPath)}\"",
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute       = false,
                StandardInputEncoding  = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            };

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("node not found");
            var request = JsonSerializer.Serialize(new { filePath });
            await proc.StandardInput.WriteAsync(request);
            proc.StandardInput.Close();

            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            var dto = JsonSerializer.Deserialize<JsParseResultDto>(stdout,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (dto is null) return ParseResult.Empty;

            var nodes = dto.Nodes.Select(n => new CodeNode
            {
                Id       = n.Id,
                Kind     = Enum.Parse<NodeKind>(n.Kind, ignoreCase: true),
                Name     = n.Name,
                FullName = n.FullName,
                FilePath = n.FilePath,
                Language = Enum.Parse<Language>(n.Language, ignoreCase: true),
                StartLine = n.StartLine,
                EndLine   = n.EndLine,
            }).ToList();

            var edges = dto.Edges.Select(e => new CodeEdge
            {
                Id       = e.Id,
                SourceId = e.SourceId,
                TargetId = e.TargetId,
                Kind     = Enum.Parse<RelationKind>(e.Kind, ignoreCase: true),
                Weight   = e.Weight,
            }).ToList();

            return new ParseResult(nodes, edges);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Node.js parser failed for {File}", filePath);
            return ParseResult.Empty;
        }
    }

    // DTOs for JS output deserialization
    private record JsParseResultDto(List<JsNodeDto> Nodes, List<JsEdgeDto> Edges);
    private record JsNodeDto(string Id, string Kind, string Name, string FullName,
        string FilePath, string Language, int StartLine, int EndLine);
    private record JsEdgeDto(string Id, string SourceId, string TargetId, string Kind, float Weight);
}
```

### Task 4.6 — JavaScript/TypeScript/Angular parser

**`src/CodeGraphMcp.Core/Parsing/JavaScriptParser.cs`**
```csharp
using CodeGraphMcp.Core.Domain;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Parsing;

public sealed class JavaScriptParser(NodeProcessRunner runner, ILogger<JavaScriptParser> logger) : ILanguageParser
{
    public Language Language => Language.JavaScript;

    public Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
        => runner.ParseAsync(filePath, ct);
}
```

Create `TypeScriptParser.cs` and `AngularParser.cs` as identical wrappers with `Language.TypeScript` and `Language.Angular` respectively — they all delegate to `NodeProcessRunner`.

### Task 4.7 — Project file parser

**`src/CodeGraphMcp.Core/Parsing/ProjectFileParser.cs`**
```csharp
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
```

**Acceptance criteria:** Manually test each parser on the sample repo files in `Fixtures/SampleRepo`. Each should return at least one node and no exceptions.

---

## 7. Phase 5 — Graph Store (SQLite)

### Task 5.1 — Schema

**`src/CodeGraphMcp.Core/Graph/GraphStoreSchema.cs`**
```csharp
namespace CodeGraphMcp.Core.Graph;

internal static class GraphStoreSchema
{
    internal const string CreateNodes = """
        CREATE TABLE IF NOT EXISTS nodes (
            id         TEXT PRIMARY KEY,
            kind       TEXT NOT NULL,
            name       TEXT NOT NULL,
            full_name  TEXT NOT NULL,
            file_path  TEXT NOT NULL,
            language   TEXT NOT NULL,
            start_line INTEGER NOT NULL DEFAULT 0,
            end_line   INTEGER NOT NULL DEFAULT 0,
            summary    TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_nodes_file ON nodes(file_path);
        CREATE INDEX IF NOT EXISTS idx_nodes_name ON nodes(name COLLATE NOCASE);
        CREATE INDEX IF NOT EXISTS idx_nodes_full ON nodes(full_name COLLATE NOCASE);
        """;

    internal const string CreateEdges = """
        CREATE TABLE IF NOT EXISTS edges (
            id         TEXT PRIMARY KEY,
            source_id  TEXT NOT NULL,
            target_id  TEXT NOT NULL,
            kind       TEXT NOT NULL,
            weight     REAL NOT NULL DEFAULT 1.0
        );
        CREATE INDEX IF NOT EXISTS idx_edges_source ON edges(source_id);
        CREATE INDEX IF NOT EXISTS idx_edges_target ON edges(target_id);
        """;

    internal const string CreateMeta = """
        CREATE TABLE IF NOT EXISTS meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """;
}
```

### Task 5.2 — GraphStore

**`src/CodeGraphMcp.Core/Graph/GraphStore.cs`**
```csharp
using CodeGraphMcp.Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Graph;

public sealed class GraphStore : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ILogger<GraphStore> _logger;

    public GraphStore(string dbPath, ILogger<GraphStore> logger)
    {
        _logger = logger;
        _conn   = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        InitialiseSchema();
        logger.LogInformation("GraphStore opened at {Path}", dbPath);
    }

    private void InitialiseSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = GraphStoreSchema.CreateNodes
                        + GraphStoreSchema.CreateEdges
                        + GraphStoreSchema.CreateMeta;
        cmd.ExecuteNonQuery();
    }

    // ── Upsert ──────────────────────────────────────────────────────────────

    public void UpsertNode(CodeNode n)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO nodes (id, kind, name, full_name, file_path, language, start_line, end_line, summary)
            VALUES (@id, @kind, @name, @full, @file, @lang, @sl, @el, @sum)
            ON CONFLICT(id) DO UPDATE SET
                kind=excluded.kind, name=excluded.name, full_name=excluded.full_name,
                start_line=excluded.start_line, end_line=excluded.end_line, summary=excluded.summary;
            """;
        cmd.Parameters.AddWithValue("@id",   n.Id);
        cmd.Parameters.AddWithValue("@kind", n.Kind.ToString());
        cmd.Parameters.AddWithValue("@name", n.Name);
        cmd.Parameters.AddWithValue("@full", n.FullName);
        cmd.Parameters.AddWithValue("@file", n.FilePath);
        cmd.Parameters.AddWithValue("@lang", n.Language.ToString());
        cmd.Parameters.AddWithValue("@sl",   n.StartLine);
        cmd.Parameters.AddWithValue("@el",   n.EndLine);
        cmd.Parameters.AddWithValue("@sum",  (object?)n.Summary ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void UpsertEdge(CodeEdge e)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO edges (id, source_id, target_id, kind, weight)
            VALUES (@id, @src, @tgt, @kind, @w)
            ON CONFLICT(id) DO UPDATE SET kind=excluded.kind, weight=excluded.weight;
            """;
        cmd.Parameters.AddWithValue("@id",   e.Id);
        cmd.Parameters.AddWithValue("@src",  e.SourceId);
        cmd.Parameters.AddWithValue("@tgt",  e.TargetId);
        cmd.Parameters.AddWithValue("@kind", e.Kind.ToString());
        cmd.Parameters.AddWithValue("@w",    e.Weight);
        cmd.ExecuteNonQuery();
    }

    // ── Delete by file ───────────────────────────────────────────────────────

    public void RemoveFileNodes(string filePath)
    {
        // Get all node ids for this file first
        var ids = new List<string>();
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = "SELECT id FROM nodes WHERE file_path = @fp";
            sel.Parameters.AddWithValue("@fp", filePath);
            using var r = sel.ExecuteReader();
            while (r.Read()) ids.Add(r.GetString(0));
        }

        if (ids.Count == 0) return;

        // Remove edges where source or target is a node from this file
        foreach (var id in ids)
        {
            using var delEdge = _conn.CreateCommand();
            delEdge.CommandText = "DELETE FROM edges WHERE source_id = @id OR target_id = @id";
            delEdge.Parameters.AddWithValue("@id", id);
            delEdge.ExecuteNonQuery();
        }

        // Remove nodes
        using var delNode = _conn.CreateCommand();
        delNode.CommandText = "DELETE FROM nodes WHERE file_path = @fp";
        delNode.Parameters.AddWithValue("@fp", filePath);
        delNode.ExecuteNonQuery();

        _logger.LogDebug("Removed {Count} nodes for {File}", ids.Count, filePath);
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    public CodeGraph LoadGraph(string rootPath)
    {
        var graph = new CodeGraph { RootPath = rootPath, GeneratedAt = DateTimeOffset.UtcNow };

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id,kind,name,full_name,file_path,language,start_line,end_line,summary FROM nodes";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var node = new CodeNode
                {
                    Id        = r.GetString(0),
                    Kind      = Enum.Parse<NodeKind>(r.GetString(1)),
                    Name      = r.GetString(2),
                    FullName  = r.GetString(3),
                    FilePath  = r.GetString(4),
                    Language  = Enum.Parse<Language>(r.GetString(5)),
                    StartLine = r.GetInt32(6),
                    EndLine   = r.GetInt32(7),
                    Summary   = r.IsDBNull(8) ? null : r.GetString(8),
                };
                graph.Nodes[node.Id] = node;
            }
        }

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id,source_id,target_id,kind,weight FROM edges";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                graph.Edges.Add(new CodeEdge
                {
                    Id       = r.GetString(0),
                    SourceId = r.GetString(1),
                    TargetId = r.GetString(2),
                    Kind     = Enum.Parse<RelationKind>(r.GetString(3)),
                    Weight   = (float)r.GetDouble(4),
                });
            }
        }

        return graph;
    }

    public List<CodeNode> SearchNodes(string query)
    {
        var results = new List<CodeNode>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id,kind,name,full_name,file_path,language,start_line,end_line,summary
            FROM nodes
            WHERE name LIKE @q COLLATE NOCASE OR full_name LIKE @q COLLATE NOCASE
            LIMIT 50
            """;
        cmd.Parameters.AddWithValue("@q", $"%{query}%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new CodeNode
            {
                Id        = r.GetString(0),
                Kind      = Enum.Parse<NodeKind>(r.GetString(1)),
                Name      = r.GetString(2),
                FullName  = r.GetString(3),
                FilePath  = r.GetString(4),
                Language  = Enum.Parse<Language>(r.GetString(5)),
                StartLine = r.GetInt32(6),
                EndLine   = r.GetInt32(7),
                Summary   = r.IsDBNull(8) ? null : r.GetString(8),
            });
        }
        return results;
    }

    public List<CodeEdge> GetEdgesForNode(string nodeId)
    {
        var edges = new List<CodeEdge>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id,source_id,target_id,kind,weight FROM edges
            WHERE source_id = @id OR target_id = @id
            """;
        cmd.Parameters.AddWithValue("@id", nodeId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            edges.Add(new CodeEdge
            {
                Id       = r.GetString(0),
                SourceId = r.GetString(1),
                TargetId = r.GetString(2),
                Kind     = Enum.Parse<RelationKind>(r.GetString(3)),
                Weight   = (float)r.GetDouble(4),
            });
        }
        return edges;
    }

    public List<CodeNode> GetNodesByFile(string filePath)
    {
        var nodes = new List<CodeNode>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id,kind,name,full_name,file_path,language,start_line,end_line,summary
            FROM nodes WHERE file_path = @fp
            """;
        cmd.Parameters.AddWithValue("@fp", filePath);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            nodes.Add(new CodeNode
            {
                Id        = r.GetString(0),
                Kind      = Enum.Parse<NodeKind>(r.GetString(1)),
                Name      = r.GetString(2),
                FullName  = r.GetString(3),
                FilePath  = r.GetString(4),
                Language  = Enum.Parse<Language>(r.GetString(5)),
                StartLine = r.GetInt32(6),
                EndLine   = r.GetInt32(7),
                Summary   = r.IsDBNull(8) ? null : r.GetString(8),
            });
        }
        return nodes;
    }

    public (int nodes, int edges) GetStats()
    {
        int n, e;
        using (var c = _conn.CreateCommand())
        {
            c.CommandText = "SELECT COUNT(*) FROM nodes";
            n = Convert.ToInt32(c.ExecuteScalar());
        }
        using (var c = _conn.CreateCommand())
        {
            c.CommandText = "SELECT COUNT(*) FROM edges";
            e = Convert.ToInt32(c.ExecuteScalar());
        }
        return (n, e);
    }

    public void Dispose() => _conn.Dispose();
}
```

**Acceptance criteria:** Unit tests can create a `GraphStore`, upsert nodes and edges, call `RemoveFileNodes`, and verify row counts.

---

## 8. Phase 6 — Graph Orchestrator

**`src/CodeGraphMcp.Core/Orchestration/GraphOrchestrator.cs`**
```csharp
using CodeGraphMcp.Core.Domain;
using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Core.Parsing;
using CodeGraphMcp.Core.Scanning;
using CodeGraphMcp.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Orchestration;

public sealed class GraphOrchestrator(
    RepositoryScanner scanner,
    GraphStore store,
    IEnumerable<ILanguageParser> parsers,
    ILogger<GraphOrchestrator> logger)
{
    private readonly Dictionary<Language, ILanguageParser> _parsers =
        parsers.ToDictionary(p => p.Language);

    // ── Full build ───────────────────────────────────────────────────────────

    public async Task BuildAsync(string rootPath, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        logger.LogInformation("Full graph build starting for {Root}", rootPath);

        var files = await scanner.ScanAsync(rootPath, ct);
        var semaphore = new SemaphoreSlim(8);

        await Parallel.ForEachAsync(files, ct, async (file, innerCt) =>
        {
            await semaphore.WaitAsync(innerCt);
            try { await ParseAndUpsert(file.FilePath, innerCt); }
            finally { semaphore.Release(); }
        });

        var (n, e) = store.GetStats();
        logger.LogInformation(
            "Build complete: {Files} files, {Nodes} nodes, {Edges} edges in {Ms}ms",
            files.Count, n, e, sw.ElapsedMilliseconds);
    }

    // ── Incremental rebuild of a single file ─────────────────────────────────

    public async Task RebuildFileAsync(string filePath, CancellationToken ct = default)
    {
        logger.LogDebug("Incremental rebuild: {File}", filePath);
        store.RemoveFileNodes(filePath);
        await ParseAndUpsert(filePath, ct);
    }

    // ── Rename ──────────────────────────────────────────────────────────────

    public async Task RenameFileAsync(string oldPath, string newPath, CancellationToken ct = default)
    {
        store.RemoveFileNodes(oldPath);
        if (File.Exists(newPath))
            await ParseAndUpsert(newPath, ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task ParseAndUpsert(string filePath, CancellationToken ct)
    {
        var lang = LanguageDetector.Detect(filePath);
        if (!_parsers.TryGetValue(lang, out var parser)) return;

        var result = await parser.ParseAsync(filePath, ct);

        foreach (var node in result.Nodes) store.UpsertNode(node);
        foreach (var edge in result.Edges) store.UpsertEdge(edge);
    }
}
```

**Acceptance criteria:** Calling `BuildAsync` on the `SampleRepo` fixture produces a graph with at least 5 nodes and 3 edges.

---

## 9. Phase 7 — FileSystemWatcher & Incremental Updates

### Task 7.1 — FileChangeEvent

**`src/CodeGraphMcp/Watcher/FileChangeEvent.cs`**
```csharp
namespace CodeGraphMcp.Watcher;

public sealed record FileChangeEvent(
    string FilePath,
    WatcherChangeTypes ChangeType,
    string? OldPath = null   // set for Renamed events
);
```

### Task 7.2 — FileChangeWatcher

**`src/CodeGraphMcp/Watcher/FileChangeWatcher.cs`**
```csharp
using System.Collections.Concurrent;
using System.Threading.Channels;
using CodeGraphMcp.Core.Orchestration;
using CodeGraphMcp.Core.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Watcher;

public sealed class FileChangeWatcher : IHostedService, IDisposable
{
    private readonly GraphOrchestrator _orchestrator;
    private readonly ILogger<FileChangeWatcher> _logger;
    private readonly Channel<FileChangeEvent> _processChannel;

    private FileSystemWatcher? _watcher;
    private Task? _consumerTask;
    private CancellationTokenSource? _cts;

    // Debounce state: filePath → pending CancellationTokenSource
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new();

    // Public channel that MCP tool handlers can subscribe to
    public ChannelReader<FileChangeEvent> Events => _processChannel.Reader;

    public FileChangeWatcher(GraphOrchestrator orchestrator, ILogger<FileChangeWatcher> logger)
    {
        _orchestrator   = orchestrator;
        _logger         = logger;
        _processChannel = Channel.CreateUnbounded<FileChangeEvent>(
            new UnboundedChannelOptions { SingleReader = true });
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _consumerTask = ConsumeAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public void Watch(string rootPath)
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        _watcher.Changed += (_, e) => Debounce(e.FullPath, WatcherChangeTypes.Changed);
        _watcher.Created += (_, e) => Debounce(e.FullPath, WatcherChangeTypes.Created);
        _watcher.Deleted += (_, e) => Debounce(e.FullPath, WatcherChangeTypes.Deleted);
        _watcher.Renamed += (_, e) => Debounce(e.FullPath, WatcherChangeTypes.Renamed, e.OldFullPath);
        _watcher.Error   += (_, e) => _logger.LogError(e.GetException(), "FileSystemWatcher error");

        _logger.LogInformation("Watching {Root}", rootPath);
    }

    private void Debounce(string filePath, WatcherChangeTypes changeType, string? oldPath = null)
    {
        if (!LanguageDetector.IsTracked(filePath)) return;

        // Cancel any pending timer for this file and start a fresh 800ms window
        if (_pending.TryRemove(filePath, out var prev)) prev.Cancel();

        var cts = new CancellationTokenSource();
        _pending[filePath] = cts;

        Task.Delay(800, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            _pending.TryRemove(filePath, out _);
            var evt = new FileChangeEvent(filePath, changeType, oldPath);
            _processChannel.Writer.TryWrite(evt);
        }, TaskScheduler.Default);
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var evt in _processChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                _logger.LogInformation("Processing {Change}: {File}", evt.ChangeType, evt.FilePath);

                switch (evt.ChangeType)
                {
                    case WatcherChangeTypes.Changed:
                    case WatcherChangeTypes.Created:
                        await _orchestrator.RebuildFileAsync(evt.FilePath, ct);
                        break;

                    case WatcherChangeTypes.Deleted:
                        _orchestrator.Store.RemoveFileNodes(evt.FilePath);
                        break;

                    case WatcherChangeTypes.Renamed when evt.OldPath is not null:
                        await _orchestrator.RenameFileAsync(evt.OldPath, evt.FilePath, ct);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing file change for {File}", evt.FilePath);
            }
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        _processChannel.Writer.TryComplete();
        if (_consumerTask is not null)
            await _consumerTask.ConfigureAwait(false);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _cts?.Dispose();
    }
}
```

> **Note:** Add a public `Store` property to `GraphOrchestrator` that exposes the `GraphStore` so the watcher can call `RemoveFileNodes` directly on deletions.

**Acceptance criteria:** Integration test (Phase 11, `WatcherTests.cs`) verifies that modifying a file triggers a `FileChangeEvent` within 2 seconds and the graph is updated.

---

## 10. Phase 8 — MCP Server & Tool Endpoints

### Task 8.1 — TokenEstimator utility

**`src/CodeGraphMcp.Core/Utilities/TokenEstimator.cs`**
```csharp
namespace CodeGraphMcp.Core.Utilities;

public static class TokenEstimator
{
    // Rough heuristic: 1 token ≈ 4 characters (conservative for code)
    public static int Estimate(string text) => text.Length / 4;
    public static int Estimate(int charCount) => charCount / 4;
}
```

### Task 8.2 — GetCodeGraph tool

**`src/CodeGraphMcp/Mcp/Tools/GetCodeGraphTool.cs`**
```csharp
using System.ComponentModel;
using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Core.Utilities;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

[McpServerToolType]
public static class GetCodeGraphTool
{
    [McpServerTool, Description("Returns the full repository code graph as compact JSON for AI agent context.")]
    public static string GetCodeGraph(
        GraphStore store,
        AppSettings settings,
        [Description("Max token budget. Defaults to 80000.")] int maxTokens = 80000)
    {
        var graph   = store.LoadGraph(settings.RootPath);
        var json    = graph.ToCompactJson();
        var tokens  = TokenEstimator.Estimate(json);
        var (n, e)  = store.GetStats();

        if (tokens <= maxTokens)
            return json;

        // Over budget: return a compressed summary (file list + edges only, no method nodes)
        var summary = new
        {
            rootPath  = graph.RootPath,
            generated = graph.GeneratedAt,
            stats     = new { nodeCount = n, edgeCount = e, estimatedTokens = tokens },
            files     = graph.Nodes.Values
                .Where(nd => nd.Kind == CodeGraphMcp.Core.Domain.NodeKind.File)
                .Select(nd => new { nd.RelativePath(settings.RootPath), nd.Language, nd.Name }),
            edges     = graph.Edges.Take(2000),
        };

        return System.Text.Json.JsonSerializer.Serialize(summary,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
    }
}
```

> **Note:** Add a `RelativePath(string root)` extension method to `CodeNode` that returns `Path.GetRelativePath(root, FilePath)`.

### Task 8.3 — GetFileContext tool

**`src/CodeGraphMcp/Mcp/Tools/GetFileContextTool.cs`**
```csharp
using System.ComponentModel;
using CodeGraphMcp.Core.Graph;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

[McpServerToolType]
public static class GetFileContextTool
{
    [McpServerTool, Description("Returns all nodes and edges for a specific file path, including connected nodes up to hopDepth hops away.")]
    public static string GetFileContext(
        GraphStore store,
        [Description("Relative or absolute file path")] string filePath,
        [Description("Number of hops to traverse from the file's nodes. Default 2.")] int hopDepth = 2)
    {
        var fileNodes = store.GetNodesByFile(filePath);
        if (fileNodes.Count == 0)
            return $"{{\"error\":\"No nodes found for {filePath}\"}}";

        var visitedNodeIds = new HashSet<string>(fileNodes.Select(n => n.Id));
        var allEdges       = new List<CodeGraphMcp.Core.Domain.CodeEdge>();

        // BFS up to hopDepth
        var frontier = new Queue<string>(fileNodes.Select(n => n.Id));
        for (int hop = 0; hop < hopDepth && frontier.Count > 0; hop++)
        {
            var nextFrontier = new Queue<string>();
            while (frontier.Count > 0)
            {
                var id    = frontier.Dequeue();
                var edges = store.GetEdgesForNode(id);
                foreach (var edge in edges)
                {
                    allEdges.Add(edge);
                    var otherId = edge.SourceId == id ? edge.TargetId : edge.SourceId;
                    if (visitedNodeIds.Add(otherId)) nextFrontier.Enqueue(otherId);
                }
            }
            frontier = nextFrontier;
        }

        var result = new
        {
            filePath,
            nodes = visitedNodeIds.Count,
            edges = allEdges.Count,
            nodeList = fileNodes,
            edgeList = allEdges.DistinctBy(e => e.Id),
        };

        return System.Text.Json.JsonSerializer.Serialize(result,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
    }
}
```

### Task 8.4 — GetSymbol tool

**`src/CodeGraphMcp/Mcp/Tools/GetSymbolTool.cs`**
```csharp
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
```

### Task 8.5 — GetSystemPrompt tool

**`src/CodeGraphMcp/Mcp/Tools/GetSystemPromptTool.cs`**
```csharp
using System.ComponentModel;
using CodeGraphMcp.Core.Context;
using CodeGraphMcp.Core.Graph;
using ModelContextProtocol.Server;

namespace CodeGraphMcp.Mcp.Tools;

[McpServerToolType]
public static class GetSystemPromptTool
{
    [McpServerTool, Description("Returns a compact markdown context block describing the repository for use as an AI agent system prompt.")]
    public static string GetSystemPrompt(GraphStore store, AppSettings settings)
    {
        var graph   = store.LoadGraph(settings.RootPath);
        var builder = new SystemPromptBuilder(settings.RootPath);
        return builder.Build(graph);
    }
}
```

**Acceptance criteria:** All four tools compile. Running `dotnet build src/CodeGraphMcp` succeeds.

---

## 11. Phase 9 — System Prompt Builder

**`src/CodeGraphMcp.Core/Context/SystemPromptBuilder.cs`**
```csharp
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
```

---

## 12. Phase 10 — Configuration & Startup

### Task 10.1 — AppSettings

**`src/CodeGraphMcp/appsettings.json`**
```json
{
  "CodeGraphMcp": {
    "RootPath": "",
    "DbPath": "codegraph.db",
    "MaxTokens": 80000,
    "DebounceMs": 800,
    "MaxParserConcurrency": 8,
    "EnableWatcher": true
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

**`src/CodeGraphMcp/Startup/ServiceRegistration.cs`**
```csharp
using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Core.Orchestration;
using CodeGraphMcp.Core.Parsing;
using CodeGraphMcp.Core.Scanning;
using CodeGraphMcp.Watcher;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeGraphMcp.Startup;

public sealed class AppSettings
{
    public string RootPath            { get; set; } = string.Empty;
    public string DbPath              { get; set; } = "codegraph.db";
    public int    MaxTokens           { get; set; } = 80_000;
    public int    DebounceMs          { get; set; } = 800;
    public int    MaxParserConcurrency { get; set; } = 8;
    public bool   EnableWatcher       { get; set; } = true;
}

public static class ServiceRegistration
{
    public static IServiceCollection AddCodeGraphMcp(
        this IServiceCollection services,
        string rootPath,
        string dbPath)
    {
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<GraphStore>>();
            return new GraphStore(dbPath, logger);
        });

        services.AddSingleton<RepositoryScanner>();
        services.AddSingleton<NodeProcessRunner>();

        // Register all parsers
        services.AddSingleton<ILanguageParser, CSharpParser>();
        services.AddSingleton<ILanguageParser, XamlParser>();
        services.AddSingleton<ILanguageParser, JavaScriptParser>();
        services.AddSingleton<ILanguageParser, ProjectFileParser>();

        services.AddSingleton<GraphOrchestrator>();
        services.AddSingleton<FileChangeWatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<FileChangeWatcher>());

        services.AddSingleton(new AppSettings { RootPath = rootPath, DbPath = dbPath });

        return services;
    }
}
```

### Task 10.2 — Program.cs

**`src/CodeGraphMcp/Program.cs`**
```csharp
using CodeGraphMcp.Core.Orchestration;
using CodeGraphMcp.Mcp.Tools;
using CodeGraphMcp.Startup;
using CodeGraphMcp.Watcher;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ── CLI args ──────────────────────────────────────────────────────────────────
var rootPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Environment.GetEnvironmentVariable("CODEGRAPH_ROOT")
      ?? throw new ArgumentException("Provide repository root path as first argument or CODEGRAPH_ROOT env var.");

var dbPath = args.Length > 1
    ? args[1]
    : Path.Combine(rootPath, ".codegraph", "graph.db");

Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

// ── Host ──────────────────────────────────────────────────────────────────────
var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(log =>
    {
        log.ClearProviders();
        // MCP uses stdio — log to stderr to avoid polluting the MCP channel
        log.AddConsole(opt => opt.LogToStandardErrorThreshold = LogLevel.Trace);
    })
    .ConfigureServices(services =>
    {
        services.AddCodeGraphMcp(rootPath, dbPath);

        // Register MCP server with all four tools
        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<GetCodeGraphTool>()
            .WithTools<GetFileContextTool>()
            .WithTools<GetSymbolTool>()
            .WithTools<GetSystemPromptTool>();
    })
    .Build();

// ── Initial full graph build ─────────────────────────────────────────────────
var orchestrator = host.Services.GetRequiredService<GraphOrchestrator>();
await orchestrator.BuildAsync(rootPath);

// ── Start file watcher ───────────────────────────────────────────────────────
var watcher = host.Services.GetRequiredService<FileChangeWatcher>();
watcher.Watch(rootPath);

// ── Run MCP server (blocks until process exits) ───────────────────────────────
await host.RunAsync();
```

**Acceptance criteria:** `dotnet run --project src/CodeGraphMcp -- /path/to/any/repo` starts without exceptions, performs the initial build, and begins watching.

---

## 13. Phase 11 — Integration Tests

### Task 11.1 — Sample repo fixture files

Create these minimal files in `src/CodeGraphMcp.Tests/Fixtures/SampleRepo/`:

**`src/OrderService.cs`**
```csharp
namespace SampleRepo;

public class OrderService : IOrderRepository
{
    private readonly IOrderRepository _repo;
    public OrderService(IOrderRepository repo) { _repo = repo; }
    public void PlaceOrder(string orderId) => _repo.Save(orderId);
}
```

**`src/IOrderRepository.cs`**
```csharp
namespace SampleRepo;

public interface IOrderRepository
{
    void Save(string orderId);
}
```

**`src/OrderViewModel.cs`**
```csharp
namespace SampleRepo;

public class OrderViewModel
{
    public string OrderId { get; set; } = string.Empty;
}
```

**`ui/OrderView.xaml`**
```xml
<?xml version="1.0" encoding="utf-8"?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="SampleRepo.OrderView">
    <StackLayout>
        <Label x:Name="OrderLabel" Text="Order" />
    </StackLayout>
</ContentPage>
```

**`ui/app.component.ts`**
```typescript
import { Component } from '@angular/core';

@Component({
  selector: 'app-root',
  template: '<h1>App</h1>'
})
export class AppComponent {
  title = 'sample-app';
}
```

### Task 11.2 — Orchestrator tests

**`src/CodeGraphMcp.Tests/OrchestratorTests.cs`**
```csharp
using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Core.Orchestration;
using CodeGraphMcp.Core.Parsing;
using CodeGraphMcp.Core.Scanning;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeGraphMcp.Tests;

public sealed class OrchestratorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cg_test_{Guid.NewGuid():N}.db");
    private readonly string _repoPath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "SampleRepo");
    private readonly GraphStore _store;

    public OrchestratorTests()
    {
        _store = new GraphStore(_dbPath, NullLogger<GraphStore>.Instance);
    }

    private GraphOrchestrator BuildOrchestrator()
    {
        var runner  = new NodeProcessRunner(NullLogger<NodeProcessRunner>.Instance);
        var parsers = new ILanguageParser[]
        {
            new CSharpParser(NullLogger<CSharpParser>.Instance),
            new XamlParser(NullLogger<XamlParser>.Instance),
            new JavaScriptParser(runner, NullLogger<JavaScriptParser>.Instance),
            new ProjectFileParser(NullLogger<ProjectFileParser>.Instance),
        };
        return new GraphOrchestrator(
            new RepositoryScanner(NullLogger<RepositoryScanner>.Instance),
            _store,
            parsers,
            NullLogger<GraphOrchestrator>.Instance);
    }

    [Fact]
    public async Task BuildAsync_PopulatesNodesAndEdges()
    {
        var orch = BuildOrchestrator();
        await orch.BuildAsync(_repoPath);

        var (n, e) = _store.GetStats();
        n.Should().BeGreaterThan(5);
        e.Should().BeGreaterThan(2);
    }

    [Fact]
    public async Task RebuildFileAsync_ReplacesNodesForFile()
    {
        var orch     = BuildOrchestrator();
        await orch.BuildAsync(_repoPath);
        var (n1, _)  = _store.GetStats();

        var csFile   = Path.Combine(_repoPath, "src", "OrderService.cs");
        await orch.RebuildFileAsync(csFile);
        var (n2, _)  = _store.GetStats();

        // Node count should be stable (removed + re-added)
        n2.Should().BeCloseTo(n1, 3);
    }

    [Fact]
    public async Task GetFileContext_ReturnsXamlViewWithLinkedViewModel()
    {
        var orch     = BuildOrchestrator();
        await orch.BuildAsync(_repoPath);

        var xamlFile = Path.Combine(_repoPath, "ui", "OrderView.xaml");
        var nodes    = _store.GetNodesByFile(xamlFile);
        nodes.Should().NotBeEmpty();

        var edges = _store.GetEdgesForNode(nodes.First().Id);
        // Should have a Binds edge to OrderViewModel
        edges.Should().Contain(e => e.Kind == CodeGraphMcp.Core.Domain.RelationKind.Binds);
    }

    [Fact]
    public async Task TokenEstimate_BelowOneMillion()
    {
        var orch  = BuildOrchestrator();
        await orch.BuildAsync(_repoPath);

        var graph  = _store.LoadGraph(_repoPath);
        var tokens = graph.EstimateTokenCount();
        tokens.Should().BeLessThan(1_000_000);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
```

### Task 11.3 — Watcher tests

**`src/CodeGraphMcp.Tests/WatcherTests.cs`**
```csharp
using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Core.Orchestration;
using CodeGraphMcp.Core.Parsing;
using CodeGraphMcp.Core.Scanning;
using CodeGraphMcp.Watcher;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeGraphMcp.Tests;

public sealed class WatcherTests : IDisposable
{
    private readonly string _tempRepo = Path.Combine(Path.GetTempPath(), $"cg_repo_{Guid.NewGuid():N}");
    private readonly string _dbPath   = Path.Combine(Path.GetTempPath(), $"cg_test_{Guid.NewGuid():N}.db");
    private readonly GraphStore _store;

    public WatcherTests()
    {
        Directory.CreateDirectory(_tempRepo);
        _store = new GraphStore(_dbPath, NullLogger<GraphStore>.Instance);
    }

    [Fact]
    public async Task FileChange_TriggersGraphUpdateWithin2Seconds()
    {
        // Arrange
        var csFile  = Path.Combine(_tempRepo, "Test.cs");
        await File.WriteAllTextAsync(csFile, "namespace T; public class A {}");

        var runner   = new NodeProcessRunner(NullLogger<NodeProcessRunner>.Instance);
        var parsers  = new ILanguageParser[] { new CSharpParser(NullLogger<CSharpParser>.Instance) };
        var orch     = new GraphOrchestrator(
            new RepositoryScanner(NullLogger<RepositoryScanner>.Instance),
            _store, parsers, NullLogger<GraphOrchestrator>.Instance);
        await orch.BuildAsync(_tempRepo);

        var watcher = new FileChangeWatcher(orch, NullLogger<FileChangeWatcher>.Instance);
        await watcher.StartAsync(CancellationToken.None);
        watcher.Watch(_tempRepo);

        // Act — modify the file
        await Task.Delay(200);
        await File.WriteAllTextAsync(csFile, "namespace T; public class A {} public class B {}");

        // Assert — wait up to 2 seconds for the event
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        FileChangeEvent? received = null;
        try
        {
            received = await watcher.Events.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) { }

        received.Should().NotBeNull();
        received!.FilePath.Should().Be(csFile);

        await watcher.StopAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_tempRepo)) Directory.Delete(_tempRepo, recursive: true);
    }
}
```

Run all tests:
```bash
dotnet test CodeGraphMcp.sln --logger "console;verbosity=normal"
```

**Acceptance criteria:** All tests pass with exit code 0.

---

## 14. Phase 12 — Production Hardening

### Task 12.1 — Global error handling in watcher

The `ConsumeAsync` loop in `FileChangeWatcher` already has a try/catch. Ensure:
- Exceptions are logged with full stack trace via `_logger.LogError`.
- The consumer loop never exits on a single file's failure.
- Cancellation is correctly propagated and not swallowed.

### Task 12.2 — SQLite WAL mode

Add to `GraphStore` constructor after opening the connection:

```csharp
using var wal = _conn.CreateCommand();
wal.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
wal.ExecuteNonQuery();
```

This prevents database lock errors when the watcher and MCP tool requests run concurrently.

### Task 12.3 — Structured logging

Ensure every log call uses structured parameters, not string interpolation:

```csharp
// Correct
logger.LogInformation("Parsing {File} ({Language})", filePath, lang);

// Wrong
logger.LogInformation($"Parsing {filePath} ({lang})");
```

### Task 12.4 — Graceful shutdown

In `Program.cs`, register a SIGINT/SIGTERM handler:

```csharp
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
};
```

### Task 12.5 — Health check on startup

After `BuildAsync`, log a summary:

```csharp
var (nodes, edges) = store.GetStats();
var tokens         = store.LoadGraph(rootPath).EstimateTokenCount();
logger.LogInformation(
    "Graph ready: {Nodes} nodes, {Edges} edges, ~{Tokens} tokens",
    nodes, edges, tokens);
```

### Task 12.6 — `.gitignore` entry

Add to the repository root `.gitignore`:

```
.codegraph/
*.db
*.db-wal
*.db-shm
```

---

## 15. Phase 13 — MCP Client Configuration

### Task 13.1 — Claude Desktop (`claude_desktop_config.json`)

Location: `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows).

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/src/CodeGraphMcp",
        "--",
        "/absolute/path/to/your/repo"
      ],
      "env": {}
    }
  }
}
```

For production use a published binary instead of `dotnet run`:

```bash
dotnet publish src/CodeGraphMcp -c Release -o ./publish
```

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "/absolute/path/to/publish/CodeGraphMcp",
      "args": ["/absolute/path/to/your/repo"],
      "env": {}
    }
  }
}
```

### Task 13.2 — Cursor (`.cursor/mcp.json`)

Place in the root of the repository Cursor has open:

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/src/CodeGraphMcp",
        "--",
        "${workspaceFolder}"
      ]
    }
  }
}
```

### Task 13.3 — Verify connection

Start the server manually and confirm it emits the MCP capability handshake on stdout:

```bash
dotnet run --project src/CodeGraphMcp -- /path/to/repo
```

You should see structured JSON lines on stdout (MCP protocol) and log lines on stderr.

---

## Appendix A — NuGet Package Reference

| Package | Version | Used in |
|---|---|---|
| `ModelContextProtocol` | 0.2.0-preview | CodeGraphMcp |
| `Microsoft.Extensions.Hosting` | 10.0.0 | CodeGraphMcp |
| `Microsoft.Extensions.Logging.Console` | 10.0.0 | CodeGraphMcp |
| `Microsoft.CodeAnalysis.CSharp` | 4.11.0 | CodeGraphMcp.Core |
| `Microsoft.Data.Sqlite` | 9.0.0 | CodeGraphMcp.Core |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.0 | CodeGraphMcp.Core |
| `Microsoft.Extensions.Options` | 10.0.0 | CodeGraphMcp.Core |
| `Microsoft.NET.Test.Sdk` | 17.12.0 | CodeGraphMcp.Tests |
| `xunit` | 2.9.2 | CodeGraphMcp.Tests |
| `xunit.runner.visualstudio` | 2.8.2 | CodeGraphMcp.Tests |
| `FluentAssertions` | 6.12.2 | CodeGraphMcp.Tests |

---

## Appendix B — Acceptance Checklist

Complete every item before considering the project production-ready.

- [ ] `dotnet build CodeGraphMcp.sln` → exit 0, zero warnings
- [ ] `dotnet test CodeGraphMcp.sln` → all tests green
- [ ] `dotnet run --project src/CodeGraphMcp -- /tmp/test-repo` → builds graph, logs stats, does not crash
- [ ] Modifying a `.cs` file in the watched repo → `FileChangeWatcher` logs the event within 2 seconds
- [ ] Deleting a `.cs` file → node count decreases
- [ ] `get_code_graph` MCP tool → returns valid JSON with `nodes` and `edges`
- [ ] `get_file_context` MCP tool → returns nodes for a given file
- [ ] `get_symbol` MCP tool → returns matching nodes with snippet
- [ ] `get_system_prompt` MCP tool → returns markdown under 4000 tokens
- [ ] Token estimate for a 500-file repo → below 100,000
- [ ] SQLite WAL mode enabled → no lock errors under concurrent reads/writes
- [ ] All log calls use structured parameters (no string interpolation)
- [ ] `.codegraph/` and `*.db` excluded from version control
- [ ] MCP server registered in Claude Desktop or Cursor config and connection verified
