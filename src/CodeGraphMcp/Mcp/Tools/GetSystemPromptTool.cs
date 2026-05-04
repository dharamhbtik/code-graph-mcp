using System.ComponentModel;
using CodeGraphMcp.Core.Context;
using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Startup;
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
