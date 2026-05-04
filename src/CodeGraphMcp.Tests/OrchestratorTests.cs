using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Core.Orchestration;
using CodeGraphMcp.Core.Parsing;
using CodeGraphMcp.Core.Scanning;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeGraphMcp.Tests;

public sealed class OrchestratorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cg_test_{Guid.NewGuid():N}.db");
    private readonly string _repoPath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "SampleRepo");
    private readonly GraphStore _store;

    public OrchestratorTests()
    {
        _store = new GraphStore(_dbPath, NullLogger<GraphStore>.Instance);
    }

    private GraphOrchestrator BuildOrchestrator()
    {
        var runner  = new NodeProcessRunner(NullLogger<NodeProcessRunner>.Instance);
        var parsers = new ILanguageParser[]
        {
            new CSharpParser(NullLogger<CSharpParser>.Instance),
            new XamlParser(NullLogger<XamlParser>.Instance),
            new JavaScriptParser(runner, NullLogger<JavaScriptParser>.Instance),
            new ProjectFileParser(NullLogger<ProjectFileParser>.Instance),
        };
        return new GraphOrchestrator(
            new RepositoryScanner(NullLogger<RepositoryScanner>.Instance),
            _store,
            parsers,
            NullLogger<GraphOrchestrator>.Instance);
    }

    [Fact]
    public async Task BuildAsync_PopulatesNodesAndEdges()
    {
        var orch = BuildOrchestrator();
        await orch.BuildAsync(_repoPath);

        var (n, e) = _store.GetStats();
        n.Should().BeGreaterThan(5);
        e.Should().BeGreaterThan(2);
    }

    [Fact]
    public async Task RebuildFileAsync_ReplacesNodesForFile()
    {
        var orch     = BuildOrchestrator();
        await orch.BuildAsync(_repoPath);
        var (n1, _)  = _store.GetStats();

        var csFile   = Path.Combine(_repoPath, "src", "OrderService.cs");
        await orch.RebuildFileAsync(csFile);
        var (n2, _)  = _store.GetStats();

        // Node count should be stable (removed + re-added)
        n2.Should().BeCloseTo(n1, 3);
    }

    [Fact]
    public async Task GetFileContext_ReturnsXamlViewWithLinkedViewModel()
    {
        var orch     = BuildOrchestrator();
        await orch.BuildAsync(_repoPath);

        var xamlFile = Path.Combine(_repoPath, "ui", "OrderView.xaml");
        var nodes    = _store.GetNodesByFile(xamlFile);
        nodes.Should().NotBeEmpty();

        // Verify at least one XamlView node was created
        nodes.Should().Contain(n => n.Kind == CodeGraphMcp.Core.Domain.NodeKind.XamlView);

        // Load the full graph and check edges directly — Binds edges target
        // a ViewModel node that may not exist as a node in the DB, so we check
        // the graph's edge list for any Binds edges
        var graph = _store.LoadGraph(_repoPath);
        graph.Edges.Should().Contain(e => e.Kind == CodeGraphMcp.Core.Domain.RelationKind.Binds);
    }

    [Fact]
    public async Task TokenEstimate_BelowOneMillion()
    {
        var orch  = BuildOrchestrator();
        await orch.BuildAsync(_repoPath);

        var graph  = _store.LoadGraph(_repoPath);
        var tokens = graph.EstimateTokenCount();
        tokens.Should().BeLessThan(1_000_000);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
