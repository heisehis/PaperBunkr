using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Paperbunkr.ShellThumbnails.Interop;

/// <summary>
/// Renders page 1 of a PDF with the <c>pdfium.dll</c> Paperbunkr already ships (design decision 9), bound
/// directly with <c>[LibraryImport]</c> - PDFiumSharpV2 is netstandard2.0 and not AOT-annotated. The document
/// is read through PDFium's custom file-access callback, so only the parts page 1 needs are pulled from the
/// stream (and every read honours the request deadline via <see cref="DeadlineStream"/>). PDFium is not
/// thread-safe; calls are serialised.
/// </summary>
internal static unsafe partial class Pdfium
{
    private const string Library = "pdfium";
    private const int BitmapBgra = 4;
    private const int RenderAnnotations = 0x01;

    private static readonly object Gate = new();
    private static bool _initialized;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAccess
    {
        public uint FileLength; // C `unsigned long` - 32-bit on Windows
        public delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, int> GetBlock;
        public nint Param;
    }

    /// <summary>Page 1 rendered on white, scaled to fit a <paramref name="maxSide"/> square; null for an encrypted,
    /// empty or unreadable document.</summary>
    public static BgraImage? RenderFirstPage(Stream stream, int maxSide)
    {
        if (stream.Length is 0 or > uint.MaxValue)
        {
            return null;
        }

        lock (Gate)
        {
            if (!_initialized)
            {
                // Windows loads this DLL into its own dllhost.exe, whose default DLL search never looks in our folder.
                // Loading pdfium.dll by full path first makes the plain "pdfium" imports below bind to that module.
                string besideUs = Path.Combine(ModuleLocation.Directory, "pdfium.dll");
                if (File.Exists(besideUs))
                {
                    NativeLibrary.Load(besideUs);
                }

                FPDF_InitLibrary();
                _initialized = true;
            }

            var handle = GCHandle.Alloc(stream);
            try
            {
                var access = new FileAccess
                {
                    FileLength = (uint)stream.Length,
                    GetBlock = &ReadBlock,
                    Param = GCHandle.ToIntPtr(handle),
                };

                nint document = FPDF_LoadCustomDocument(&access, null);
                if (document == 0)
                {
                    return null; // includes FPDF_ERR_PASSWORD - decision 15, never prompt
                }

                try
                {
                    return RenderPage(document, maxSide);
                }
                finally
                {
                    FPDF_CloseDocument(document);
                }
            }
            finally
            {
                handle.Free();
            }
        }
    }

    private static BgraImage? RenderPage(nint document, int maxSide)
    {
        if (FPDF_GetPageCount(document) < 1)
        {
            return null;
        }

        nint page = FPDF_LoadPage(document, 0);
        if (page == 0)
        {
            return null;
        }

        try
        {
            // Page size is in points; fit it to the requested square (PDF pages have no pixel size, so this may enlarge).
            float pageWidth = FPDF_GetPageWidthF(page), pageHeight = FPDF_GetPageHeightF(page);
            if (pageWidth <= 0 || pageHeight <= 0)
            {
                return null;
            }

            double scale = Math.Min(maxSide / pageWidth, maxSide / pageHeight);
            int width = Math.Max(1, (int)Math.Round(pageWidth * scale));
            int height = Math.Max(1, (int)Math.Round(pageHeight * scale));

            byte[] pixels = new byte[width * height * 4];
            fixed (byte* buffer = pixels)
            {
                nint bitmap = FPDFBitmap_CreateEx(width, height, BitmapBgra, buffer, width * 4);
                if (bitmap == 0)
                {
                    return null;
                }

                try
                {
                    FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                    FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, RenderAnnotations);
                }
                finally
                {
                    FPDFBitmap_Destroy(bitmap);
                }
            }

            return new BgraImage(width, height, pixels);
        }
        finally
        {
            FPDF_ClosePage(page);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ReadBlock(nint param, uint position, byte* buffer, uint size)
    {
        try
        {
            var stream = (Stream)GCHandle.FromIntPtr(param).Target!;
            stream.Position = position;
            var target = new Span<byte>(buffer, (int)size);
            while (target.Length > 0)
            {
                int read = stream.Read(target);
                if (read <= 0)
                {
                    return 0;
                }

                target = target[read..];
            }

            return 1;
        }
        catch
        {
            return 0; // deadline or I/O failure - PDFium then fails the load/render; nothing may escape into native code
        }
    }

    [LibraryImport(Library)]
    private static partial void FPDF_InitLibrary();

    [LibraryImport(Library)]
    private static partial nint FPDF_LoadCustomDocument(FileAccess* access, byte* password);

    [LibraryImport(Library)]
    private static partial void FPDF_CloseDocument(nint document);

    [LibraryImport(Library)]
    private static partial int FPDF_GetPageCount(nint document);

    [LibraryImport(Library)]
    private static partial nint FPDF_LoadPage(nint document, int index);

    [LibraryImport(Library)]
    private static partial void FPDF_ClosePage(nint page);

    [LibraryImport(Library)]
    private static partial float FPDF_GetPageWidthF(nint page);

    [LibraryImport(Library)]
    private static partial float FPDF_GetPageHeightF(nint page);

    [LibraryImport(Library)]
    private static partial nint FPDFBitmap_CreateEx(int width, int height, int format, byte* buffer, int stride);

    [LibraryImport(Library)]
    private static partial void FPDFBitmap_FillRect(nint bitmap, int left, int top, int width, int height, uint color);

    [LibraryImport(Library)]
    private static partial void FPDF_RenderPageBitmap(nint bitmap, nint page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    [LibraryImport(Library)]
    private static partial void FPDFBitmap_Destroy(nint bitmap);
}
