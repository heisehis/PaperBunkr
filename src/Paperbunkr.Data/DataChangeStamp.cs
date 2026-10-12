using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Paperbunkr.Data;

/// <summary>
/// A process-wide counter that goes up whenever this process writes to the library database, through any
/// <see cref="PaperbunkrDbContext"/>: SaveChanges, ExecuteUpdate/ExecuteDelete and raw SQL alike. A screen that remembers the
/// value it loaded at can skip reloading on the next visit when the value has not moved
/// (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md §4.6).
/// <para>Deliberately coarse and conservative: any command that is not a plain <c>SELECT</c> or <c>PRAGMA</c> counts, and so
/// does every transaction commit (a write inside a transaction is not visible to other connections until then, so the commit
/// has to move the counter after any reader could have recorded it). It can report a change that did not matter; it must
/// never miss one. A write made by another process is not seen.</para>
/// </summary>
public static class DataChangeStamp
{
    private static long s_current;

    [ThreadStatic]
    private static int t_suppressed;

    /// <summary>The current value. Read it <em>before</em> loading, so a write that lands during the load leaves the reader stale.</summary>
    public static long Current => Interlocked.Read(ref s_current);

    /// <summary>Marks the data as changed by something the interceptors cannot see (a restored backup, a replaced file).</summary>
    public static void Touch() => Interlocked.Increment(ref s_current);

    /// <summary>
    /// Writes made on this thread until the scope is disposed do not move the counter. For bookkeeping no screen displays
    /// (the last-open screen), which would otherwise mark everything stale on every navigation. Synchronous writes only.
    /// </summary>
    public static IDisposable Suppress() => new SuppressScope();

    internal static readonly IInterceptor[] Interceptors = { new CommandInterceptor(), new TransactionInterceptor() };

    // Whether each open connection has run a counted write since its last commit.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbConnection, System.Runtime.CompilerServices.StrongBox<bool>> s_wroteSinceCommit = new();

    private static void Bump()
    {
        if (t_suppressed == 0)
        {
            Interlocked.Increment(ref s_current);
        }
    }

    internal static bool IsWrite(string? commandText)
    {
        if (string.IsNullOrEmpty(commandText))
        {
            return false;
        }

        var text = commandText.AsSpan().TrimStart();
        return !text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase)
            && !IsJobLogOnly(text);
    }

    // The Activity Center's run log is written whenever a background job finishes (several a minute with trackers and
    // schedules on) and no screen that asks this class shows it. Only the one statement EF generates for it is let through;
    // anything else that mentions the table still counts.
    private static bool IsJobLogOnly(ReadOnlySpan<char> text)
    {
        // The second is the tracker auto-sync's pull throttle (TrackingLink.LastSyncedAt), written each time a manga detail
        // page is opened and read only by the throttle itself. An update that sets any other column has different text.
        // EF puts WHERE on its own line, so the throttle update is matched as "SET that one column, then WHERE".
        const string throttle = "UPDATE \"TrackingLinks\" SET \"LastSyncedAt\" = @p0";
        bool isThrottle = text.StartsWith(throttle, StringComparison.Ordinal)
            && text.Slice(throttle.Length).TrimStart().StartsWith("WHERE ", StringComparison.Ordinal);
        // The third is the per-series activity log, which only the detail page's Activity tab reads (it loads on every
        // open and asks nobody whether it is stale). Whatever the event describes is a write of its own and counts there.
        if (!isThrottle
            && !text.StartsWith("INSERT INTO \"ActivityRuns\" (", StringComparison.Ordinal)
            && !text.StartsWith("INSERT INTO \"SeriesActivityEvents\" (", StringComparison.Ordinal))
        {
            return false;
        }

        int end = text.IndexOf(';');
        return end < 0 || text.Slice(end + 1).Trim().IsEmpty;
    }

    private sealed class SuppressScope : IDisposable
    {
        private bool _disposed;

        public SuppressScope() => t_suppressed++;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                t_suppressed--;
            }
        }
    }

    private sealed class CommandInterceptor : DbCommandInterceptor
    {
        private static void Note(DbCommand command)
        {
            if (IsWrite(command.CommandText))
            {
                Bump();
                if (command.Connection is { } connection)
                {
                    s_wroteSinceCommit.GetOrCreateValue(connection).Value = true;
                }
            }
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            Note(command);
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Note(command);
            return new ValueTask<int>(result);
        }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            Note(command);
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Note(command);
            return new ValueTask<DbDataReader>(result);
        }

        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
        {
            Note(command);
            return result;
        }

        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        {
            Note(command);
            return new ValueTask<object?>(result);
        }
    }

    private sealed class TransactionInterceptor : DbTransactionInterceptor
    {
        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Committed(eventData);

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Committed(eventData);
            return Task.CompletedTask;
        }

        // A commit only publishes what its connection wrote. One that carried nothing but exempt statements (the tracker
        // throttle is saved inside a transaction) must not count, or the exemption would be undone a line later.
        private static void Committed(TransactionEndEventData eventData)
        {
            // No context to ask (not expected): count it, the safe direction.
            if (eventData.Context is not { } context)
            {
                Bump();
                return;
            }

            if (s_wroteSinceCommit.TryGetValue(context.Database.GetDbConnection(), out var wrote) && wrote.Value)
            {
                wrote.Value = false;
                Bump();
            }
        }
    }
}
