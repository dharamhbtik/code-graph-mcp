using CodeGraphMcp.Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Graph;

public sealed class GraphStore : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ILogger<GraphStore> _logger;

    public GraphStore(string dbPath, ILogger<GraphStore> logger)
    {
        _logger = logger;
        _conn   = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        
        using var wal = _conn.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        wal.ExecuteNonQuery();

        InitialiseSchema();
        logger.LogInformation("GraphStore opened at {Path}", dbPath);
    }

    private void InitialiseSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = GraphStoreSchema.CreateNodes
                        + GraphStoreSchema.CreateEdges
                        + GraphStoreSchema.CreateMeta;
        cmd.ExecuteNonQuery();
    }

    // ── Upsert ──────────────────────────────────────────────────────────────

    public void UpsertNode(CodeNode n)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO nodes (id, kind, name, full_name, file_path, language, start_line, end_line, summary)
            VALUES (@id, @kind, @name, @full, @file, @lang, @sl, @el, @sum)
            ON CONFLICT(id) DO UPDATE SET
                kind=excluded.kind, name=excluded.name, full_name=excluded.full_name,
                start_line=excluded.start_line, end_line=excluded.end_line, summary=excluded.summary;
            """;
        cmd.Parameters.AddWithValue("@id",   n.Id);
        cmd.Parameters.AddWithValue("@kind", n.Kind.ToString());
        cmd.Parameters.AddWithValue("@name", n.Name);
        cmd.Parameters.AddWithValue("@full", n.FullName);
        cmd.Parameters.AddWithValue("@file", n.FilePath);
        cmd.Parameters.AddWithValue("@lang", n.Language.ToString());
        cmd.Parameters.AddWithValue("@sl",   n.StartLine);
        cmd.Parameters.AddWithValue("@el",   n.EndLine);
        cmd.Parameters.AddWithValue("@sum",  (object?)n.Summary ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void UpsertEdge(CodeEdge e)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO edges (id, source_id, target_id, kind, weight)
            VALUES (@id, @src, @tgt, @kind, @w)
            ON CONFLICT(id) DO UPDATE SET kind=excluded.kind, weight=excluded.weight;
            """;
        cmd.Parameters.AddWithValue("@id",   e.Id);
        cmd.Parameters.AddWithValue("@src",  e.SourceId);
        cmd.Parameters.AddWithValue("@tgt",  e.TargetId);
        cmd.Parameters.AddWithValue("@kind", e.Kind.ToString());
        cmd.Parameters.AddWithValue("@w",    e.Weight);
        cmd.ExecuteNonQuery();
    }

    // ── Delete by file ───────────────────────────────────────────────────────

    public void RemoveFileNodes(string filePath)
    {
        // Get all node ids for this file first
        var ids = new List<string>();
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = "SELECT id FROM nodes WHERE file_path = @fp";
            sel.Parameters.AddWithValue("@fp", filePath);
            using var r = sel.ExecuteReader();
            while (r.Read()) ids.Add(r.GetString(0));
        }

        if (ids.Count == 0) return;

        // Remove edges where source or target is a node from this file
        foreach (var id in ids)
        {
            using var delEdge = _conn.CreateCommand();
            delEdge.CommandText = "DELETE FROM edges WHERE source_id = @id OR target_id = @id";
            delEdge.Parameters.AddWithValue("@id", id);
            delEdge.ExecuteNonQuery();
        }

        // Remove nodes
        using var delNode = _conn.CreateCommand();
        delNode.CommandText = "DELETE FROM nodes WHERE file_path = @fp";
        delNode.Parameters.AddWithValue("@fp", filePath);
        delNode.ExecuteNonQuery();

        _logger.LogDebug("Removed {Count} nodes for {File}", ids.Count, filePath);
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    public CodeGraph LoadGraph(string rootPath)
    {
        var graph = new CodeGraph { RootPath = rootPath, GeneratedAt = DateTimeOffset.UtcNow };

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id,kind,name,full_name,file_path,language,start_line,end_line,summary FROM nodes";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var node = new CodeNode
                {
                    Id        = r.GetString(0),
                    Kind      = Enum.Parse<NodeKind>(r.GetString(1)),
                    Name      = r.GetString(2),
                    FullName  = r.GetString(3),
                    FilePath  = r.GetString(4),
                    Language  = Enum.Parse<Language>(r.GetString(5)),
                    StartLine = r.GetInt32(6),
                    EndLine   = r.GetInt32(7),
                    Summary   = r.IsDBNull(8) ? null : r.GetString(8),
                };
                graph.Nodes[node.Id] = node;
            }
        }

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id,source_id,target_id,kind,weight FROM edges";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                graph.Edges.Add(new CodeEdge
                {
                    Id       = r.GetString(0),
                    SourceId = r.GetString(1),
                    TargetId = r.GetString(2),
                    Kind     = Enum.Parse<RelationKind>(r.GetString(3)),
                    Weight   = (float)r.GetDouble(4),
                });
            }
        }

        return graph;
    }

    public List<CodeNode> SearchNodes(string query)
    {
        var results = new List<CodeNode>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id,kind,name,full_name,file_path,language,start_line,end_line,summary
            FROM nodes
            WHERE name LIKE @q COLLATE NOCASE OR full_name LIKE @q COLLATE NOCASE
            LIMIT 50
            """;
        cmd.Parameters.AddWithValue("@q", $"%{query}%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new CodeNode
            {
                Id        = r.GetString(0),
                Kind      = Enum.Parse<NodeKind>(r.GetString(1)),
                Name      = r.GetString(2),
                FullName  = r.GetString(3),
                FilePath  = r.GetString(4),
                Language  = Enum.Parse<Language>(r.GetString(5)),
                StartLine = r.GetInt32(6),
                EndLine   = r.GetInt32(7),
                Summary   = r.IsDBNull(8) ? null : r.GetString(8),
            });
        }
        return results;
    }

    public List<CodeEdge> GetEdgesForNode(string nodeId)
    {
        var edges = new List<CodeEdge>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id,source_id,target_id,kind,weight FROM edges
            WHERE source_id = @id OR target_id = @id
            """;
        cmd.Parameters.AddWithValue("@id", nodeId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            edges.Add(new CodeEdge
            {
                Id       = r.GetString(0),
                SourceId = r.GetString(1),
                TargetId = r.GetString(2),
                Kind     = Enum.Parse<RelationKind>(r.GetString(3)),
                Weight   = (float)r.GetDouble(4),
            });
        }
        return edges;
    }

    public List<CodeNode> GetNodesByFile(string filePath)
    {
        var nodes = new List<CodeNode>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id,kind,name,full_name,file_path,language,start_line,end_line,summary
            FROM nodes WHERE file_path = @fp
            """;
        cmd.Parameters.AddWithValue("@fp", filePath);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            nodes.Add(new CodeNode
            {
                Id        = r.GetString(0),
                Kind      = Enum.Parse<NodeKind>(r.GetString(1)),
                Name      = r.GetString(2),
                FullName  = r.GetString(3),
                FilePath  = r.GetString(4),
                Language  = Enum.Parse<Language>(r.GetString(5)),
                StartLine = r.GetInt32(6),
                EndLine   = r.GetInt32(7),
                Summary   = r.IsDBNull(8) ? null : r.GetString(8),
            });
        }
        return nodes;
    }

    public (int nodes, int edges) GetStats()
    {
        int n, e;
        using (var c = _conn.CreateCommand())
        {
            c.CommandText = "SELECT COUNT(*) FROM nodes";
            n = Convert.ToInt32(c.ExecuteScalar());
        }
        using (var c = _conn.CreateCommand())
        {
            c.CommandText = "SELECT COUNT(*) FROM edges";
            e = Convert.ToInt32(c.ExecuteScalar());
        }
        return (n, e);
    }

    public void Dispose() => _conn.Dispose();
}
