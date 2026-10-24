using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Paperbunkr.App.Services;

/// <summary>How the "not responding" dialog was closed.</summary>
public enum FreezeDialogResult
{
    /// <summary>The user chose Keep waiting (or pressed Esc / the close box).</summary>
    KeepWaiting,

    /// <summary>The user chose Close Paperbunkr.</summary>
    Close,

    /// <summary>The UI thread answered again and the watchdog closed the dialog itself.</summary>
    Recovered,
}

/// <summary>
/// The freeze watchdog's "not responding" dialog (docs/superpowers/specs/2026-10-21-freeze-watchdog-
/// redesign-design.md §2). <see cref="Show"/> must not block: the watchdog keeps pinging the UI
/// thread while the dialog is up, and calls <see cref="RequestClose"/> when it answers.
/// </summary>
public interface IFreezeDialog
{
    /// <param name="elapsed">How long the UI thread has not answered; read repeatedly for the live counter.</param>
    /// <param name="onClosed">Called once, on the dialog's own thread, when the dialog closes.</param>
    void Show(Func<TimeSpan> elapsed, Action<FreezeDialogResult> onClosed);

    /// <summary>Close the dialog because the app recovered. Safe to call when nothing is showing.</summary>
    void RequestClose();
}

/// <summary>Human-readable freeze durations: "47 s", "1 min 12 s".</summary>
public static class FreezeDurationText
{
    public static string Format(TimeSpan duration)
    {
        int seconds = Math.Max(0, (int)Math.Round(duration.TotalSeconds));
        if (seconds < 60)
        {
            return $"{seconds} s";
        }

        int minutes = seconds / 60;
        int rest = seconds % 60;
        return rest == 0 ? $"{minutes} min" : $"{minutes} min {rest} s";
    }

    /// <summary>The dialog's live counter: "0:42", "12:05".</summary>
    public static string Clock(TimeSpan duration)
    {
        int seconds = Math.Max(0, (int)duration.TotalSeconds);
        return $"{seconds / 60}:{seconds % 60:00}";
    }
}

/// <summary>
/// Windows <c>TaskDialogIndirect</c> implementation of <see cref="IFreezeDialog"/>, shown on its own
/// background thread so it never depends on the (wedged) Avalonia dispatcher. Needs Common Controls
/// v6 (<c>app.manifest</c>); without it <c>TaskDialogIndirect</c> isn't exported and this falls back
/// to <see cref="NativeMessageBox.ShowNotResponding"/>, which has no live counter and can't close
/// itself. Not unit-tested (thin native wrapper, same as <see cref="NativeMessageBox"/>); checked on
/// screen with the Debug-only <c>PAPERBUNKR_SIMULATE_FREEZE</c> trigger.
/// </summary>
internal sealed class NativeFreezeDialog : IFreezeDialog
{
    private const int KeepWaitingButtonId = 100;
    private const int CloseButtonId = 101;

    private volatile bool _closeRequested;

