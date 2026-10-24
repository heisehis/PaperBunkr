using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Layout;
using Paperbunkr.App.Theming.Motion;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Shows a screen through the active theme's layout when it has a valid one, and through the screen's own built-in layout otherwise
/// (design doc sections 8.4 and 10.1). The built-in layout is the control's child in XAML and is never thrown away, so it is always there to
/// fall back to: a missing, refused or failing theme layout shows the screen exactly as it ships.
///
/// The screen owns its behaviour. It supplies the contract (what a layout may place, show and trigger), a factory for its host components,
/// and its published values and commands; the layout only arranges them. Keyboard shortcuts stay registered on the screen itself, so
/// they work the same in any layout. A rebuild is deferred one dispatcher tick: swapping a subtree out from inside an event that is still
/// routing through it is a known crash in this framework.
/// </summary>
public sealed class ThemeLayoutHost : Decorator
{
    public static readonly StyledProperty<string> ScreenProperty = AvaloniaProperty.Register<ThemeLayoutHost, string>(nameof(Screen), string.Empty);

    private Control? _builtIn;
    private ThemeLayoutRenderResult? _rendered;
    private ThemeLayoutDocument? _document;
    private string? _activeBreakpoint;
    private bool _rebuildQueued;
    private bool _attached;
    private int _rebuildGeneration;

    /// <summary>The screen name a theme's <c>layouts</c> section uses ("Home").</summary>
    public string Screen
    {
        get => GetValue(ScreenProperty);
        set => SetValue(ScreenProperty, value);
    }

    public ThemeLayoutContract? Contract { get; set; }

    /// <summary>Creates the screen's host component for a slot node. Each call must return a new control.</summary>
    public Func<ThemeLayoutNode, Control?>? CreateSlot { get; set; }

    public Func<string, object?>? GetBinding { get; set; }

    public Func<string, ICommand?>? GetCommand { get; set; }

    /// <summary>Plays a named theme motion on a control (attached by the motion runtime).</summary>
    public Action<Control, string>? ApplyMotion { get; set; }

    /// <summary>True while the theme's layout (not the built-in one) is showing.</summary>
    public bool IsCustomLayoutActive => _rendered is not null;

    public string? ActiveBreakpoint => _activeBreakpoint;

    /// <summary>What validating and building the layout found, most recent build.</summary>
    public IReadOnlyList<ThemeDiagnostic> Diagnostics { get; private set; } = Array.Empty<ThemeDiagnostic>();

    /// <summary>The control built for a layout node id, when the theme's layout is showing.</summary>
    public Control? FindNode(string nodeId) => _rendered is not null && _rendered.Controls.TryGetValue(nodeId, out var control) ? control : null;

    /// <summary>Raised after the shown layout changed (theme layout applied, breakpoint crossed, or fell back) - the screen restores focus here.</summary>
    public event Action? LayoutApplied;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _builtIn ??= Child;
        _attached = true;
        ThemeLayoutSource.Changed += QueueRebuild;
        ThemeExtensionRegistry.Changed += QueueRebuild;
        RebuildNow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        ThemeLayoutSource.Changed -= QueueRebuild;
        ThemeExtensionRegistry.Changed -= QueueRebuild;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // Only a breakpoint crossing rebuilds; ordinary resizing is handled by the layout's own fluid constraints.
        if (_document is not null && _document.BreakpointFor(e.NewSize.Width)?.Name != _activeBreakpoint)
        {
            QueueRebuild();
        }
    }

    /// <summary>Re-reads every published value the layout shows. The screen calls this when its view-model changes.</summary>
    public void RefreshBindings() => _rendered?.Refresh();

    private void QueueRebuild()
    {
        if (_rebuildQueued || !_attached)
        {
            return;
        }

        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            if (_attached)
            {
                RebuildNow();
            }
        });
    }

    /// <summary>Validates and builds the active theme's layout for this screen, or falls back to the built-in one. Synchronous - production code goes through the deferred path.</summary>
    public void RebuildNow()
    {
        _builtIn ??= Child;
        var diagnostics = new List<ThemeDiagnostic>();
        ThemeLayoutRenderResult? rendered = null;
        ThemeLayoutDocument? document = null;
        string? breakpointName = null;

        var layout = Contract is null ? null : ThemeLayoutSource.LayoutFor(Screen);
        if (layout is not null && Contract is not null && CreateSlot is not null)
        {
            try
            {
                document = ThemeLayoutValidator.Validate(layout, Contract, diagnostics, ThemeLayoutSource.PatchesFor(Screen));
                if (document is not null)
                {
                    var breakpoint = document.BreakpointFor(Bounds.Width > 0 ? Bounds.Width : double.MaxValue);
                    breakpointName = breakpoint?.Name;
                    rendered = ThemeLayoutRenderer.Render(document.RootFor(breakpoint), new ThemeLayoutRenderContext
                    {
                        CreateSlot = CreateSlot,
                        GetBinding = name => GetBinding?.Invoke(name),
                        GetCommand = name => GetCommand?.Invoke(name),
                        OpenAsset = ThemeLayoutSource.OpenAsset,
                        GetMaterial = ThemeLayoutSource.Material,
                        CreateExtension = ThemeExtensionRegistry.Create,
                        ApplyMotion = (control, motion) => ApplyMotion?.Invoke(control, motion),
                    });

                    foreach (var extension in document.Root.SelfAndDescendants().Where(n => n.Type == "Extension"))
                    {
                        string provider = extension.Text("provider") ?? string.Empty;
                        if (!ThemeExtensionRegistry.ProviderIds.Contains(provider))
                        {
                            diagnostics.Add(new ThemeDiagnostic(
                                ThemeDiagnosticSeverity.Information, ThemeDiagnosticCodes.MissingProvider,
                                $"'{extension.Id}' uses the extension '{provider}', which is not installed; its fallback is shown.", $"layouts.{Screen}.{extension.Id}", FallbackUsed: true));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Whatever went wrong building a theme's layout, the screen still has to work.
                rendered = null;
                document = null;
                diagnostics.Add(new ThemeDiagnostic(
                    ThemeDiagnosticSeverity.Error, ThemeDiagnosticCodes.InvalidLayout,
                    $"The theme's {Screen} layout could not be built ({ex.Message}). The built-in layout is used.", $"layouts.{Screen}", FallbackUsed: true));
            }
        }

        Diagnostics = diagnostics;
        ThemeLayoutSource.Report(Screen, diagnostics);

        var replacement = rendered?.Root ?? _builtIn;
        bool changed = !ReferenceEquals(replacement, Child);
        _rendered = rendered;
        _document = document;
        _activeBreakpoint = breakpointName;

        if (changed)
        {
            TimeSpan exitDuration = Child is Control previous ? PlayExitTree(previous) : TimeSpan.Zero;
            int generation = ++_rebuildGeneration;
            if (exitDuration > TimeSpan.Zero && _attached)
            {
                DispatcherTimer.RunOnce(() =>
                {
                    if (_attached && generation == _rebuildGeneration)
                    {
                        Child = replacement;
                        LayoutApplied?.Invoke();
                    }
                }, exitDuration);
            }
            else
            {
                Child = replacement;
                LayoutApplied?.Invoke();
            }
        }
    }

    private static TimeSpan PlayExitTree(Control root)
    {
        TimeSpan longest = ThemeMotionRuntime.PlayExit(root);
        foreach (var control in root.GetVisualDescendants().OfType<Control>())
        {
            var duration = ThemeMotionRuntime.PlayExit(control);
            if (duration > longest)
            {
                longest = duration;
            }
        }

        return longest;
    }
}
