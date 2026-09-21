using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Paperbunkr.Sharing.Server;

/// <summary>
/// In-memory session tokens (spec §6): random 256-bit opaque values with a sliding expiry, never
/// persisted - restarting the server invalidates every session, which is the intended revocation story
/// in v1. A <see cref="TimeProvider"/> is injected so expiry is testable without sleeping.
/// </summary>
public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _expiries = new();
    private readonly TimeSpan _lifetime;
    private readonly TimeProvider _time;

    public SessionStore(TimeSpan lifetime, TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        _lifetime = lifetime;
        _time = time ?? TimeProvider.System;
    }

    public int ActiveCount
    {
        get
        {
            Purge();
            return _expiries.Count;
        }
    }

    public (string Token, DateTimeOffset ExpiresAt) Create()
    {
        string token = Base64Url(RandomNumberGenerator.GetBytes(32));
        DateTimeOffset expiresAt = _time.GetUtcNow() + _lifetime;
        _expiries[token] = expiresAt;
        return (token, expiresAt);
    }

    /// <summary>True (and the expiry slides forward) only for a live token.</summary>
    public bool TryValidate(string? token)
    {
        if (string.IsNullOrEmpty(token) || !_expiries.TryGetValue(token, out DateTimeOffset expiresAt))
        {
            return false;
        }

        DateTimeOffset now = _time.GetUtcNow();
        if (expiresAt <= now)
        {
            _expiries.TryRemove(token, out _);
            return false;
        }

        _expiries[token] = now + _lifetime;
        return true;
    }

    public void Revoke(string token) => _expiries.TryRemove(token, out _);

    public void Clear() => _expiries.Clear();

    private void Purge()
    {
        DateTimeOffset now = _time.GetUtcNow();
        foreach (var pair in _expiries)
        {
            if (pair.Value <= now)
            {
                _expiries.TryRemove(pair.Key, out _);
            }
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
