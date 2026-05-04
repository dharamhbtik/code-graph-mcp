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
