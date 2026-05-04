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

        // Register MCP server — discover all [McpServerToolType] tools in this assembly
        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(GetCodeGraphTool).Assembly);
    })
    .Build();

// Graceful shutdown
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
};

// ── Initial full graph build ─────────────────────────────────────────────────
var orchestrator = host.Services.GetRequiredService<GraphOrchestrator>();
await orchestrator.BuildAsync(rootPath);

// Health check on startup
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var (nodes, edges) = orchestrator.Store.GetStats();
var tokens         = orchestrator.Store.LoadGraph(rootPath).EstimateTokenCount();
logger.LogInformation(
    "Graph ready: {Nodes} nodes, {Edges} edges, ~{Tokens} tokens",
    nodes, edges, tokens);

// ── Start file watcher ───────────────────────────────────────────────────────
var watcher = host.Services.GetRequiredService<FileChangeWatcher>();
watcher.Watch(rootPath);

// ── Run MCP server (blocks until process exits) ───────────────────────────────
await host.RunAsync();
