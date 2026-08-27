using System.Collections.Concurrent;
using SharpCoreDB.Data.Provider;
using Scdms.Models;

namespace Scdms.Services;

/// <summary>
/// Singleton holder for active transaction contexts across all browser sessions.
/// Transactions span multiple HTTP requests (Begin → queries → Commit/Rollback), and each
/// request has its own DI scope, so the live connection/transaction objects must live in a
/// process-wide store keyed by session id. Stale entries (sessions that expired or vanished)
/// are swept opportunistically so open database handles are eventually released.
/// </summary>
public sealed class TransactionContextStore : IAsyncDisposable
{
    /// <summary>
    /// Idle time after which a transaction context is considered abandoned. The ASP.NET session
    /// timeout is 20 minutes, so a context idle for 6 hours belongs to a dead session.
    /// </summary>
    private static readonly TimeSpan MaxIdle = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<string, TransactionContext> _contexts = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the transaction context for a session, touching it to reflect recent activity.
    /// </summary>
    public bool TryGet(string sessionId, out TransactionContext? context)
    {
        if (_contexts.TryGetValue(sessionId, out var found))
        {
            found.Touch();
            context = found;
            return true;
        }

        context = null;
        return false;
    }

    /// <summary>
    /// Registers a transaction context for a session. Returns false when the session already
    /// has an active transaction.
    /// </summary>
    public bool TryAdd(string sessionId, TransactionContext context) => _contexts.TryAdd(sessionId, context);

    /// <summary>
    /// Removes and disposes the transaction context for a session.
    /// </summary>
    public async Task<bool> TryRemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_contexts.TryRemove(sessionId, out var context))
        {
            await DisposeContextAsync(context).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes and disposes contexts that have been idle past <see cref="MaxIdle"/>.
    /// Cheap to call on every operation; the dictionary is small in practice.
    /// </summary>
    public void SweepStale()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (sessionId, context) in _contexts)
        {
            if (now - context.LastUsedUtc <= MaxIdle)
            {
                continue;
            }

            if (_contexts.TryRemove(sessionId, out var removed))
            {
                _ = DisposeContextFireAndForget(removed);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var sessionId in _contexts.Keys.ToArray())
        {
            if (_contexts.TryRemove(sessionId, out var context))
            {
                try
                {
                    await DisposeContextAsync(context).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort shutdown cleanup.
                }
            }
        }
    }

    private static async Task DisposeContextFireAndForget(TransactionContext context)
    {
        try
        {
            await DisposeContextAsync(context).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup of abandoned sessions.
        }
    }

    /// <summary>
    /// Releases the connection and transaction objects held by a context.
    /// </summary>
    public static async Task DisposeContextAsync(TransactionContext context)
    {
        if (context.LocalTransaction is not null)
        {
            context.LocalTransaction.Dispose();
        }

        if (context.LocalConnection is not null)
        {
            await context.LocalConnection.DisposeAsync().ConfigureAwait(false);
        }

        if (context.ServerTransaction is not null)
        {
            await context.ServerTransaction.DisposeAsync().ConfigureAwait(false);
        }

        if (context.ServerConnection is not null)
        {
            await context.ServerConnection.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Holds the live connection and transaction instances for one browser session.
/// </summary>
public sealed class TransactionContext
{
    public required ViewerTransactionState State { get; init; }

    public SharpCoreDBConnection? LocalConnection { get; init; }

    public SharpCoreDBTransaction? LocalTransaction { get; init; }

    public SharpCoreDB.Client.SharpCoreDBConnection? ServerConnection { get; init; }

    public SharpCoreDB.Client.SharpCoreDBTransaction? ServerTransaction { get; init; }

    /// <summary>UTC timestamp of the last access, used for stale-sweeping.</summary>
    public DateTimeOffset LastUsedUtc { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Marks the context as recently used.</summary>
    public void Touch() => LastUsedUtc = DateTimeOffset.UtcNow;
}
