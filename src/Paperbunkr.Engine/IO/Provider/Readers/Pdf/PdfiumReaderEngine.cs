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

        /// <summary>Test seam (rev-3 addendum, design §15 #2): invoked with the page index each time a <see cref="PdfPage"/> is loaded (an <c>FPDF_LoadPage</c> - content-stream + resource-dict parse), so a test can assert the session's page cache avoids reloading.</summary>
        internal static Action<int> OnPageLoaded;

        public byte[] ReadByteImage(string source, ProviderImageInfo info)
        {
            var doc = new PdfDocument(source);
            try
            {
                OnPageLoaded?.Invoke(info.Index);
                using (PdfPage pdfPage = doc.Pages[info.Index])
                {
                    return RenderPage(pdfPage);
                }
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                ((System.IDisposable)doc).Dispose();
            }
        }

        /// <summary>Rasterises an already-loaded page to a JPEG byte[]; the caller owns the <see cref="PdfPage"/> lifetime (stateless path disposes it per call, the session keeps a small LRU - design §15 #2).</summary>
        private byte[] RenderPage(PdfPage pdfPage)
        {
            try
            {
                Size size = CalculateSize(pdfPage.Width, pdfPage.Height);
                using (Bitmap bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb))
                {
                    pdfPage.Render(bitmap);
                    return bitmap.ImageToBytes(ImageFormat.Jpeg);
                }
            }
            catch (Exception)
            {
                return null;
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
            // FPDF_LoadPage parses the page's content stream + resource dict; the prefetch pass and
            // a later detail render (design §6.2) of the same page would each pay it. Keep a tiny
            // LRU of open PdfPage objects - the current page and its immediate neighbours (the paged
            // window) stay resident during a forward flip without holding the whole document open
            // (design §15 #2). All access is on the pipeline's single reader thread (PDFium affinity).
            private const int MaxOpenPages = 3;

            private readonly PdfiumReaderEngine _owner;
            private readonly PdfDocument _doc;
            private readonly Dictionary<int, PdfPage> _pages = new Dictionary<int, PdfPage>();
            private readonly LinkedList<int> _lru = new LinkedList<int>(); // front = most recent

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

                try
                {
                    return _owner.RenderPage(GetOrLoadPage(index));
                }
                catch (Exception)
                {
                    return null;
                }
            }

            private PdfPage GetOrLoadPage(int index)
            {
                if (_pages.TryGetValue(index, out PdfPage cached))
                {
                    _lru.Remove(index);
                    _lru.AddFirst(index);
                    return cached;
                }

                OnPageLoaded?.Invoke(index);
                PdfPage page = _doc.Pages[index];
                _pages[index] = page;
                _lru.AddFirst(index);

                while (_lru.Count > MaxOpenPages)
                {
                    int evict = _lru.Last.Value;
                    _lru.RemoveLast();
                    if (_pages.Remove(evict, out PdfPage stale))
                    {
                        try { stale.Dispose(); } catch { } // FPDF_ClosePage
                    }
                }

                return page;
            }

            public void Dispose()
            {
                foreach (PdfPage page in _pages.Values)
                {
                    try { page.Dispose(); } catch { }
                }
                _pages.Clear();
                _lru.Clear();
                ((System.IDisposable)_doc).Dispose();
            }
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
