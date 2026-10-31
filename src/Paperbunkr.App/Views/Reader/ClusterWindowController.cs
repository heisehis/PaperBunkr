using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.Views.Reader;

/// <summary>
/// Shows one floating reader cluster (<see cref="ReaderCluster"/>) in its own small owned window over the book reader's NativeWebView
/// (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §5.2): Avalonia cannot draw over the native page in the same window,
/// but an owned window always sits above its owner. The window is sized to the cluster, pinned to an <see cref="ClusterAnchor"/> of the host
/// (the reader's root grid) and follows the owner as it moves or resizes. Only the cluster's own pixels are window, so clicks anywhere else
/// still reach the page. The window never keeps activation: after a click it hands focus back to the reader, so keys keep reaching the input
/// service. Same lifecycle as <see cref="OverlayWindowController"/>, which the reader's panels use.
/// </summary>
internal sealed class ClusterWindowController
{
    private readonly Control _content;
    private readonly Func<Layoutable?> _getHost;
    private readonly ClusterAnchor _anchor;
    private OverlayHostWindow? _window;
    private UserControl? _wrapper;
    private Layoutable? _host;
    private Window? _owner;

    public ClusterWindowController(Control content, Func<Layoutable?> getHost, ClusterAnchor anchor)
    {
        _content = content;
        _getHost = getHost;
        _anchor = anchor;
    }

    public bool IsOpen => _window is not null;

    public void SetOpen(bool open)
    {
        if (open)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    private void Show()
    {
        if (_window is not null)
        {
            Sync();
            return;
        }

        var host = _getHost();
        if (host is null || TopLevel.GetTopLevel(host) is not Window owner || !owner.IsVisible)
        {
            Services.DiagnosticsService.LogMilestone($"BookReader cluster {_anchor}: not shown (host {(host is null ? "missing" : "not in a visible window")}).");
            return;
        }

        if (_content.Parent is Panel parent)
        {
            parent.Children.Remove(_content);
        }

        _host = host;
        _owner = owner;
        _host.LayoutUpdated += OnHostLayoutUpdated;
        _owner.PositionChanged += OnOwnerPositionChanged;

        // Bindings on the cluster resolve against the reader's view model, which a separate window does not inherit.
        _wrapper = new UserControl { Content = _content, DataContext = host.DataContext };

        // Size and place the window before it is shown, as the highlight popup does: a window shown first and moved later can open at the
        // screen's default spot (and, if its size is not known yet, stay there).
        _wrapper.Measure(Size.Infinity);
        var size = _wrapper.DesiredSize;
        var window = new OverlayHostWindow
        {
            Content = _wrapper,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowActivated = false,
            Topmost = false,
        };
        if (size.Width > 0 && size.Height > 0)
        {
            window.Position = _host.PointToScreen(ClusterAnchorMath.Place(_anchor, _host.Bounds.Size, size));
        }

        window.SizeChanged += (_, _) => Sync();
        window.Opened += (_, _) => Sync();
        _window = window;
        window.Show(owner);
        MakeNonActivating(window);
        Sync();
        Services.DiagnosticsService.LogMilestone($"BookReader cluster {_anchor}: shown at {window.Position} size {size} over host {_host.Bounds.Size}.");
    }

    private void Hide()
    {
        if (_host is not null)
        {
            _host.LayoutUpdated -= OnHostLayoutUpdated;
            _host = null;
        }

        if (_owner is not null)
        {
            _owner.PositionChanged -= OnOwnerPositionChanged;
            _owner = null;
        }

        if (_window is null)
        {
            return;
        }

        if (_wrapper is not null)
        {
            _wrapper.Content = null;
            _wrapper = null;
        }

        _window.Content = null;
        _window.Close();
        _window = null;
    }

    // A cluster window must never take activation. Handing activation back to the reader after a click (the first version) moved focus off the
    // pressed button between press and release, and a Button that loses focus drops its press, so no click ever fired. WS_EX_NOACTIVATE lets the
    // window take clicks while the reader keeps activation and keyboard focus.
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static void MakeNonActivating(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero)
        {
            return;
        }

        long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExNoActivate));
    }

    private void OnOwnerPositionChanged(object? sender, PixelPointEventArgs e) => Sync();

    private void OnHostLayoutUpdated(object? sender, EventArgs e) => Sync();

    private void Sync()
    {
        if (_window is null || _host is null || _owner is null)
        {
            return;
        }

        var clusterSize = _window.ClientSize;
        if (clusterSize.Width <= 0 || clusterSize.Height <= 0)
        {
            clusterSize = _wrapper?.DesiredSize ?? default;
        }

        if (clusterSize.Width <= 0 || clusterSize.Height <= 0)
        {
            return;
        }

        var place = ClusterAnchorMath.Place(_anchor, _host.Bounds.Size, clusterSize);
        var screenPoint = _host.PointToScreen(place);
        if (_window.Position != screenPoint)
        {
            _window.Position = screenPoint;
        }
    }
}
