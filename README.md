# CodeGraphMcp

A local MCP server written in .NET 10 that scans code repositories, builds a code graph of symbols and relationships, and exposes it to AI agents (Claude, Cursor, GitHub Copilot) via tool endpoints.

> Instead of feeding your entire codebase to an AI — hundreds of thousands of tokens — CodeGraphMcp gives it a structured 4K–80K token graph. The AI gets the same understanding at a fraction of the cost.

---

## Why this exists

AI coding assistants have a problem. When they try to understand your codebase, they either read every file (slow, expensive, often exceeds context limits) or read nothing and guess (wrong imports, broken suggestions, missed dependencies).

CodeGraphMcp sits in the middle. It parses your repository once, extracts the structure — classes, interfaces, functions, inheritance, imports, calls — and stores it as a compact graph. The AI queries this graph instead of reading raw source files.

```
500 files × ~400 tokens each = ~200,000 tokens (raw source)

CodeGraphMcp extracts:
  250 nodes (classes, functions, interfaces, modules)
  180 edges (imports, inherits, calls, references)

Compact graph = ~8,000 tokens (96% reduction)
System prompt = ~3,500 tokens (98% reduction)
```

What the AI gets from the graph vs. reading files directly:

| Information | Without CodeGraphMcp | With CodeGraphMcp |
|-------------|---------------------|-------------------|
| File structure | Must read every file | Instant file map with symbol counts |
| Class hierarchy | Must trace inheritance across files | Direct `inherits` / `implements` edges |
| Dependencies | Must read import statements | Pre-computed `imports` / `dependsOn` graph |
| Entry points | Must guess | Top 8 most-connected nodes, ranked |
| Symbol locations | Must search | Exact file + line number for every symbol |
| XAML bindings | Must infer naming conventions | Explicit `binds` edges |
| Total tokens | 200,000+ | 4,000–80,000 |

The token budget adapts automatically:

| Mode | Tokens | What's included |
|------|--------|-----------------|
| System Prompt | ~4,000 | File map, entry points, symbol index |
| Full Graph | ~8,000–80,000 | Complete node + edge graph |
| Compressed | ~2,000–4,000 | Auto-triggered when full graph exceeds budget |

Rough savings by repo size:

| Repository | Raw tokens | Graph tokens | Reduction |
|------------|-----------|-------------|-----------|
| 50 files | ~20,000 | ~2,000 | 90% |
| 200 files | ~80,000 | ~5,000 | 94% |
| 500 files | ~200,000 | ~12,000 | 94% |
| 1,000 files | ~400,000 | ~25,000 | 94% |
| 5,000 files | ~2,000,000 | ~80,000 (capped) | 96% |

---

## Features

- 30+ languages — C#, Java, Kotlin, Swift, C/C++, Objective-C, PHP, Go, Rust, Python, Ruby, Dart, JS/TS, Angular, XAML, SQL, Markdown, HTML, CSS/SCSS, YAML, Shell, project files
- Graph construction — nodes (files, classes, methods, interfaces, structs, enums) and edges (calls, imports, inherits, implements, references, binds)
- SQLite storage with WAL mode for concurrent access
- Real-time file watching with 800ms debounce for incremental updates
- 5 MCP tool endpoints for querying the graph
- System prompt builder (4K token budget)
- Self-contained binaries for Linux, macOS, and Windows

---

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) — optional, only needed for JS/TS/Angular parsing

---

## Getting started

Build:

```bash
dotnet build CodeGraphMcp.sln
```

Run against any repository:

```bash
dotnet run --project src/CodeGraphMcp -- /path/to/your/repo
```

This will scan the repository, parse all supported files, store the graph in `.codegraph/graph.db`, start the MCP server on stdio, and begin watching for file changes.

Run tests:

```bash
dotnet test CodeGraphMcp.sln
```

Publish a standalone binary:

```bash
dotnet publish src/CodeGraphMcp -c Release -o ./publish
./publish/CodeGraphMcp /path/to/your/repo
```

