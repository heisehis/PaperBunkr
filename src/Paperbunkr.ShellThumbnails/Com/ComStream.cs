using System.Runtime.InteropServices;
using Paperbunkr.ShellThumbnails.Interop;

namespace Paperbunkr.ShellThumbnails.Com;

/// <summary>
/// Read-only seekable <see cref="Stream"/> over the <c>IStream</c> Windows passes to
/// <c>IInitializeWithStream.Initialize</c>, called by vtable slot. Owns one reference to the IStream.
/// </summary>
internal sealed unsafe class ComStream : Stream
{
    private const int SlotRead = 3;
    private const int SlotSeek = 5;
    private const int SlotStat = 12;
    private const uint StatDefault = 0;
    private const uint StatNoName = 1;

    private ComPtr _stream;
    private long _position;
    private long _length = -1;

    /// <summary>Takes ownership of an already-AddRef'd IStream pointer.</summary>
    public ComStream(nint stream)
    {
        _stream = new ComPtr(stream);
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            if (_length < 0)
            {
                Stat(StatNoName, out _length);
            }

            return _length;
        }
    }

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    /// <summary>The stream's file name as Windows reports it (usually the file's own name), or null.</summary>
    public string? Name => Stat(StatDefault, out _);

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        uint read;
        fixed (byte* p = buffer)
        {
            int hr = ((delegate* unmanaged[Stdcall]<nint, byte*, uint, uint*, int>)_stream.Slot(SlotRead))(_stream.Pointer, p, (uint)buffer.Length, &read);
            ComPtr.Check(hr);
        }

        _position += read;
        return (int)read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ulong newPosition;
        ComPtr.Check(((delegate* unmanaged[Stdcall]<nint, long, uint, ulong*, int>)_stream.Slot(SlotSeek))(_stream.Pointer, offset, (uint)origin, &newPosition));
        _position = (long)newPosition;
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _stream.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>IStream::Stat - STATSTG is { LPOLESTR name; DWORD type; ULARGE_INTEGER cbSize; ... }, 80 bytes on x64.</summary>
    private string? Stat(uint flags, out long size)
    {
        byte* statstg = stackalloc byte[128];
        new Span<byte>(statstg, 128).Clear();
        ComPtr.Check(((delegate* unmanaged[Stdcall]<nint, byte*, uint, int>)_stream.Slot(SlotStat))(_stream.Pointer, statstg, flags));
        size = *(long*)(statstg + 16);
        nint name = *(nint*)statstg;
        if (name == 0)
        {
            return null;
        }

        string? result = Marshal.PtrToStringUni(name);
        Marshal.FreeCoTaskMem(name);
        return result;
    }
}
