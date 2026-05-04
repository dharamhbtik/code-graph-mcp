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
