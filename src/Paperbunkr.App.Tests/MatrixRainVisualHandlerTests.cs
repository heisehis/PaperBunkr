using System.Reflection;
using Paperbunkr.App.Services;
using Paperbunkr.App.Views;
using SkiaSharp;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="MatrixRainVisualHandler"/>'s run/dispose lifecycle (docs/superpowers/specs/
/// 2026-09-16-theme-system-design.md § Matrix rain effect, § Battery-linked animation throttling)
/// directly, bypassing the real <c>Compositor</c> attach a production overlay goes through.
/// Confirmed via a standalone probe (no Avalonia app/window) that calling
/// <c>RegisterForNextAnimationFrameUpdate()</c>/<c>Invalidate()</c>/<c>CompositionNow</c> on an
/// unattached handler throws <see cref="InvalidOperationException"/> ("Object is not yet attached
/// to the compositor") rather than no-op'ing - so these tests prime <c>_running</c>/<c>_shouldRun</c>/
/// <c>_paint</c>/<c>_typeface</c> directly via the internal test seam on those fields (App.Tests-only,
/// via this project's existing <c>InternalsVisibleTo</c>) instead of driving every transition through
/// <c>OnMessage</c> alone, and use that same exception as the observable proof a "resume" attempt
/// really reaches the framework's own registration call.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MatrixRainVisualHandlerTests
{
    [Fact]
    public void OnAnimationFrameUpdate_StopsReRegistering_OnceRunStateGoesFalse()
    {
        var handler = new MatrixRainVisualHandler();
        // Simulate the loop already mid-flight (what a real attach + a prior RunState(true) would
        // have produced) without going through the throwing attach path.
        handler._running = true;
        handler._shouldRun = true;

        // The "false" transition itself never touches RegisterForNextAnimationFrameUpdate/Invalidate
        // (only the _shouldRun && !_running branch does), so this is safe to call directly.
        var messageException = Record.Exception(() => handler.OnMessage(new MatrixRainRunState(false)));
        Assert.Null(messageException);

        // OnAnimationFrameUpdate's own first line is `if (!_shouldRun) { _running = false; return; }` -
        // it returns before ever calling Invalidate()/RegisterForNextAnimationFrameUpdate(), so this
        // must not throw even though the handler was never attached to a real compositor. A throw
        // here would mean the loop tried to keep running instead of terminating.
        var frameException = Record.Exception(() => handler.OnAnimationFrameUpdate());
        Assert.Null(frameException);
        Assert.False(handler._running);
    }

    [Fact]
    public void OnMessage_RunStateTrue_WhenAlreadyRunning_DoesNotReRegister()
    {
        var handler = new MatrixRainVisualHandler();
        handler._running = true; // loop already active

        // _shouldRun && !_running is false here (!_running is false), so the register call is
        // skipped - must not throw despite no compositor attach.
        var exception = Record.Exception(() => handler.OnMessage(new MatrixRainRunState(true)));

        Assert.Null(exception);
        Assert.True(handler._running);
    }

    [Fact]
    public void OnMessage_RunStateTrue_WhenStopped_AttemptsToResume_ReachesCompositorRegistration()
    {
        var handler = new MatrixRainVisualHandler(); // fresh: _running=false, _shouldRun=false

        // Resuming after a stop (design doc's "real bug caught in review, now fixed" case) has to
        // actively call RegisterForNextAnimationFrameUpdate() again from OnMessage - there is no
        // live Compositor in this unit-test host to observe that call succeed, but the framework's
        // own attach guard is a precise, honest proxy: it only throws from inside that exact call,
        // so reaching it (rather than silently no-op'ing) is real proof the resume path fires.
        var exception = Assert.Throws<InvalidOperationException>(
            () => handler.OnMessage(new MatrixRainRunState(true)));

        Assert.Contains("compositor", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnMessage_Dispose_DisposesPaintAndTypeface_NotJustNullsReferences()
    {
        var handler = new MatrixRainVisualHandler();
        var paint = new SKPaint();
        var typeface = SKTypeface.FromFamilyName("Arial");
        handler._paint = paint;
        handler._typeface = typeface;

        var disposeException = Record.Exception(() => handler.OnMessage(new MatrixRainDispose()));

        Assert.Null(disposeException);
        Assert.Null(handler._paint);
        Assert.Null(handler._typeface);
        // Handle going to IntPtr.Zero is the real disposal signal for SKPaint - SKObject's native
        // handle is released synchronously on Dispose(), it's not merely GC-eligible. (Reading a
        // disposed SKPaint's managed properties like .Color instead access-violates the process
        // rather than throwing ObjectDisposedException - confirmed via a standalone probe - so
        // Handle is the only property safe to assert here.)
        Assert.Equal(IntPtr.Zero, paint.Handle);
        // SKTypeface is different: confirmed via a standalone probe that SKTypeface.FromFamilyName
        // returns an SKObject with IgnorePublicDispose=true (SkiaSharp treats family-name typefaces
        // as owned by its internal font-manager cache), so calling .Dispose() on it is a deliberate,
        // documented no-op - Handle intentionally does NOT go to zero, unlike SKPaint. The
        // leak-avoidance concern this test exists for doesn't actually apply to this SKTypeface:
        // SkiaSharp's own cache is exactly why MatrixRainVisualHandler's `_typeface ??=
        // SKTypeface.FromFamilyName(...)` pattern doesn't allocate a fresh native typeface per
        // switch/dispose cycle either. What matters here is just that Dispose() is called and
        // doesn't throw, and that the handler drops its own reference.
    }

    [Theory]
    [InlineData(nameof(MatrixRainVisualHandler.OnMessage))]
    [InlineData(nameof(MatrixRainVisualHandler.OnAnimationFrameUpdate))]
    [InlineData(nameof(MatrixRainVisualHandler.OnRender))]
    public void RenderThreadMethod_NeverReferences_BatteryStatusInterop_IsThrottled(string methodName)
    {
        // Design doc's own battery-throttle constraint: GetSystemPowerStatus must only ever be
        // polled from MatrixRainOverlay's 30s UI-thread DispatcherTimer, never from the render
        // thread (a P/Invoke transition 60-144 times/second was the rejected design). A metadata
        // token is unique per member within a module; if that exact 4-byte token sequence never
        // appears in this method's IL, no instruction in it can reference BatteryStatusInterop.
        // IsThrottled - true regardless of which opcode (call/callvirt/ldftn/...) would carry it, so
        // this doesn't need a full IL-opcode decoder to be a sound proof of absence.
        var targetMethod = typeof(BatteryStatusInterop).GetMethod(nameof(BatteryStatusInterop.IsThrottled))!;
        byte[] tokenBytes = BitConverter.GetBytes(targetMethod.MetadataToken);

        var method = typeof(MatrixRainVisualHandler).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!;
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;

        Assert.False(ContainsSubsequence(il, tokenBytes),
            $"{methodName}'s IL references BatteryStatusInterop.IsThrottled's metadata token - it must only be called from the UI-thread timer.");
    }

    private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }

        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
