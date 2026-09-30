using System.Runtime.InteropServices;
using Paperbunkr.ShellThumbnails.Com;

namespace Paperbunkr.ShellThumbnails.Interop;

/// <summary>
/// The folder this handler DLL was loaded from. Inside Windows' thumbnail surrogate <see cref="AppContext.BaseDirectory"/>
/// is dllhost.exe's folder (System32) - verified 2026-09-30 when pdfium.dll next to the DLL wasn't found - so the
/// neighbours it ships with (pdfium.dll, Resources\ddjvu.exe) are located from the module that contains our own code.
/// Falls back to <see cref="AppContext.BaseDirectory"/> when the DLL isn't native (tests run it as a managed assembly).
/// </summary>
internal static unsafe partial class ModuleLocation
{
    private const uint FromAddress = 0x4;
    private const uint UnchangedRefCount = 0x2;

    private static readonly Lazy<string> Folder = new(Resolve);

    public static string Directory => Folder.Value;

    private static string Resolve()
    {
        delegate* unmanaged[Stdcall]<int> anyExport = &Exports.DllCanUnloadNow;
        if (GetModuleHandleExW(FromAddress | UnchangedRefCount, anyExport, out nint module))
        {
            char* buffer = stackalloc char[1024];
            uint length = GetModuleFileNameW(module, buffer, 1024);
            if (length is > 0 and < 1024)
            {
                string path = new(buffer, 0, (int)length);
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFileName(path).Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase)
                    && Path.GetDirectoryName(path) is { } folder)
                {
                    return folder;
                }
            }
        }

        return AppContext.BaseDirectory;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleHandleExW(uint flags, void* address, out nint module);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetModuleFileNameW(nint module, char* buffer, uint size);
}
