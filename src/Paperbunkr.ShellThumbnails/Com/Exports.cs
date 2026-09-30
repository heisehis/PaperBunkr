using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Paperbunkr.ShellThumbnails.Com;

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint outer, in Guid iid, out nint instance);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool lockServer);
}

[GeneratedComClass]
internal sealed partial class ThumbnailProviderFactory : IClassFactory
{
    private const int ClassENoAggregation = unchecked((int)0x80040110);

    public int CreateInstance(nint outer, in Guid iid, out nint instance)
    {
        instance = 0;
        if (outer != 0)
        {
            return ClassENoAggregation;
        }

        return Exports.QueryInterfaceFor(new ThumbnailProvider(), iid, out instance);
    }

    public int LockServer(bool lockServer) => 0;
}

/// <summary>
/// The two entry points COM needs from an in-process server, exported from the Native AOT DLL. A Native AOT
/// library can never be unloaded, so <c>DllCanUnloadNow</c> always answers "no" (S_FALSE).
/// </summary>
internal static unsafe class Exports
{
    private const int SFalse = 1;
    private const int ClassEClassNotAvailable = unchecked((int)0x80040111);
    private const int EPointer = unchecked((int)0x80004003);

    private static readonly StrategyBasedComWrappers Wrappers = new();

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int DllGetClassObject(Guid* clsid, Guid* iid, nint* result)
    {
        if (result is null)
        {
            return EPointer;
        }

        *result = 0;
        if (*clsid != ThumbnailProvider.Clsid)
        {
            return ClassEClassNotAvailable;
        }

        try
        {
            int hr = QueryInterfaceFor(new ThumbnailProviderFactory(), *iid, out nint factory);
            *result = factory;
            return hr;
        }
        catch (Exception ex)
        {
            return ex.HResult;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow", CallConvs = new[] { typeof(CallConvStdcall) })]
    public static int DllCanUnloadNow() => SFalse;

    /// <summary>Wraps a managed COM class and hands back the requested interface (caller owns the reference).</summary>
    internal static int QueryInterfaceFor(object instance, Guid iid, out nint result)
    {
        nint unknown = Wrappers.GetOrCreateComInterfaceForObject(instance, CreateComInterfaceFlags.None);
        try
        {
            return Marshal.QueryInterface(unknown, in iid, out result);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }
}
