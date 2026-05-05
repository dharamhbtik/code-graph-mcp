using CodeGraphMcp.Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Graph;

public sealed class GraphStore : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ILogger<GraphStore> _logger;
    private readonly object _dbLock = new();

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
        lock (_dbLock)
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
    }

    public void UpsertEdge(CodeEdge e)
    {
        lock (_dbLock)
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
    }

    /// <summary>
    /// Bulk-upsert a set of nodes and edges in a single transaction.
    /// Thread-safe and much faster than individual upserts during parallel builds.
    /// </summary>
    public void BulkUpsert(IReadOnlyList<CodeNode> nodes, IReadOnlyList<CodeEdge> edges)
    {
        lock (_dbLock)
        {
            using var tx = _conn.BeginTransaction();
            try
            {
                foreach (var n in nodes)
                {
                    using var cmd = _conn.CreateCommand();
                    cmd.Transaction = tx;
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

                foreach (var e in edges)
                {
                    using var cmd = _conn.CreateCommand();
                    cmd.Transaction = tx;
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

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }

    // ── Delete by file ───────────────────────────────────────────────────────

    public void RemoveFileNodes(string filePath)
    {
        lock (_dbLock)
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
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    public CodeGraph LoadGraph(string rootPath)
    {
        lock (_dbLock)
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
    }

    public List<CodeNode> SearchNodes(string query)
    {
        lock (_dbLock)
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
    }

    public List<CodeEdge> GetEdgesForNode(string nodeId)
    {
        lock (_dbLock)
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
    }

    public List<CodeNode> GetNodesByFile(string filePath)
    {
        lock (_dbLock)
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
    }

    public (int nodes, int edges) GetStats()
    {
        lock (_dbLock)
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
    }

    /// <summary>
    /// Resolves a relative or partial file path against stored absolute paths.
    /// Returns the matching absolute path, or the original if no match is found.
    /// </summary>
    public string ResolveFilePath(string inputPath)
    {
        // If the file exists at the given path, it's already absolute
        if (File.Exists(inputPath))
            return Path.GetFullPath(inputPath);

        lock (_dbLock)
        {
            using var cmd = _conn.CreateCommand();
            // Suffix match: find stored paths ending with the given relative path
            cmd.CommandText = """
                SELECT file_path FROM nodes
                WHERE file_path LIKE @suffix COLLATE NOCASE
                GROUP BY file_path
                LIMIT 1
                """;
            // Normalize separators and ensure leading separator for suffix match
            var normalized = inputPath.Replace('\\', '/');
            cmd.Parameters.AddWithValue("@suffix", $"%/{normalized}");
            var result = cmd.ExecuteScalar();
            return result as string ?? inputPath;
        }
    }

    /// <summary>
    /// Batch-loads edges for multiple node IDs in a single query.
    /// Eliminates the N+1 pattern of calling GetEdgesForNode per node.
    /// </summary>
    public List<CodeEdge> GetEdgesForNodes(IReadOnlyCollection<string> nodeIds)
    {
        if (nodeIds.Count == 0) return [];

        lock (_dbLock)
        {
            var edges = new List<CodeEdge>();
            // SQLite parameter limit is typically 999; chunk if needed
            foreach (var chunk in ChunkCollection(nodeIds, 500))
            {
                using var cmd = _conn.CreateCommand();
                var placeholders = new List<string>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    var paramName = $"@id{i}";
                    placeholders.Add(paramName);
                    cmd.Parameters.AddWithValue(paramName, chunk[i]);
                }
                var inClause = string.Join(",", placeholders);
                cmd.CommandText = $"SELECT id,source_id,target_id,kind,weight FROM edges WHERE source_id IN ({inClause}) OR target_id IN ({inClause})";
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
            }
            return edges;
        }
    }

    /// <summary>
    /// Batch-loads nodes by their IDs in a single query.
    /// Used to resolve SHA hash IDs to human-readable node data.
    /// </summary>
    public List<CodeNode> GetNodesByIds(IReadOnlyCollection<string> nodeIds)
    {
        if (nodeIds.Count == 0) return [];

        lock (_dbLock)
        {
            var nodes = new List<CodeNode>();
            foreach (var chunk in ChunkCollection(nodeIds, 500))
            {
                using var cmd = _conn.CreateCommand();
                var placeholders = new List<string>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    var paramName = $"@id{i}";
                    placeholders.Add(paramName);
                    cmd.Parameters.AddWithValue(paramName, chunk[i]);
                }
                var inClause = string.Join(",", placeholders);
                cmd.CommandText = $"SELECT id,kind,name,full_name,file_path,language,start_line,end_line,summary FROM nodes WHERE id IN ({inClause})";
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
            }
            return nodes;
        }
    }

    private static List<List<T>> ChunkCollection<T>(IReadOnlyCollection<T> source, int chunkSize)
    {
        var chunks = new List<List<T>>();
        var current = new List<T>(chunkSize);
        foreach (var item in source)
        {
            current.Add(item);
            if (current.Count >= chunkSize)
            {
                chunks.Add(current);
                current = new List<T>(chunkSize);
            }
        }
        if (current.Count > 0) chunks.Add(current);
        return chunks;
    }

    public void Dispose() => _conn.Dispose();
}
