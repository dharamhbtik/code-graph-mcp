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