---

## MCP Tools

The server exposes five tools over the MCP protocol.

### `GetCodeGraph`

Returns the full code graph as JSON. Takes an optional `maxTokens` parameter (default 80,000). When the graph exceeds the budget, it returns a compressed summary that still includes type nodes (classes, interfaces, methods) with relative paths and readable edge names — not just a bare file list.

Useful for giving the AI a complete structural overview in a single call.

### `GetFileContext`

Returns all symbols and relationships for a specific file, with BFS traversal up to `hopDepth` hops away (default 2). Accepts both relative and absolute paths. Connected nodes come back with full details — names, kinds, paths — grouped by type. Uses batch SQL queries internally so it stays fast even on large graphs.

Useful when the AI is about to edit a file and needs to understand what it connects to.

### `GetSymbol`

Searches by name (partial match, case-insensitive) and returns definitions, locations, incoming/outgoing connections with resolved names, and code snippets covering the full symbol definition (capped at 30 lines).

Useful for finding where something is defined and what depends on it.

### `GetSystemPrompt`

Returns a compact markdown block — entry points ranked by connectivity, key dependency chains (inheritance, implements), file map, symbol index — all within a 4,000 token budget.

Useful as the opening context for any AI conversation about the codebase.

### `GenerateDesignDocument`

Generates Mermaid diagrams (class diagrams or dependency flow graphs) for a given symbol. Prefers concrete implementations over interfaces when selecting root nodes and sanitizes all labels for valid Mermaid syntax.

Useful for visualizing architecture, inheritance trees, or call graphs.

---

## Supported languages

### Dedicated parsers

| Language | Parser | What it extracts |
|----------|--------|-----------------|
| C# | Roslyn | Files, namespaces, classes, interfaces, enums, structs, records, methods, properties, inheritance |
| XAML | System.Xml.Linq | Files, views (x:Class), resources (x:Name), ViewModel bindings |
| JavaScript | Node.js | Files, functions, classes, imports, Angular decorators |
| TypeScript | Node.js | Files, functions, classes, imports |
| Angular | Node.js | Files, components, injectables, NgModules |
| .csproj/.sln | System.Xml.Linq | Files, package refs, project refs |
| Markdown | Line parser | Files, document sections |

### Regex-based parsers

| Language | Extensions | Extracts |
|----------|-----------|----------|
| Java | `.java` | Classes, interfaces, enums, records, methods, imports, packages, inheritance |
| Kotlin | `.kt`, `.kts` | Classes, interfaces, enums, objects, functions, imports, packages |
| Swift | `.swift` | Classes, structs, protocols, enums, functions, imports |
| C | `.c`, `.h` | Structs, enums, functions, `#include` |
| C++ | `.cpp`, `.cc`, `.cxx`, `.hpp` | Classes, structs, enums, functions, namespaces, `#include`, inheritance |
| Objective-C | `.m`, `.mm` | Interfaces, implementations, protocols, methods, `#import` |
| PHP | `.php` | Classes, interfaces, traits, enums, functions, namespaces, `use` |
| Go | `.go` | Structs, interfaces, functions, packages, imports |
| Rust | `.rs` | Structs, enums, traits, functions, modules, `use` |
| Python | `.py` | Classes, functions, imports |
| Ruby | `.rb` | Classes, modules, methods, `require` |
| Dart | `.dart` | Classes, enums, mixins, functions, imports |
| SQL | `.sql` | Tables, procedures, views, functions |
| HTML | `.html`, `.htm` | Script/style references |
| CSS/SCSS | `.css`, `.scss` | `@import` references |
| Shell | `.sh`, `.bash`, `.zsh` | Functions, `source` includes |
| YAML | `.yaml`, `.yml` | Top-level config keys |
| JSON | `.json` | File tracking (config detection) |

---

## Client configuration

The easiest approach is to download the standalone binary from the [Releases page](../../releases) — no .NET SDK required. Point it at your repo and configure your AI editor to connect.

### Claude Desktop

