using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>
/// Windows taskbar overlay icon (Cosmetics pitch #24) through <c>ITaskbarList3.SetOverlayIcon</c>. Best effort like every
/// native wrapper here: off Windows, or if any COM call fails, it silently does nothing.
/// </summary>
public sealed class TaskbarBadgeService : IDisposable
{
    private readonly Window _window;
    private object? _taskbar;
    private IntPtr _currentIcon;
    private int _shownCount = -1;

    public TaskbarBadgeService(Window window) => _window = window;

    public void Show(int count, bool rerender = false)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var hwnd = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            _taskbar ??= CreateTaskbar();
            var list = (ITaskbarList3)_taskbar;
            // Re-send the overlay every time: Windows drops it when Explorer restarts or the button is recreated, and the old
            // "skip when the count is unchanged" guard then left it gone. Only the icon itself is cached (rebuilt on a new count or a skin change).
            IntPtr icon = _currentIcon;
            if (count != _shownCount || rerender || icon == IntPtr.Zero)
            {
                IntPtr fresh = IntPtr.Zero;
                if (count > 0)
                {
                    using var bitmap = TaskbarBadgeIcons.RenderOverlay(count);
                    fresh = ToHIcon(bitmap);
                }

                icon = fresh;
            }

            list.SetOverlayIcon(hwnd, icon, count > 0 ? $"{count} new releases" : string.Empty);
            if (icon != _currentIcon && _currentIcon != IntPtr.Zero)
            {
                DestroyIcon(_currentIcon);
            }

            _currentIcon = icon;
            _shownCount = count;
        }
        catch
        {
            // Overlay is cosmetic; never let it break the shell.
        }
    }

    public void Dispose()
    {
        Show(0);
        if (_taskbar is not null && OperatingSystem.IsWindows())
        {
            Marshal.FinalReleaseComObject(_taskbar);
            _taskbar = null;
        }
    }

    private static object CreateTaskbar()
    {
        var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090"))!;
        var instance = Activator.CreateInstance(type)!;
        ((ITaskbarList3)instance).HrInit();
        return instance;
    }

    private static IntPtr ToHIcon(RenderTargetBitmap bitmap)
    {
        int size = TaskbarBadgeIcons.OverlaySize;
        var pixels = new byte[size * size * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new Avalonia.PixelRect(0, 0, size, size), handle.AddrOfPinnedObject(), pixels.Length, size * 4);
        }
        finally
        {
            handle.Free();
        }

        // Avalonia hands back premultiplied alpha; icon colour bitmaps want straight alpha.
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            if (a is > 0 and < 255)
            {
                pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
            }
        }

        var colour = CreateBitmap(size, size, 1, 32, pixels);
        var mask = CreateBitmap(size, size, 1, 1, new byte[size * size / 8]);
        try
        {
            var info = new ICONINFO { fIcon = true, hbmColor = colour, hbmMask = mask };
            return CreateIconIndirect(ref info);
        }
        finally
        {
            DeleteObject(colour);
            DeleteObject(mask);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    // Vtable order matters: ITaskbarList, ITaskbarList2, then ITaskbarList3 up to SetOverlayIcon. Unused slots keep placeholder signatures.
    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hwnd);
        [PreserveSig] int DeleteTab(IntPtr hwnd);
        [PreserveSig] int ActivateTab(IntPtr hwnd);
        [PreserveSig] int SetActiveAlt(IntPtr hwnd);
        [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        [PreserveSig] int SetProgressState(IntPtr hwnd, int flags);
        [PreserveSig] int RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);
        [PreserveSig] int UnregisterTab(IntPtr hwndTab);
        [PreserveSig] int SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        [PreserveSig] int SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);
        [PreserveSig] int ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
        [PreserveSig] int ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
        [PreserveSig] int ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        [PreserveSig] int SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
    }
}
