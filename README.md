# CodeGraphMcp

A local **MCP (Model Context Protocol) server** written in .NET 10 that scans code repositories, builds a **code graph** of symbols and relationships, and exposes it to AI agents (Claude, Cursor, Copilot) via MCP tool endpoints.

## ✨ Features

- **Multi-language parsing** — C#, XAML, JavaScript, TypeScript, Angular, JSON, SQL, Markdown, and .csproj/.sln project files
- **Code graph construction** — Nodes (files, classes, methods, functions) and edges (calls, imports, inherits, references, binds)
- **SQLite storage** — Persistent, fast graph storage with WAL mode for concurrent access
- **Real-time file watching** — Automatically detects file changes and incrementally patches the graph (800ms debounce)
- **MCP tool endpoints** — Four tools for AI agents to query the repository structure
- **System prompt builder** — Generates a compact, token-budgeted context block for AI agents

## 📋 Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) (optional — required only for JS/TS/Angular parsing)

## 🚀 Quick Start

### Build

```bash
dotnet build CodeGraphMcp.sln
```

### Run

```bash
# Point it at any code repository
dotnet run --project src/CodeGraphMcp -- /path/to/your/repo
```

The server will:
1. Scan the repository for tracked files
2. Parse each file and build the code graph
3. Store the graph in `.codegraph/graph.db`
4. Start the MCP server on stdio
5. Watch for file changes and update the graph incrementally

### Test

```bash
dotnet test CodeGraphMcp.sln
```

### Publish (Production)

```bash
dotnet publish src/CodeGraphMcp -c Release -o ./publish
./publish/CodeGraphMcp /path/to/your/repo
```

## 🔧 MCP Tools

The server exposes four tools via the MCP protocol:

| Tool | Description |
|------|-------------|
| `GetCodeGraph` | Returns the full repository code graph as compact JSON. Supports a `maxTokens` budget (default 80,000) — if over budget, returns a compressed summary. |
| `GetFileContext` | Returns all nodes and edges for a specific file path, with BFS traversal up to `hopDepth` hops (default 2). |
| `GetSymbol` | Searches for a symbol by name (partial, case-insensitive) and returns definitions, file locations, edges, and code snippets. |
| `GetSystemPrompt` | Returns a compact markdown context block describing the repository — key entry points, file map, and symbol index — within a 4,000 token budget. |

## 🔌 Client Configuration

### Claude Desktop

Add to `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

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

For production, use the published binary:

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

### Cursor

Place `.cursor/mcp.json` in the root of the repository Cursor has open:

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

## 🏗️ Architecture

```
CodeGraphMcp/
├── src/
│   ├── CodeGraphMcp/                    # Main executable + MCP server
│   │   ├── Program.cs                   # Entry point
│   │   ├── Mcp/Tools/                   # MCP tool endpoints
│   │   ├── Watcher/                     # FileSystemWatcher + debounce
│   │   └── Startup/                     # DI registration
│   ├── CodeGraphMcp.Core/              # Domain + graph logic
│   │   ├── Domain/                      # CodeNode, CodeEdge, enums
│   │   ├── Scanning/                    # Repository file discovery
│   │   ├── Parsing/                     # Language parsers (C#, XAML, JS, etc.)
│   │   ├── Graph/                       # SQLite graph store
│   │   ├── Orchestration/               # Build + incremental update
│   │   ├── Context/                     # System prompt builder
│   │   └── Utilities/                   # Language detection, token estimation
│   └── CodeGraphMcp.Tests/             # Integration tests
└── scripts/
    └── parse-js.mjs                     # Node.js JS/TS parser script
```

### Supported Languages

| Language | Parser | Nodes Extracted |
|----------|--------|----------------|
| C# | Roslyn (`Microsoft.CodeAnalysis.CSharp`) | Files, namespaces, classes, interfaces, enums, structs, records, methods, properties |
| XAML | `System.Xml.Linq` | Files, XamlView (x:Class), XamlResource (x:Name), ViewModel bindings |
| JavaScript | Node.js regex-based | Files, functions, classes, imports |
| TypeScript | Node.js regex-based | Files, functions, classes, imports |
| Angular | Node.js regex-based | Files, components, injectables, NgModules |
| .csproj/.sln | `System.Xml.Linq` | Files, package references, project references |
| Markdown | Line-based | Files, document sections (headings) |

### Design Constraints

| Constraint | Value |
|------------|-------|
| Runtime | .NET 10 |
| Transport | stdio (MCP standard) |
| Graph storage | SQLite (WAL mode) |
| Debounce window | 800 ms |
| Max parser concurrency | 8 files in parallel |
| Max graph context tokens | 80,000 (configurable) |
| System prompt budget | 4,000 tokens |

## 📦 NuGet Dependencies

| Package | Version | Project |
|---------|---------|---------|
| `ModelContextProtocol` | 0.2.0-preview.1 | CodeGraphMcp |
| `Microsoft.Extensions.Hosting` | 10.0.0 | CodeGraphMcp |
| `Microsoft.Extensions.Logging.Console` | 10.0.0 | CodeGraphMcp |
| `Microsoft.CodeAnalysis.CSharp` | 4.11.0 | CodeGraphMcp.Core |
| `Microsoft.Data.Sqlite` | 9.0.0 | CodeGraphMcp.Core |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.0 | CodeGraphMcp.Core |

## 📄 License

This project is provided as-is for personal and commercial use.
