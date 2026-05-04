using CodeGraphMcp.Core.Domain;

namespace CodeGraphMcp.Core.Scanning;

public sealed record SourceFile(
    string FilePath,
    string RelativePath,
    Language Language,
    string ContentHash,
    DateTimeOffset LastModified
);
