using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Paperbunkr.ShellThumbnails.Interop;

namespace Paperbunkr.ShellThumbnails.Com;

[GeneratedComInterface]
[Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")] // IID_IInitializeWithStream (b7d14566-... is IInitializeWithFile)
internal partial interface IInitializeWithStream
{
    [PreserveSig]
    int Initialize(nint stream, uint mode);
}

[GeneratedComInterface]
[Guid("e357fccd-a995-4576-b01f-234630154e96")]
internal partial interface IThumbnailProvider
{
    [PreserveSig]
    int GetThumbnail(uint size, out nint bitmap, out int alphaType);
}

/// <summary>
/// The object Windows' thumbnail surrogate creates per file (docs/superpowers/specs/2026-09-30-explorer-cover-
/// thumbnails-design.md §1). <c>Initialize</c> only keeps the IStream - reading it there would download a cloud
/// placeholder file just to be asked for nothing. <c>GetThumbnail</c> does everything on the calling thread under
/// the request deadline and reports failure with <c>E_FAIL</c>, which makes Windows show its default icon.
/// </summary>
[GeneratedComClass]
[Guid(ClsidString)]
internal sealed partial class ThumbnailProvider : IInitializeWithStream, IThumbnailProvider
{
    public const string ClsidString = "7fd4d2e6-ec36-4ed3-88b8-9360ce98535b";

    public static readonly Guid Clsid = new(ClsidString);

    private const int SOk = 0;
    private const int EFail = unchecked((int)0x80004005);
    private const int EUnexpected = unchecked((int)0x8000FFFF);
    private const int ErrorAlreadyInitialized = unchecked((int)0x800704DF);
    private const int WtsatArgb = 2;

    /// <summary>Managed heap size above which a request ends with a full GC. The surrogate lives for hours; without
    /// this, large-object-heap garbage from one big cover can keep it at gigabytes (the tianwen report).</summary>
    private const long CollectAboveBytes = 64L * 1024 * 1024;

    private ComStream? _stream;

    public int Initialize(nint stream, uint mode)
    {
        if (_stream is not null)
        {
            return ErrorAlreadyInitialized;
        }

        if (stream == 0)
        {
            return EUnexpected;
        }

        Marshal.AddRef(stream);
        _stream = new ComStream(stream);
        return SOk;
    }

    public int GetThumbnail(uint size, out nint bitmap, out int alphaType)
    {
        bitmap = 0;
        alphaType = 0;
        if (_stream is null)
        {
            return EUnexpected;
        }

        try
        {
            var deadline = new Deadline(Deadline.DefaultBudget);
            string? name = TryGetName(_stream);
            string? extension = FormatSniffer.ExtensionFor(name, _stream);
            if (extension is null)
            {
                HandlerTrace.Write($"'{name}': unrecognised format");
                return EFail;
            }

            var image = ThumbnailRenderer.Render(_stream, extension, (int)Math.Max(1, size), deadline);
            if (image is null)
            {
                HandlerTrace.Write($"'{name}' ({extension}, {size}px): no image");
                return EFail;
            }

            bitmap = Gdi.CreateBitmap(image);
            alphaType = WtsatArgb;
            HandlerTrace.Write($"'{name}' ({extension}, {size}px): {image.Width}x{image.Height}");
            return bitmap == 0 ? EFail : SOk;
        }
        catch (Exception ex)
        {
            HandlerTrace.Write($"GetThumbnail failed: {ex}");
            return EFail; // nothing may escape back into the surrogate
        }
        finally
        {
            _stream.Dispose();
            _stream = null;
            TrimHeap();
        }
    }

    private static string? TryGetName(ComStream stream)
    {
        try
        {
            return stream.Name;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static void TrimHeap()
    {
        if (GC.GetTotalMemory(forceFullCollection: false) > CollectAboveBytes)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
    }
}
