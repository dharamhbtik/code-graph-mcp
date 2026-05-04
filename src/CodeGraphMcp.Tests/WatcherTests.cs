using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Core.Orchestration;
using CodeGraphMcp.Core.Parsing;
using CodeGraphMcp.Core.Scanning;
using CodeGraphMcp.Watcher;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CodeGraphMcp.Tests;

public sealed class WatcherTests : IDisposable
{
    private readonly string _tempRepo = Path.Combine(Path.GetTempPath(), $"cg_repo_{Guid.NewGuid():N}");
    private readonly string _dbPath   = Path.Combine(Path.GetTempPath(), $"cg_test_{Guid.NewGuid():N}.db");
    private readonly GraphStore _store;

    public WatcherTests()
    {
        Directory.CreateDirectory(_tempRepo);
        _store = new GraphStore(_dbPath, NullLogger<GraphStore>.Instance);
    }

    [Fact]
    public async Task FileChange_TriggersGraphUpdateWithin2Seconds()
    {
        // Arrange
        var csFile  = Path.Combine(_tempRepo, "Test.cs");
        await File.WriteAllTextAsync(csFile, "namespace T; public class A {}");

        var runner   = new NodeProcessRunner(NullLogger<NodeProcessRunner>.Instance);
        var parsers  = new ILanguageParser[] { new CSharpParser(NullLogger<CSharpParser>.Instance) };
        var orch     = new GraphOrchestrator(
            new RepositoryScanner(NullLogger<RepositoryScanner>.Instance),
            _store, parsers, NullLogger<GraphOrchestrator>.Instance);
        await orch.BuildAsync(_tempRepo);

        var watcher = new FileChangeWatcher(orch, NullLogger<FileChangeWatcher>.Instance);
        await watcher.StartAsync(CancellationToken.None);
        watcher.Watch(_tempRepo);

        // Act — modify the file
        await Task.Delay(200);
        await File.WriteAllTextAsync(csFile, "namespace T; public class A {} public class B {}");

        // Assert — wait up to 2 seconds for the event
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        FileChangeEvent? received = null;
        try
        {
            received = await watcher.Events.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) { }

        received.Should().NotBeNull();
        received!.FilePath.Should().Be(csFile);

        await watcher.StopAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_tempRepo)) Directory.Delete(_tempRepo, recursive: true);
    }
}
