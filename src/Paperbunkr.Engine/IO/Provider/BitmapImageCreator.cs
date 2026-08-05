using System;
using System.Drawing;
using System.Drawing.Imaging;
using CSJ2K.Util;

namespace cYo.Projects.ComicRack.Engine.IO.Provider
{
	// Written fresh for Paperbunkr. CSJ2K (the JPEG2000 decoder library, referenced via the CSJ2K
	// NuGet package) ships a System.Drawing-based IImageCreator ("BitmapImageCreator") only in its
	// .NET Framework build target; the netstandard1.3 build that net8.0 actually resolves to omits
	// it (System.Drawing isn't universally available on netstandard). ComicRackCE's
	// Jpeg2000Image.cs called that Framework-only helper via `BitmapImageCreator.Register()`
	// followed by `J2kImage.FromBytes(data).As&lt;Bitmap&gt;()`. This reimplements just enough of
	// that registration (IImageCreator -> System.Drawing.Bitmap) against CSJ2K's public
	// CSJ2K.Util.IImageCreator/IImage contract so the same call sites keep working on net8+Windows,
	// where System.Drawing.Common is available.
	internal static class BitmapImageCreator
	{
		public static void Register()
		{
			ImageFactory.Register(new Creator());
		}

		private sealed class Creator : IImageCreator
		{
			public bool IsDefault => true;

			public IImage Create(int width, int height, byte[] data)
			{
				return new BitmapImage(width, height, data);
			}

			public CSJ2K.j2k.image.BlkImgDataSrc ToPortableImageSource(object image)
			{
				throw new NotSupportedException("Encoding from a pre-existing image is not supported by this shim; use CSJ2K's built-in PortableImage path for encode.");
			}
		}

		private sealed class BitmapImage : IImage
		{
			private readonly Bitmap bitmap;

			public BitmapImage(int width, int height, byte[] data)
			{
				bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
				Rectangle rect = new Rectangle(0, 0, width, height);
				BitmapData bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
				try
				{
					int srcStride = width * 3;
					for (int y = 0; y < height; y++)
					{
						IntPtr dest = bmpData.Scan0 + y * bmpData.Stride;
						System.Runtime.InteropServices.Marshal.Copy(data, y * srcStride, dest, srcStride);
					}
				}
				finally
				{
					bitmap.UnlockBits(bmpData);
				}
			}

			public T As<T>()
			{
				if (typeof(T) == typeof(Bitmap) || typeof(T) == typeof(Image))
				{
					return (T)(object)bitmap;
				}
				throw new NotSupportedException($"BitmapImageCreator cannot produce {typeof(T)}");
			}
		}
	}
}
