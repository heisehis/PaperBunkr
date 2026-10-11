using System;
using System.Runtime.InteropServices;

namespace Paperbunkr.App.Services.Performance;

/// <summary>
/// Installed RAM and how much of it the whole system is using right now (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md
/// §4.1/§4.2). Windows answers through <c>GlobalMemoryStatusEx</c>; anywhere else, or if the call fails, the runtime's own
/// figures from the last garbage collection are used (slightly stale, good enough to size a cache).
/// </summary>
public static class SystemMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static bool TryRead(out MemoryStatusEx status)
    {
        status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return GlobalMemoryStatusEx(ref status);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Installed physical memory in bytes.</summary>
    public static long TotalPhysicalBytes
    {
        get
        {
            if (TryRead(out var status) && status.TotalPhys > 0)
            {
                return (long)status.TotalPhys;
            }

            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        }
    }

    /// <summary>Share of physical memory in use across the whole system, 0-100.</summary>
    public static int LoadPercent
    {
        get
        {
            if (TryRead(out var status))
            {
                return (int)Math.Min(100, status.MemoryLoad);
            }

            var info = GC.GetGCMemoryInfo();
            return info.TotalAvailableMemoryBytes <= 0
                ? 0
                : (int)Math.Clamp(info.MemoryLoadBytes * 100 / info.TotalAvailableMemoryBytes, 0, 100);
        }
    }
}
