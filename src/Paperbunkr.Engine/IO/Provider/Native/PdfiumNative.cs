using System;
using System.Runtime.InteropServices;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Native
{
	/// <summary>
	/// Fresh, minimal P/Invoke shim for PDFium (docs/onboarding.md §2/§4). Written from PDFium's
	/// own public C API (BSD/Apache-licensed, https://pdfium.googlesource.com/pdfium/), not ported
	/// from ComicRackCE. CE didn't actually hand-roll a raw DllImport layer for pdfium at all --
	/// it depended entirely on the third-party PDFiumSharpV2 / bblanchon.PDFium.Win32 NuGet
	/// packages (see Pdfium.cs / PdfiumReaderEngine.cs), whose native binary was bundled via a
	/// post-build xcopy step keyed to a hardcoded win-x86/win-x64 layout (the "Bundled via
	/// post-build xcopy" resolution mechanism flagged in onboarding.md §4).
	///
	/// This class exposes just the handful of entry points needed to open a document and render a
	/// page to a bitmap buffer -- the actual subset the comic-page-thumbnail use case needs --
	/// using NativeLibrary.SetDllImportResolver instead of a build-time xcopy/hardcoded path.
	/// It is not currently wired into ComicRack.Engine's PDF provider (which keeps using the
	/// PDFiumSharpV2 package unchanged for this spike); see retarget_spike_report.md for the
	/// rationale and the follow-up work this implies. It *is* wired into <c>PdfBookSource</c>
	/// (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md §4) via the text/
	/// metadata/bookmark entry points below -- PDFiumSharpV2 exposes none of those.
	/// </summary>
	public static class PdfiumNative
	{
		private const string LibraryName = "pdfium";

		static PdfiumNative()
		{
			NativeInterop.RegisterResolver(typeof(PdfiumNative).Assembly, LibraryName);
		}

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern void FPDF_InitLibrary();

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern void FPDF_DestroyLibrary();

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern IntPtr FPDF_LoadDocument(string filePath, string password);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern void FPDF_CloseDocument(IntPtr document);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern int FPDF_GetPageCount(IntPtr document);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern void FPDF_ClosePage(IntPtr page);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern double FPDF_GetPageWidth(IntPtr page);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern double FPDF_GetPageHeight(IntPtr page);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern int FPDFBitmap_GetStride(IntPtr bitmap);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

		// --- Added for Novel PDF text-extraction reflow (docs/superpowers/specs/
		// 2026-08-09-novels-epub-pdf-support-design.md §4/§9.2) -- PDFiumSharpV2 (the wrapper
		// already referenced by Paperbunkr.Engine for comic-page PDF rendering) exposes no text
		// APIs at all, so PdfBookSource talks to these entry points directly instead. Same
		// "pdfium" native binary already bundled for comic PDFs -- no new native dependency, just
		// more of PDFium's own public C API surfaced through the resolver already registered above.

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr FPDFText_LoadPage(IntPtr page);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern void FPDFText_ClosePage(IntPtr textPage);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern int FPDFText_CountChars(IntPtr textPage);

		/// <summary>UTF-16LE code units, not including the null terminator FPDFText_GetText also writes.</summary>
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern int FPDFText_GetText(IntPtr textPage, int startIndex, int count, [Out] byte[] result);

		/// <summary>
		/// UTF-16LE, including the null terminator. Returns the required buffer length in bytes;
		/// call once with a null/zero-length buffer to size it, per PDFium's own convention.
		/// </summary>
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
		public static extern uint FPDF_GetMetaText(IntPtr document, string tag, byte[]? buffer, uint buflen);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr FPDFBookmark_GetFirstChild(IntPtr document, IntPtr bookmark);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr FPDFBookmark_GetNextSibling(IntPtr document, IntPtr bookmark);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern uint FPDFBookmark_GetTitle(IntPtr bookmark, byte[]? buffer, uint buflen);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern IntPtr FPDFBookmark_GetDest(IntPtr document, IntPtr bookmark);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern int FPDFDest_GetDestPageIndex(IntPtr document, IntPtr dest);
	}
}