Add to `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "/path/to/CodeGraphMcp",
      "args": ["/path/to/your/repo"],
      "env": {}
    }
  }
}
```

If building from source, use `"command": "dotnet"` and `"args": ["run", "--project", "/path/to/src/CodeGraphMcp", "--", "/path/to/repo"]`.

### Cursor

Place `.cursor/mcp.json` in the repository root:

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "/path/to/CodeGraphMcp",
      "args": ["${workspaceFolder}"]
    }
  }
}
```

### GitHub Copilot (VS Code)

Add to your VS Code `settings.json`:

```json
{
  "mcp": {
    "servers": {
      "codegraph": {
        "command": "/path/to/CodeGraphMcp",
        "args": ["${workspaceFolder}"]
      }
    }
  }
}
```

After configuring, Copilot Chat will discover the tools automatically. Try asking it to call `GetSystemPrompt` at the start of a conversation.

### Visual Studio (2022 / 2026)

Visual Studio 2022 (v17.13+) and Visual Studio 2026 support MCP tools in Copilot natively.

1. Go to **Tools > Options > GitHub > Copilot > MCP Servers** (or edit `mcp.json`).
2. Add:

```json
{
  "mcpServers": {
    "codegraph": {
      "command": "C:\\path\\to\\CodeGraphMcp.exe",
      "args": ["${workspaceFolder}"]
    }
  }
}
```

### Other clients (Windsurf, Cline, etc.)

Any MCP client can connect via stdio:

```bash
/path/to/CodeGraphMcp /path/to/your/repo
```

The server uses JSON-RPC on stdin/stdout and logs to stderr.

---

## Architecture

```
CodeGraphMcp/
├── src/
│   ├── CodeGraphMcp/                    # Main executable + MCP server
│   │   ├── Program.cs                   # Entry point, graceful shutdown
│   │   ├── Mcp/Tools/                   # 5 tool endpoints
│   │   ├── Watcher/                     # FileSystemWatcher + debounce
│   │   └── Startup/                     # DI registration for 25+ parsers
│   ├── CodeGraphMcp.Core/              # Domain + graph logic
│   │   ├── Domain/                      # CodeNode, CodeEdge, enums
│   │   ├── Scanning/                    # File discovery + hashing
│   │   ├── Parsing/                     # 25+ language parsers
│   │   ├── Graph/                       # SQLite graph store (WAL)
│   │   ├── Orchestration/               # Full + incremental builds
│   │   ├── Context/                     # System prompt builder
│   │   └── Utilities/                   # Language detection, token estimation
│   └── CodeGraphMcp.Tests/             # Integration tests
└── scripts/
    └── parse-js.mjs                     # Node.js parser for JS/TS
```

How it works:

```
               ┌─────────────┐
               │  Repository │
               └──────┬──────┘
                      │ scan
               ┌──────▼──────┐
               │  Scanner    │  Discovers files, computes hashes
               └──────┬──────┘
                      │
           ┌──────────▼──────────┐
           │  Language Parsers   │  25+ parsers, 8 files in parallel
           └──────────┬──────────┘
                      │
               ┌──────▼──────┐
               │ Graph Store │  SQLite with WAL mode
               └──────┬──────┘
                      │
       ┌──────────────┼──────────────┐
       │              │              │
┌──────▼──────┐ ┌────▼────┐ ┌──────▼──────┐
│ MCP Tools   │ │ Watcher │ │  Prompt     │
│ (5 tools)   │ │ (800ms) │ │  Builder    │
└─────────────┘ └─────────┘ └─────────────┘
```

Design constraints:

| Constraint | Value |
|------------|-------|
| Runtime | .NET 10 |
| Transport | stdio (MCP standard) |
| Storage | SQLite (WAL mode) |
| Debounce | 800ms |
| Parser concurrency | 8 files |
| Max graph tokens | 80,000 (configurable) |
| System prompt budget | 4,000 tokens |

---

## How AI editors benefit

Without CodeGraphMcp:

```
Developer: "Add a new payment method to the checkout flow"

