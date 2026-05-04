using System.Collections.Concurrent;
using System.Threading.Channels;
using CodeGraphMcp.Core.Orchestration;
using CodeGraphMcp.Core.Utilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Watcher;

public sealed class FileChangeWatcher : IHostedService, IDisposable
{
    private readonly GraphOrchestrator _orchestrator;
    private readonly ILogger<FileChangeWatcher> _logger;
    private readonly Channel<FileChangeEvent> _processChannel;

    private FileSystemWatcher? _watcher;
    private Task? _consumerTask;
    private CancellationTokenSource? _cts;

    // Debounce state: filePath → pending CancellationTokenSource
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new();

    // Public channel that MCP tool handlers can subscribe to
    public ChannelReader<FileChangeEvent> Events => _processChannel.Reader;

    public FileChangeWatcher(GraphOrchestrator orchestrator, ILogger<FileChangeWatcher> logger)
    {
        _orchestrator   = orchestrator;
        _logger         = logger;
        _processChannel = Channel.CreateUnbounded<FileChangeEvent>(
            new UnboundedChannelOptions { SingleReader = true });
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _consumerTask = ConsumeAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public void Watch(string rootPath)
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        _watcher.Changed += (_, e) => Debounce(e.FullPath, WatcherChangeTypes.Changed);
        _watcher.Created += (_, e) => Debounce(e.FullPath, WatcherChangeTypes.Created);
        _watcher.Deleted += (_, e) => Debounce(e.FullPath, WatcherChangeTypes.Deleted);
        _watcher.Renamed += (_, e) => Debounce(e.FullPath, WatcherChangeTypes.Renamed, e.OldFullPath);
        _watcher.Error   += (_, e) => _logger.LogError(e.GetException(), "FileSystemWatcher error");

        _logger.LogInformation("Watching {Root}", rootPath);
    }

    private void Debounce(string filePath, WatcherChangeTypes changeType, string? oldPath = null)
    {
        if (!LanguageDetector.IsTracked(filePath)) return;

        // Cancel any pending timer for this file and start a fresh 800ms window
        if (_pending.TryRemove(filePath, out var prev)) prev.Cancel();

        var cts = new CancellationTokenSource();
        _pending[filePath] = cts;

        Task.Delay(800, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            _pending.TryRemove(filePath, out _);
            var evt = new FileChangeEvent(filePath, changeType, oldPath);
            _processChannel.Writer.TryWrite(evt);
        }, TaskScheduler.Default);
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _processChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    _logger.LogInformation("Processing {Change}: {File}", evt.ChangeType, evt.FilePath);

                    switch (evt.ChangeType)
                    {
                        case WatcherChangeTypes.Changed:
                        case WatcherChangeTypes.Created:
                            await _orchestrator.RebuildFileAsync(evt.FilePath, ct);
                            break;

                        case WatcherChangeTypes.Deleted:
                            _orchestrator.Store.RemoveFileNodes(evt.FilePath);
                            break;

                        case WatcherChangeTypes.Renamed when evt.OldPath is not null:
                            await _orchestrator.RenameFileAsync(evt.OldPath, evt.FilePath, ct);
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error processing file change for {File}", evt.FilePath);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        _processChannel.Writer.TryComplete();
        if (_consumerTask is not null)
            await _consumerTask.ConfigureAwait(false);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _cts?.Dispose();
    }
}
