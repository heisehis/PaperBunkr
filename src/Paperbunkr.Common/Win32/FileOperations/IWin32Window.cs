using System;

namespace cYo.Common.Win32.FileOperations
{
	// TODO(Paperbunkr): portable stand-in for System.Windows.Forms.IWin32Window, whose only
	// member (Handle) is all these Shell file-operation APIs actually need (an HWND to own the
	// native progress dialog). Avoid a WinForms dependency for a single-member interface.
	// A future Avalonia-hosted implementation can wrap PlatformImpl handle access to implement this.
	public interface IWin32Window
	{
		IntPtr Handle { get; }
	}
}