AI: reads 50 files trying to understand the codebase (100K+ tokens)
    misses the PaymentService in a different module
    doesn't know about IPaymentGateway
    generates code with wrong imports and missing dependencies
```

With CodeGraphMcp:

```
Developer: "Add a new payment method to the checkout flow"

AI: calls GetSystemPrompt → knows entire repo structure (3,500 tokens)
    calls GetSymbol("Payment") → finds PaymentService, IPaymentGateway, PaymentController
    calls GetFileContext("PaymentService.cs") → sees all connected types
    generates correct code with proper imports and dependency injection
```

The main benefits:

1. **Faster** — the AI doesn't read hundreds of files; the graph gives it structural context instantly
2. **More accurate** — it knows about inheritance, imports, and dependencies before writing anything
3. **Cheaper** — 90–96% fewer tokens per request
4. **Live** — file watcher keeps the graph current as you code
5. **Private** — everything runs locally, nothing leaves your machine
6. **Works everywhere** — any MCP-compatible agent (Claude, Cursor, Copilot, Windsurf, Cline)

---

## Prompting guide

Once CodeGraphMcp is connected to your AI editor, here are some effective ways to use it.

### Start of conversation

Give the AI the big picture first:

> "Call `GetSystemPrompt` to understand this repository. I want to build a new feature."

This returns ~4,000 tokens covering entry points, file map, and symbols. The AI immediately knows your naming conventions, languages, and where things live.

### Large-scale migrations (e.g. Xamarin to MAUI)

> "We're migrating from Xamarin.Forms to .NET MAUI.
> 1. Use `GetCodeGraph` to find all XAML files and their ViewModels.
> 2. Use `GetFileContext` on `App.xaml.cs` to trace the DI setup.
> 3. Generate a migration plan mapping `DependencyService` calls to `MauiProgram.cs`."

The graph's `binds` edges connect Views to ViewModels directly, and `dependsOn` edges trace project references — no need to open 30 files.

### Adding a feature

> "I need a 'User Profile' feature. Find `UserRepository` and `UserController` with `GetSymbol`, then use `GetFileContext` on the controller to see its dependencies. Write the `UpdateProfile` endpoint following existing DI patterns."

`GetSymbol` locates files regardless of folder structure. The edges show exactly which services to inject.

### Debugging across modules

> "The 'Order Placed' event isn't updating inventory.
> 1. `GetSymbol` for `OrderPlacedEvent`.
> 2. Check edges for handlers/implementors.
> 3. Suggest a fix."

The graph has `references` and `implements` edges, so the AI traverses from event to handler without text-searching the whole repo.

### Onboarding

> "I'm new here. Use `GetCodeGraph` and `GetSystemPrompt` to summarize the architecture. What are the top 3 classes to read first?"

Nodes are ranked by connectivity, so the AI finds the core orchestrators and controllers immediately.

---

## Dependencies

| Package | Version | Project |
|---------|---------|---------|
| ModelContextProtocol | 0.2.0-preview.1 | CodeGraphMcp |
| Microsoft.Extensions.Hosting | 10.0.0 | CodeGraphMcp |
| Microsoft.Extensions.Logging.Console | 10.0.0 | CodeGraphMcp |
| Microsoft.CodeAnalysis.CSharp | 4.11.0 | CodeGraphMcp.Core |
| Microsoft.Data.Sqlite | 9.0.0 | CodeGraphMcp.Core |
| Microsoft.Extensions.Logging.Abstractions | 10.0.0 | CodeGraphMcp.Core |

---

## Releases

Pre-built binaries are available on the [Releases page](../../releases):

- Linux x64 — `codegraph-mcp-linux-x64.tar.gz`
- macOS ARM64 — `codegraph-mcp-osx-arm64.tar.gz`
- Windows x64 — `codegraph-mcp-win-x64.zip`

To cut a new release:

```bash
git tag v1.1.0
git push origin v1.1.0
```

---

## License

This project is provided as-is for personal and commercial use.
