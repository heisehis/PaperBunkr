using System;

namespace cYo.Common.Windows.Forms
{
	[Flags]
	public enum AutomaticProgressDialogOptions
	{
		None = 0x0,
		EnableCancel = 0x1
	}

	// TODO(Paperbunkr): stand-in for ComicRackCE's cYo.Common.Windows.Forms.AutomaticProgressDialog
	// (a WinForms modal dialog that pops up after a delay to show progress/allow cancelling a
	// long-running operation, e.g. ComicDatabase.FinalizeLoading()'s database-consistency check).
	// The original shows real UI (a Form) and supports user cancellation via ShouldAbort; this
	// headless stand-in just runs the work synchronously with no UI and no cancel support, so core
	// engine logic keeps working during the retarget spike. Needs a real Avalonia-backed progress
	// UI (and a way for engine code to check for cancellation) once the UI layer exists.
	public static class AutomaticProgressDialog
	{
		public static bool ShouldAbort { get; set; }

		public static int Value { get; set; }

		public static bool Process(object parent, string caption, string description, int timeToWait, Action executeMethod, AutomaticProgressDialogOptions options)
		{
			ShouldAbort = false;
			Value = 0;
			executeMethod?.Invoke();
			return !ShouldAbort;
		}
	}
}
