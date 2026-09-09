using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using PDFiumSharp;
using PDFiumSharp.Enums;
using cYo.Common.Drawing;
using cYo.Projects.ComicRack.Engine.IO.Provider.Native;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Readers.Pdf
{
    public class PdfiumReaderEngine : IComicAccessor
    {
        // PDFiumSharpV2 P/Invokes against "pdfium_x64"/"pdfium_x86"/"pdfium_arm64" (its own
        // per-arch DllImport names - see PDFiumSharp.PDFium.PlatformInvoke), but the native binary
        // actually bundled in this project is bblanchon.PDFium.Win32's plain "pdfium.dll" (the
        // same one NativeInterop's own "pdfium" resolver already finds for PdfiumNative.cs's raw
        // shim). Those two names never matched, so PDFiumSharpV2 silently failed to load the
        // native library for every real PDF - confirmed via a live repro against real e-book PDFs
        // (DllNotFoundException: "Unable to load DLL 'pdfium_x64'"), swallowed somewhere upstream
        // by ComicProvider.Open() into a silent Count == 0 rather than a visible crash. Redirects
        // PDFiumSharpV2's own DllImport names to the same already-resolved pdfium.dll instead of
        // adding a second, differently-named native binary.
        static PdfiumReaderEngine()
        {
            string resolvedPath = NativeInterop.ResolveNativeAssetPath(typeof(PdfDocument).Assembly, "pdfium.dll");

            NativeLibrary.SetDllImportResolver(typeof(PdfDocument).Assembly, (name, requestingAssembly, searchPath) =>
            {
                if (name != "pdfium_x64" && name != "pdfium_x86" && name != "pdfium_arm64")
                {
                    return IntPtr.Zero;
                }

                return NativeLibrary.TryLoad(resolvedPath, out IntPtr handle) ? handle : IntPtr.Zero;
            });
        }

        public IEnumerable<ProviderImageInfo> GetEntryList(string source)
        {
            using (var pdfDocument = new PdfDocument(source))
            {
                for (int i = 0; i < pdfDocument.Pages.Count; i++)
                {
                    using (PdfPage pdfPage = pdfDocument.Pages[i])
                    {
                        yield return new ProviderImageInfo(i);
                    }
                }
            }

        }

        public bool IsFormat(string source)
        {
            throw new NotImplementedException();
        }

        public byte[] ReadByteImage(string source, ProviderImageInfo info) => RenderPageToJpeg(new PdfDocument(source), info.Index, disposeDoc: true);

        private byte[] RenderPageToJpeg(PdfDocument doc, int index, bool disposeDoc)
        {
            try
            {
                using (PdfPage pdfPage = doc.Pages[index])
                {
                    Size size = CalculateSize(pdfPage.Width, pdfPage.Height);
                    using (Bitmap bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb))
                    {
                        pdfPage.Render(bitmap);
                        return bitmap.ImageToBytes(ImageFormat.Jpeg);
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (disposeDoc)
                {
                    ((System.IDisposable)doc).Dispose();
                }
            }
        }

        // Keep-open reading session (docs/superpowers/specs/2026-09-08-reader-decode-cache-prefetch-
        // pipeline-design.md §4.3): one PdfDocument (FPDF_DOCUMENT) held for the whole reading
        // session instead of re-parsing the xref table on every page. PDFium is not thread-safe -
        // the pipeline serialises all reads on one thread. The session's "entry name" is the page
        // index as a string (a PDF has no per-page names).
        public bool SupportsSession => true;

        public IComicAccessorSession OpenSession(string source)
        {
            try
            {
                return new PdfiumAccessorSession(this, new PdfDocument(source));
            }
            catch
            {
                return null;
            }
        }

        private sealed class PdfiumAccessorSession : IComicAccessorSession
        {
            private readonly PdfiumReaderEngine _owner;
            private readonly PdfDocument _doc;

            public PdfiumAccessorSession(PdfiumReaderEngine owner, PdfDocument doc)
            {
                _owner = owner;
                _doc = doc;
            }

            public int Count => _doc.Pages.Count;

            public byte[] ReadEntryBytes(string entryName)
            {
                if (!int.TryParse(entryName, out int index) || index < 0 || index >= _doc.Pages.Count)
                {
                    return null;
                }
                return _owner.RenderPageToJpeg(_doc, index, disposeDoc: false);
            }

            public void Dispose() => ((System.IDisposable)_doc).Dispose();
        }

        private Size CalculateSize(double width, double height)
        {
            Size maxSize = EngineConfiguration.Default.PdfiumImageSize;

            //The width & height are returned in point (1/72 inch)
            //but PDFs created by CR will have the wrong page size. Which would mean that opening this PDF would mean the resolution would balloon up.
            //To prevent from the above mentioned ballooning, this will be the MAX resolution
            int maxWidth = maxSize.Width; //1920 is 8.5in at 225dpi
            int maxHeight = maxSize.Height; //2540 is 11in at 225dpi

            //Calculate the width based on the max height
            int targeWidth = (int)((width * maxHeight) / height);
            //Calculate the height based on the max width
            int targeHeight = (int)((height * maxWidth) / width);

            //if the page is a landscape page (width > height), use the max height, if not we use the max width
            Size outSize = width > height ? new Size(targeWidth, maxHeight) : new Size(maxWidth, targeHeight);

            return outSize;
        }

        public T ReadInfo<T>(string source) where T : ComicInfo => null;

        public bool WriteInfo(string source, ComicInfo info) => false;
    }
}
