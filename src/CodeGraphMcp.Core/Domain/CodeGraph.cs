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
