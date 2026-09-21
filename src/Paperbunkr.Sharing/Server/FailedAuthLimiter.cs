using System.Collections.Concurrent;

namespace Paperbunkr.Sharing.Server;

/// <summary>
/// Per-client failed-login backoff (spec §6). After <c>maxFailures</c> bad passwords inside
/// <c>window</c> the client is locked out; each further lockout doubles the duration up to
/// <c>maxLockout</c>. A successful login clears the client's record. <see cref="LockedOut"/> fires once
/// per lockout so the host can raise an Activity Center alert without polling.
/// </summary>
public sealed class FailedAuthLimiter
{
    private sealed class Entry
    {
        public readonly List<DateTimeOffset> Failures = new();
        public DateTimeOffset BlockedUntil;
        public int Lockouts;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly int _maxFailures;
    private readonly TimeSpan _window;
    private readonly TimeSpan _baseLockout;
    private readonly TimeSpan _maxLockout;
    private readonly TimeProvider _time;

    public FailedAuthLimiter(
        int maxFailures = 5,
        TimeSpan? window = null,
        TimeSpan? baseLockout = null,
        TimeSpan? maxLockout = null,
        TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFailures, 1);
        _maxFailures = maxFailures;
        _window = window ?? TimeSpan.FromMinutes(5);
        _baseLockout = baseLockout ?? TimeSpan.FromMinutes(1);
        _maxLockout = maxLockout ?? TimeSpan.FromMinutes(15);
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised with the client key each time a client becomes locked out.</summary>
    public event Action<string>? LockedOut;

    public bool IsBlocked(string clientKey, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_entries.TryGetValue(clientKey, out Entry? entry))
        {
            return false;
        }

        lock (entry)
        {
            TimeSpan remaining = entry.BlockedUntil - _time.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            retryAfter = remaining;
            return true;
        }
    }

    public void RecordFailure(string clientKey)
    {
        Entry entry = _entries.GetOrAdd(clientKey, _ => new Entry());
        bool lockedOutNow = false;

        lock (entry)
        {
            DateTimeOffset now = _time.GetUtcNow();
            entry.Failures.RemoveAll(f => now - f > _window);
            entry.Failures.Add(now);

            if (entry.Failures.Count >= _maxFailures && entry.BlockedUntil <= now)
            {
                double factor = Math.Pow(2, entry.Lockouts);
                TimeSpan duration = TimeSpan.FromTicks((long)Math.Min(_baseLockout.Ticks * factor, _maxLockout.Ticks));
                entry.BlockedUntil = now + duration;
                entry.Lockouts++;
                entry.Failures.Clear();
                lockedOutNow = true;
            }
        }

        if (lockedOutNow)
        {
            LockedOut?.Invoke(clientKey);
        }
    }

    public void RecordSuccess(string clientKey) => _entries.TryRemove(clientKey, out _);
}
