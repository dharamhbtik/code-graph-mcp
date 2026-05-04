# CodeGraphMcp

A local **MCP (Model Context Protocol) server** written in .NET 10 that scans code repositories, builds a **code graph** of symbols and relationships, and exposes it to AI agents (Claude, Cursor, GitHub Copilot) via MCP tool endpoints.

> **Stop feeding your entire codebase to AI.** CodeGraphMcp replaces 200K+ tokens of raw source code with a structured 4K–80K token graph that gives AI editors a complete understanding of your repository architecture, dependencies, and symbol relationships.

---

## 🧠 How It Reduces Token Usage

### The Problem

When AI coding assistants (Claude, Cursor, Copilot) try to understand your codebase, they have two options:

1. **Read every file** — Feeding a 500-file repository to an AI consumes 200,000–500,000+ tokens per request. This is slow, expensive, and often exceeds context windows.
2. **Read nothing** — The AI guesses about your code structure, leading to incorrect assumptions, wrong imports, and broken code suggestions.

### The Solution

CodeGraphMcp creates a **structured code graph** — a compact representation of your entire repository that captures *what matters*:

```
┌──────────────────────────────────────────────────────────────────┐
│  500 files × ~400 tokens each = ~200,000 tokens (raw source)    │
│                           ↓                                      │
│  CodeGraphMcp extracts:                                          │
│  • 250 nodes (classes, functions, interfaces, modules)           │
│  • 180 edges (imports, inherits, calls, references)              │
│                           ↓                                      │
│  Compact graph JSON = ~8,000 tokens (96% reduction)              │
│  System prompt     = ~3,500 tokens (98% reduction)               │
└──────────────────────────────────────────────────────────────────┘
```

### What the AI Gets

Instead of raw source code, the AI receives a structured understanding:

| Information | Without CodeGraphMcp | With CodeGraphMcp |
|-------------|---------------------|-------------------|
| File structure | Must read every file | Instant file map with symbol counts |
| Class hierarchy | Must trace inheritance across files | Direct `inherits` / `implements` edges |
| Dependencies | Must read import statements | Pre-computed `imports` / `dependsOn` graph |
| Entry points | Must guess | Top 5 most-connected nodes, ranked |
| Symbol locations | Must search | Exact file + line number for every symbol |
| XAML ↔ ViewModel bindings | Must infer naming conventions | Explicit `binds` edges |
| Total tokens | 200,000+ | 4,000–80,000 |

### Token Budget Modes

CodeGraphMcp automatically adapts to your token budget:

| Mode | Tokens | What's Included |
|------|--------|-----------------|
| **System Prompt** | ~4,000 | File map, key entry points, symbol index — perfect for initial context |
| **Full Graph** | ~8,000–80,000 | Complete node + edge graph — for deep code understanding |
| **Compressed** | ~2,000–4,000 | Auto-triggered when full graph exceeds budget — file list + top edges only |

### Real-World Token Savings

| Repository Size | Raw Source Tokens | CodeGraphMcp Tokens | Reduction |
|----------------|-------------------|---------------------|-----------|
| 50 files | ~20,000 | ~2,000 | **90%** |
| 200 files | ~80,000 | ~5,000 | **94%** |
| 500 files | ~200,000 | ~12,000 | **94%** |
| 1,000 files | ~400,000 | ~25,000 | **94%** |
| 5,000 files | ~2,000,000 | ~80,000 (capped) | **96%** |

---

## ✨ Features

- **30+ language support** — C#, Java, Kotlin, Swift, C, C++, Objective-C, PHP, Go, Rust, Python, Ruby, Dart, JavaScript, TypeScript, Angular, XAML, SQL, Markdown, HTML, CSS/SCSS, YAML, Shell, and project files
- **Code graph construction** — Nodes (files, classes, methods, functions, interfaces, structs, enums) and edges (calls, imports, inherits, implements, references, binds)
- **SQLite storage** — Persistent, fast graph storage with WAL mode for concurrent access
- **Real-time file watching** — Automatically detects file changes and incrementally patches the graph (800ms debounce)
- **4 MCP tool endpoints** — Query the repository structure from any MCP-compatible AI agent
- **System prompt builder** — Generates a compact, token-budgeted context block for AI agents
- **Self-contained binaries** — Publish for Linux, macOS, or Windows without requiring .NET runtime