    public void Show(Func<TimeSpan> elapsed, Action<FreezeDialogResult> onClosed)
    {
        _closeRequested = false;
        var thread = new Thread(() =>
        {
            FreezeDialogResult result;
            try
            {
                result = ShowTaskDialog(elapsed);
            }
            catch (EntryPointNotFoundException)
            {
                result = ShowFallback();
            }
            catch (DllNotFoundException)
            {
                result = ShowFallback();
            }

            if (_closeRequested && result == FreezeDialogResult.KeepWaiting)
            {
                result = FreezeDialogResult.Recovered;
            }

            onClosed(result);
        })
        {
            IsBackground = true,
            Name = "Paperbunkr Freeze Dialog",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public void RequestClose() => _closeRequested = true;

    private static FreezeDialogResult ShowFallback() =>
        NativeMessageBox.ShowNotResponding() == NativeMessageBoxResult.ForceExit
            ? FreezeDialogResult.Close
            : FreezeDialogResult.KeepWaiting;

    private static string ContentText(TimeSpan elapsed) =>
        $"Not responding for {FreezeDurationText.Clock(elapsed)}. It may still finish what it's doing.";

    private FreezeDialogResult ShowTaskDialog(Func<TimeSpan> elapsed)
    {
        IntPtr title = Marshal.StringToHGlobalUni("Paperbunkr");
        IntPtr instruction = Marshal.StringToHGlobalUni("Paperbunkr isn't responding");
        IntPtr content = Marshal.StringToHGlobalUni(ContentText(elapsed()));
        IntPtr footer = Marshal.StringToHGlobalUni(
            "Closing now may lose changes in progress. <a href=\"logs\">Open log folder</a>");
        IntPtr keepText = Marshal.StringToHGlobalUni("Keep waiting");
        IntPtr closeText = Marshal.StringToHGlobalUni("Close Paperbunkr");

        int buttonSize = Marshal.SizeOf<TaskDialogButton>();
        IntPtr buttons = Marshal.AllocHGlobal(buttonSize * 2);
        string lastShown = string.Empty;

        TaskDialogCallback callback = (hwnd, msg, wParam, lParam, refData) =>
        {
            switch (msg)
            {
                case TDN_CREATED:
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                    break;
                case TDN_TIMER:
                    if (_closeRequested)
                    {
                        SendMessage(hwnd, TDM_CLICK_BUTTON, (IntPtr)KeepWaitingButtonId, IntPtr.Zero);
                        break;
                    }

                    string text = ContentText(elapsed());
                    if (text != lastShown)
                    {
                        lastShown = text;
                        IntPtr p = Marshal.StringToHGlobalUni(text);
                        try
                        {
                            SendMessage(hwnd, TDM_SET_ELEMENT_TEXT, (IntPtr)TDE_CONTENT, p);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(p);
                        }
                    }

                    break;
                case TDN_HYPERLINK_CLICKED:
                    OpenLogFolder();
                    break;
            }

            return 0; // S_OK; for TDN_BUTTON_CLICKED this lets the dialog close.
        };

        try
        {
            Marshal.StructureToPtr(new TaskDialogButton { Id = KeepWaitingButtonId, Text = keepText }, buttons, false);
            Marshal.StructureToPtr(new TaskDialogButton { Id = CloseButtonId, Text = closeText }, buttons + buttonSize, false);

            var config = new TaskDialogConfig
            {
                Size = (uint)Marshal.SizeOf<TaskDialogConfig>(),
                Flags = TDF_ENABLE_HYPERLINKS | TDF_ALLOW_DIALOG_CANCELLATION | TDF_CALLBACK_TIMER,
                WindowTitle = title,
                MainIcon = TD_WARNING_ICON,
                MainInstruction = instruction,
                Content = content,
                ButtonCount = 2,
                Buttons = buttons,
                DefaultButton = KeepWaitingButtonId,
                FooterIcon = TD_WARNING_ICON,
                Footer = footer,
                Callback = Marshal.GetFunctionPointerForDelegate(callback),
            };

            int hr = TaskDialogIndirect(ref config, out int pressed, IntPtr.Zero, IntPtr.Zero);
            GC.KeepAlive(callback);
            if (hr < 0)
            {
                return ShowFallback();
            }

            return pressed == CloseButtonId ? FreezeDialogResult.Close : FreezeDialogResult.KeepWaiting;
        }
        finally
        {
            Marshal.FreeHGlobal(buttons);
            Marshal.FreeHGlobal(title);
            Marshal.FreeHGlobal(instruction);
            Marshal.FreeHGlobal(content);
            Marshal.FreeHGlobal(footer);
            Marshal.FreeHGlobal(keepText);
            Marshal.FreeHGlobal(closeText);
        }
    }

    private static void OpenLogFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DiagnosticsService.LogDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"FreezeWatchdog: couldn't open log folder - {ex.GetType().Name}: {ex.Message}");
        }
    }

    // --- comctl32 v6 TaskDialog interop. commctrl.h declares these structs inside pshpack1.h. ---

    private const uint TDF_ENABLE_HYPERLINKS = 0x0001;
    private const uint TDF_ALLOW_DIALOG_CANCELLATION = 0x0008;
    private const uint TDF_CALLBACK_TIMER = 0x0800;

    private const uint TDN_CREATED = 0;
    private const uint TDN_HYPERLINK_CLICKED = 3;
    private const uint TDN_TIMER = 4;

    private const uint WM_USER = 0x0400;
    private const uint TDM_CLICK_BUTTON = WM_USER + 102;
    private const uint TDM_SET_ELEMENT_TEXT = WM_USER + 108;
    private const int TDE_CONTENT = 0;

    // MAKEINTRESOURCEW(-1)
    private static readonly IntPtr TD_WARNING_ICON = new(0xFFFF);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TaskDialogCallback(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr refData);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TaskDialogButton
    {
        public int Id;
        public IntPtr Text;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TaskDialogConfig
    {
        public uint Size;
        public IntPtr Parent;
        public IntPtr Instance;
        public uint Flags;
        public uint CommonButtons;
        public IntPtr WindowTitle;
        public IntPtr MainIcon;
        public IntPtr MainInstruction;
        public IntPtr Content;
        public uint ButtonCount;
        public IntPtr Buttons;
        public int DefaultButton;
        public uint RadioButtonCount;
        public IntPtr RadioButtons;
        public int DefaultRadioButton;
        public IntPtr VerificationText;
        public IntPtr ExpandedInformation;
        public IntPtr ExpandedControlText;
        public IntPtr CollapsedControlText;
        public IntPtr FooterIcon;
        public IntPtr Footer;
        public IntPtr Callback;
        public IntPtr CallbackData;
        public uint Width;
    }

    [DllImport("comctl32.dll", EntryPoint = "TaskDialogIndirect", ExactSpelling = true)]
    private static extern int TaskDialogIndirect(ref TaskDialogConfig config, out int button, IntPtr radioButton, IntPtr verificationChecked);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
