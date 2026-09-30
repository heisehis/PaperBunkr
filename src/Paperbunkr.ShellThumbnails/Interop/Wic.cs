using System.Runtime.InteropServices;

namespace Paperbunkr.ShellThumbnails.Interop;

/// <summary>A top-down 32bpp BGRA image (stride = width * 4), straight (non-premultiplied) alpha.</summary>
internal sealed record BgraImage(int Width, int Height, byte[] Pixels);

/// <summary>
/// Windows Imaging Component, called through raw vtables (see <see cref="ComPtr"/>): decode an encoded image
/// from memory, scale it to fit the requested thumbnail size and convert to 32bpp BGRA - Microsoft's own
/// thumbnail-provider sample pipeline. Codecs are whatever Windows has: JPEG/PNG/GIF/BMP/TIFF built in,
/// WebP/HEIF/AVIF/JPEG XL only with their Store extensions (design decision 16).
/// </summary>
internal static unsafe partial class Wic
{
    /// <summary>WINCODEC_ERR_COMPONENTNOTFOUND - no installed codec understands the bytes.</summary>
    public const int ComponentNotFound = unchecked((int)0x88982F50);

    private static readonly Guid ClsidImagingFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid IidImagingFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    private static readonly Guid PixelFormat32bppBgra = new("6fddc324-4e03-4bfe-b185-3d77768dc90f");

    private const uint ClsctxInprocServer = 1;
    private const int DecodeMetadataCacheOnDemand = 0;
    private const int InterpolationFant = 3;

    // IWICImagingFactory
    private const int FactoryCreateDecoderFromStream = 4;
    private const int FactoryCreateFormatConverter = 10;
    private const int FactoryCreateBitmapScaler = 11;

    // IWICBitmapDecoder
    private const int DecoderGetFrame = 13;

    // IWICBitmapSource (and derived IWICBitmapScaler / IWICFormatConverter)
    private const int SourceGetSize = 3;
    private const int SourceCopyPixels = 7;
    private const int SourceInitialize = 8;

    /// <summary>Decodes <paramref name="encoded"/> and scales it to fit a <paramref name="maxSide"/> square, never enlarging.</summary>
    public static BgraImage DecodeScaled(byte[] encoded, int maxSide)
    {
        using var factory = CreateFactory();
        using var stream = CreateMemoryStream(encoded);

        // Out-parameters need an addressable local; each is then moved into a `using` owner.
        var decoder = new ComPtr();
        ComPtr.Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, int, nint*, int>)factory.Slot(FactoryCreateDecoderFromStream))(
            factory.Pointer, stream.Pointer, null, DecodeMetadataCacheOnDemand, &decoder.Pointer));
        using var ownedDecoder = decoder;

        var frame = new ComPtr();
        ComPtr.Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)ownedDecoder.Slot(DecoderGetFrame))(ownedDecoder.Pointer, 0, &frame.Pointer));
        using var ownedFrame = frame;

        uint width, height;
        ComPtr.Check(((delegate* unmanaged[Stdcall]<nint, uint*, uint*, int>)ownedFrame.Slot(SourceGetSize))(ownedFrame.Pointer, &width, &height));
        (int targetW, int targetH) = FitWithin((int)width, (int)height, maxSide);

        var scaler = new ComPtr();
        ComPtr.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)factory.Slot(FactoryCreateBitmapScaler))(factory.Pointer, &scaler.Pointer));
        using var ownedScaler = scaler;
        ComPtr.Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, uint, int, int>)ownedScaler.Slot(SourceInitialize))(
            ownedScaler.Pointer, ownedFrame.Pointer, (uint)targetW, (uint)targetH, InterpolationFant));

        var converter = new ComPtr();
        ComPtr.Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)factory.Slot(FactoryCreateFormatConverter))(factory.Pointer, &converter.Pointer));
        using var ownedConverter = converter;
        Guid format = PixelFormat32bppBgra;
        ComPtr.Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, int, nint, double, int, int>)ownedConverter.Slot(SourceInitialize))(
            ownedConverter.Pointer, ownedScaler.Pointer, &format, 0, 0, 0.0, 0));

        byte[] pixels = new byte[targetW * targetH * 4];
        fixed (byte* p = pixels)
        {
            ComPtr.Check(((delegate* unmanaged[Stdcall]<nint, void*, uint, uint, byte*, int>)ownedConverter.Slot(SourceCopyPixels))(
                ownedConverter.Pointer, null, (uint)(targetW * 4), (uint)pixels.Length, p));
        }

        return new BgraImage(targetW, targetH, pixels);
    }

    /// <summary>Largest size with the same aspect ratio that fits a <paramref name="maxSide"/> square; never upscales.</summary>
    public static (int Width, int Height) FitWithin(int width, int height, int maxSide)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("Image has no size.");
        }

        if (width <= maxSide && height <= maxSide)
        {
            return (width, height);
        }

        double scale = Math.Min((double)maxSide / width, (double)maxSide / height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static ComPtr CreateFactory()
    {
        Guid clsid = ClsidImagingFactory, iid = IidImagingFactory;
        nint factory;
        ComPtr.Check(CoCreateInstance(&clsid, 0, ClsctxInprocServer, &iid, &factory));
        return new ComPtr(factory);
    }

    private static ComPtr CreateMemoryStream(byte[] data)
    {
        nint stream;
        fixed (byte* p = data)
        {
            stream = SHCreateMemStream(p, (uint)data.Length); // copies the bytes
        }

        if (stream == 0)
        {
            throw new OutOfMemoryException("SHCreateMemStream failed.");
        }

        return new ComPtr(stream);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);

    [LibraryImport("shlwapi.dll")]
    private static partial nint SHCreateMemStream(byte* data, uint size);
}