---

## 📋 Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) (optional — only needed for JS/TS/Angular parsing with tree-sitter)

---

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
1. Scan the repository for tracked files across all supported languages
2. Parse each file and build the code graph (nodes + edges)
3. Store the graph in `.codegraph/graph.db` (SQLite)
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

---

## 🔧 MCP Tools

The server exposes four tools via the MCP protocol:

### `GetCodeGraph`
Returns the full repository code graph as compact JSON. Supports a `maxTokens` budget parameter (default 80,000). If the graph exceeds the budget, it automatically returns a compressed summary with file lists and top edges only.

**Use case:** Give the AI a complete structural understanding of the codebase in one call.

### `GetFileContext`
Returns all nodes and edges for a specific file path, with BFS traversal up to `hopDepth` hops away (default 2). This surfaces not just the file's own symbols, but also connected classes, interfaces, and dependencies.

**Use case:** When the AI is about to edit a file, give it full context of what that file connects to.

### `GetSymbol`
Searches for a symbol by name (partial match, case-insensitive) and returns definitions, file locations, edges, and code snippets (±5 lines of context).

**Use case:** The AI needs to find where a class, function, or interface is defined and what depends on it.

### `GetSystemPrompt`
Returns a compact markdown context block describing the repository — key entry points, file map, and symbol index — within a 4,000 token budget. Designed to be included in the system prompt at the start of every conversation.

**Use case:** Give every AI conversation baseline knowledge of the repository structure without burning tokens.

---

## 🗣️ Supported Languages

### Dedicated Parsers (Deep Analysis)

| Language | Parser Engine | Nodes Extracted |
|----------|--------------|----------------|
| **C#** | Roslyn (`Microsoft.CodeAnalysis.CSharp`) | Files, namespaces, classes, interfaces, enums, structs, records, methods, properties, inheritance |
| **XAML** | `System.Xml.Linq` | Files, XamlView (x:Class), XamlResource (x:Name), ViewModel bindings |
| **JavaScript** | Node.js regex | Files, functions, classes, imports, Angular decorators |
| **TypeScript** | Node.js regex | Files, functions, classes, imports |
| **Angular** | Node.js regex | Files, components, injectables, NgModules |
| **.csproj/.sln** | `System.Xml.Linq` | Files, package references, project references |
| **Markdown** | Line parser | Files, document sections (headings) |

### Regex-Based Parsers (Structural Analysis)

| Language | Extensions | Extracts |
|----------|-----------|----------|
| **Java** | `.java` | Classes, interfaces, enums, records, methods, imports, packages, inheritance |
| **Kotlin** | `.kt`, `.kts` | Classes, interfaces, enums, objects, functions, imports, packages |
| **Swift** | `.swift` | Classes, structs, protocols, enums, functions, imports |
| **C** | `.c`, `.h` | Structs, enums, functions, `#include` |
| **C++** | `.cpp`, `.cc`, `.cxx`, `.hpp` | Classes, structs, enums, functions, namespaces, `#include`, inheritance |
| **Objective-C** | `.m`, `.mm` | Interfaces, implementations, protocols, methods, `#import` |
| **PHP** | `.php` | Classes, interfaces, traits, enums, functions, namespaces, `use` |
| **Go** | `.go` | Structs, interfaces, functions, packages, imports |
| **Rust** | `.rs` | Structs, enums, traits, functions, modules, `use` |
| **Python** | `.py` | Classes, functions, imports |
| **Ruby** | `.rb` | Classes, modules, methods, `require` |
| **Dart** | `.dart` | Classes, enums, mixins, functions, imports |
| **SQL** | `.sql` | Tables (`CREATE TABLE`), procedures, views, functions |
| **HTML** | `.html`, `.htm` | Script/style references |
| **CSS/SCSS** | `.css`, `.scss` | `@import` references |
| **Shell** | `.sh`, `.bash`, `.zsh` | Functions, `source` includes |
| **YAML** | `.yaml`, `.yml` | Top-level configuration keys |
| **JSON** | `.json` | File tracking (config detection) |

---

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

### GitHub Copilot (VS Code)

GitHub Copilot supports MCP servers via the VS Code MCP extension. Add to your VS Code `settings.json`:

```json
{
  "mcp": {
    "servers": {
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
}
```

Or use the published binary:

```json
{
  "mcp": {
    "servers": {
      "codegraph": {
        "command": "/absolute/path/to/publish/CodeGraphMcp",
        "args": ["${workspaceFolder}"]
      }
    }
  }
}
```

> **Tip:** After configuring, Copilot Chat will automatically discover the MCP tools. You can ask it to use `GetSystemPrompt` at the start of conversations for instant repository context.

### Windsurf / Cline / Other MCP Clients

Any MCP-compatible client can connect using the stdio transport. The command format is the same:

```bash
/path/to/CodeGraphMcp /path/to/your/repo
```

The server communicates via JSON-RPC on stdin/stdout (MCP protocol) and logs to stderr.

---

## 🏗️ Architecture

```
CodeGraphMcp/
├── src/
│   ├── CodeGraphMcp/                    # Main executable + MCP server
│   │   ├── Program.cs                   # Entry point + graceful shutdown
│   │   ├── Mcp/Tools/                   # 4 MCP tool endpoints
│   │   ├── Watcher/                     # FileSystemWatcher + 800ms debounce
│   │   └── Startup/                     # DI registration for 25+ parsers
│   ├── CodeGraphMcp.Core/              # Domain + graph logic
│   │   ├── Domain/                      # CodeNode, CodeEdge, Language, NodeKind, RelationKind
│   │   ├── Scanning/                    # Repository file discovery + hashing
│   │   ├── Parsing/                     # 25+ language parsers (Roslyn, regex, Node.js)
│   │   ├── Graph/                       # SQLite graph store (WAL mode)
│   │   ├── Orchestration/               # Full build + incremental rebuild
│   │   ├── Context/                     # System prompt builder (4K token budget)
│   │   └── Utilities/                   # Language detection, token estimation
│   └── CodeGraphMcp.Tests/             # Integration tests (5 tests)
└── scripts/
    └── parse-js.mjs                     # Node.js JS/TS parser script
```

### How It Works

```
                  ┌─────────────┐
                  │  Repository │
                  └──────┬──────┘
                         │ scan
                  ┌──────▼──────┐
                  │  Scanner    │  Discovers tracked files, computes hashes
                  └──────┬──────┘
                         │ files
              ┌──────────▼──────────┐
              │  Language Parsers   │  25+ parsers extract nodes + edges
              │  (C#, Java, Go...) │  from each file concurrently (8 max)
              └──────────┬──────────┘
                         │ nodes, edges
                  ┌──────▼──────┐
                  │ Graph Store │  SQLite DB with WAL mode
                  │ (SQLite)    │  Upsert, query, delete by file
                  └──────┬──────┘
                         │
          ┌──────────────┼──────────────┐
          │              │              │
   ┌──────▼──────┐ ┌────▼────┐ ┌──────▼──────┐
   │ MCP Tools   │ │ Watcher │ │  System     │
   │ (4 tools)   │ │ (800ms  │ │  Prompt     │
   │ stdio JSON  │ │ debounce)│ │  Builder    │
   └─────────────┘ └─────────┘ └─────────────┘
```

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

---

## 🔄 How AI Editors Benefit

### Before CodeGraphMcp

```
Developer: "Add a new payment method to the checkout flow"

AI: *reads 50 files trying to understand the codebase* (100K+ tokens)
    *misses the PaymentService class in a different module*
    *doesn't know about the IPaymentGateway interface*
    *generates code with wrong imports and missing dependencies*
```

### After CodeGraphMcp

```
Developer: "Add a new payment method to the checkout flow"

AI: *calls GetSystemPrompt* (3,500 tokens → knows entire repo structure)
    *calls GetSymbol("Payment")* → finds PaymentService, IPaymentGateway, PaymentController
    *calls GetFileContext("PaymentService.cs")* → sees all connected types + edges
    *generates correct code with proper imports, interfaces, and dependency injection*
```

### Key Benefits

1. **Faster responses** — AI doesn't need to read hundreds of files; the graph provides instant structural context
2. **Better accuracy** — The AI knows about inheritance, imports, and dependencies before writing code
3. **Lower cost** — 90-96% token reduction means significantly lower API costs for cloud-hosted AI
4. **Always current** — The file watcher keeps the graph up-to-date as you code, with 800ms debounce
5. **Works offline** — Everything runs locally; no data leaves your machine
6. **Universal** — Works with any MCP-compatible AI agent: Claude, Cursor, Copilot, Windsurf, Cline

