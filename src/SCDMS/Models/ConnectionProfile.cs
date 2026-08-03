namespace Scdms.Models;

/// <summary>
/// Represents a saved non-sensitive connection profile for fast reconnect workflows.
/// </summary>
public sealed record class ConnectionProfile
{
    public required string Name { get; init; }

    public required ViewerConnectionMode ConnectionMode { get; init; }

    public string? LocalDatabasePath { get; init; }

    public DatabaseStorageMode LocalStorageMode { get; init; } = DatabaseStorageMode.Directory;

    public bool LocalReadOnly { get; init; }

    public string? ServerHost { get; init; }

    public int ServerPort { get; init; } = 5001;

    public bool ServerUseSsl { get; init; } = true;

    public bool ServerPreferHttp3 { get; init; } = true;

    public string? ServerDatabase { get; init; }

    public string? ServerUsername { get; init; }

    public DateTimeOffset LastUsedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string DisplayTarget => ConnectionMode == ViewerConnectionMode.Server
        ? $"{ServerHost}:{ServerPort}/{ServerDatabase}"
        : LocalDatabasePath ?? string.Empty;
}

public enum ViewerConnectionMode
{
    Local = 0,
    Server = 1
}

public enum DatabaseStorageMode
{
    Directory = 0,
    SingleFile = 1
}
