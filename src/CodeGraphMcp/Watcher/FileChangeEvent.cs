namespace CodeGraphMcp.Watcher;

public sealed record FileChangeEvent(
    string FilePath,
    WatcherChangeTypes ChangeType,
    string? OldPath = null   // set for Renamed events
);