---

## 💡 Prompting Guide & Best Practices

Once you have configured CodeGraphMcp with your AI editor (Cursor, Copilot, Claude), you can use it to perform complex, repository-wide tasks without blowing up your token limits. Here are the best ways to prompt the AI using the graph.

### 1. The "Start of Conversation" Anchor
Always give the AI the bird's-eye view first. This allows it to understand the architecture before writing a single line of code.

**Prompt:**
> "Please call the `GetSystemPrompt` tool to understand the structure of this repository. I want to build a new feature."

*Why it works:* `GetSystemPrompt` returns a compact (~4,000 token) index of your entry points, file map, and core symbols. The AI instantly learns your naming conventions, what languages are used, and where the core modules live.

### 2. Large-Scale Refactoring & Migrations (e.g., Xamarin to MAUI)
When migrating codebases, the AI needs to know how UI views map to logic, and where platform-specific code lives.

**Prompt:**
> "We are migrating this app from Xamarin.Forms to .NET MAUI.
> 1. Use `GetCodeGraph` to find all XAML files and their corresponding ViewModels.
> 2. Use `GetFileContext` on `App.xaml.cs` to trace the dependency injection setup.
> 3. Generate a migration plan mapping the old `DependencyService` calls to the new MAUI `MauiProgram.cs` DI container."

*Why it works:* Instead of opening 30 files manually, the AI queries the graph for `binds` edges (View ↔ ViewModel) and `dependsOn` edges, allowing it to accurately update references.

### 3. Adding a New Feature (Vertical Slice)
When adding a feature that touches the database, API, and UI.

**Prompt:**
> "I need to add a 'User Profile' feature. 
> Use `GetSymbol` to find the `UserRepository` interface and the `UserController`. 
> Then, use `GetFileContext` to see what services the `UserController` depends on. 
> Finally, write the code for the new `UpdateProfile` endpoint, ensuring you follow the existing dependency injection patterns."

*Why it works:* `GetSymbol` instantly locates the files regardless of folder structure. The AI sees the edges attached to `UserController` and knows exactly which services to inject.

### 4. Debugging Cross-Module Issues
When a bug spans multiple files (e.g., an event fired in the UI but handled in a background service).

**Prompt:**
> "There is a bug where the 'Order Placed' event is not updating the inventory. 
> 1. Use `GetSymbol` to find the `OrderPlacedEvent`.
> 2. Look at the edges to see what classes implement or handle this event.
> 3. Read the relevant files and suggest a fix."

*Why it works:* The graph contains `references` and `implements` edges. The AI doesn't need to text-search the entire repo; it simply traverses the graph from the Event node to the Handler node.

### 5. Onboarding to a New Codebase
When you join a new project and need to understand the flow.

**Prompt:**
> "I am new to this codebase. Use `GetCodeGraph` and `GetSystemPrompt` to analyze the architecture. Give me a 5-bullet summary of how the frontend communicates with the backend, and list the top 3 most important classes I should read first."

*Why it works:* CodeGraphMcp ranks nodes by connectivity (number of edges). The AI easily identifies the core orchestration classes and controllers without blindly reading utility files.

---

## 📦 NuGet Dependencies

| Package | Version | Project |
|---------|---------|---------|
| `ModelContextProtocol` | 0.2.0-preview.1 | CodeGraphMcp |
| `Microsoft.Extensions.Hosting` | 10.0.0 | CodeGraphMcp |
| `Microsoft.Extensions.Logging.Console` | 10.0.0 | CodeGraphMcp |
| `Microsoft.CodeAnalysis.CSharp` | 4.11.0 | CodeGraphMcp.Core |
| `Microsoft.Data.Sqlite` | 9.0.0 | CodeGraphMcp.Core |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.0 | CodeGraphMcp.Core |

---

## 🏷️ Releases

Pre-built binaries are available for every tagged release on the [Releases page](../../releases). Supported platforms:

- **Linux x64** (`codegraph-mcp-linux-x64.tar.gz`)
- **macOS ARM64** (`codegraph-mcp-osx-arm64.tar.gz`)
- **Windows x64** (`codegraph-mcp-win-x64.zip`)

To create a new release:

```bash
git tag v1.0.0
git push origin v1.0.0
```

---

## 📄 License

This project is provided as-is for personal and commercial use.
