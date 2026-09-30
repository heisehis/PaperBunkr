using System.Runtime.InteropServices;

namespace Paperbunkr.ShellThumbnails.Interop;

/// <summary>
/// Minimal owned COM pointer for calling interfaces by vtable slot. Native AOT has no built-in COM
/// (<c>[ComImport]</c>), and the WIC/IStream surface used here is a handful of calls, so raw function
/// pointers keep the handler small instead of declaring whole interfaces.
/// </summary>
internal unsafe struct ComPtr : IDisposable
{
    public nint Pointer;

    public ComPtr(nint pointer)
    {
        Pointer = pointer;
    }

    public readonly bool IsNull => Pointer == 0;

    /// <summary>The function pointer at vtable slot <paramref name="slot"/> (0-2 are IUnknown).</summary>
    public readonly void* Slot(int slot) => (*(void***)Pointer)[slot];

    public void Dispose()
    {
        if (Pointer != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(2))(Pointer);
            Pointer = 0;
        }
    }

    public static void Check(int hr)
    {
        if (hr < 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }
    }
}
