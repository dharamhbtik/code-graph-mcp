namespace CodeGraphMcp.Core.Graph;

internal static class GraphStoreSchema
{
    internal const string CreateNodes = """
        CREATE TABLE IF NOT EXISTS nodes (
            id         TEXT PRIMARY KEY,
            kind       TEXT NOT NULL,
            name       TEXT NOT NULL,
            full_name  TEXT NOT NULL,
            file_path  TEXT NOT NULL,
            language   TEXT NOT NULL,
            start_line INTEGER NOT NULL DEFAULT 0,
            end_line   INTEGER NOT NULL DEFAULT 0,
            summary    TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_nodes_file ON nodes(file_path);
        CREATE INDEX IF NOT EXISTS idx_nodes_name ON nodes(name COLLATE NOCASE);
        CREATE INDEX IF NOT EXISTS idx_nodes_full ON nodes(full_name COLLATE NOCASE);
        """;

    internal const string CreateEdges = """
        CREATE TABLE IF NOT EXISTS edges (
            id         TEXT PRIMARY KEY,
            source_id  TEXT NOT NULL,
            target_id  TEXT NOT NULL,
            kind       TEXT NOT NULL,
            weight     REAL NOT NULL DEFAULT 1.0
        );
        CREATE INDEX IF NOT EXISTS idx_edges_source ON edges(source_id);
        CREATE INDEX IF NOT EXISTS idx_edges_target ON edges(target_id);
        """;

    internal const string CreateMeta = """
        CREATE TABLE IF NOT EXISTS meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """;
}
