using System.Runtime.InteropServices;

namespace Paperbunkr.ShellThumbnails.Interop;

/// <summary>BGRA pixels → a top-down 32bpp DIB section, the <c>HBITMAP</c> <c>IThumbnailProvider</c> returns
/// (Windows takes ownership).</summary>
internal static unsafe partial class Gdi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    public static nint CreateBitmap(BgraImage image)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = image.Width,
            Height = -image.Height, // negative = top-down rows, matching BgraImage
            Planes = 1,
            BitCount = 32,
            Compression = 0, // BI_RGB
        };

        void* bits;
        nint bitmap = CreateDIBSection(0, &header, 0, &bits, 0, 0);
        if (bitmap == 0 || bits is null)
        {
            return 0;
        }

        image.Pixels.AsSpan().CopyTo(new Span<byte>(bits, image.Pixels.Length));
        return bitmap;
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint hdc, BitmapInfoHeader* info, uint usage, void** bits, nint section, uint offset);
}
