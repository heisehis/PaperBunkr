namespace Paperbunkr.ShellThumbnails;

/// <summary>
/// Read-only pass-through that throws <see cref="TimeoutException"/> once the request's budget is spent
/// (design decision 14: ~3 s, then no thumbnail). The work runs on Windows' calling thread - the source
/// <c>IStream</c> isn't safe to hand to another thread - so the deadline is enforced cooperatively at every read
/// instead of by abandoning a background task.
/// </summary>
internal sealed class DeadlineStream : Stream
{
    private readonly Stream _inner;
    private readonly Deadline _deadline;

    public DeadlineStream(Stream inner, Deadline deadline)
    {
        _inner = inner;
        _deadline = deadline;
    }

    public override bool CanRead => true;

    public override bool CanSeek => _inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        _deadline.ThrowIfExpired();
        return _inner.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        _deadline.ThrowIfExpired();
        return _inner.Read(buffer);
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A point in time after which a thumbnail request gives up.</summary>
internal sealed class Deadline
{
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(3);

    private readonly long _expiresAt;

    public Deadline(TimeSpan budget)
    {
        _expiresAt = Environment.TickCount64 + (long)budget.TotalMilliseconds;
    }

    public TimeSpan Remaining => TimeSpan.FromMilliseconds(Math.Max(0, _expiresAt - Environment.TickCount64));

    public bool IsExpired => Environment.TickCount64 >= _expiresAt;

    public void ThrowIfExpired()
    {
        if (IsExpired)
        {
            throw new TimeoutException("Thumbnail budget exceeded.");
        }
    }
}
