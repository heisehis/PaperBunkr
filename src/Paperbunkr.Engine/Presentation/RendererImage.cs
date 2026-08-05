using System.Drawing;

namespace cYo.Common.Presentation
{
	// Ported from ComicRackCE's cYo.Common.Presentation (a WinForms-adjacent project overall,
	// excluded per the porting brief). This one class is a pure System.Drawing.Bitmap wrapper with
	// no System.Windows.Forms dependency at all -- pulled into Paperbunkr.Engine because
	// ThumbnailImage/PageImage's shared base (MemoryOptimizedImage) genuinely needs it for core
	// image-cache memory accounting, not for any UI concern. See docs/retarget_spike_report.md.
	public abstract class RendererImage
	{
		public abstract Bitmap Bitmap { get; }

		public virtual bool IsValid => Bitmap != null;

		public virtual Size Size => Bitmap.Size;

		public int Width => Size.Width;

		public int Height => Size.Height;

		public override bool Equals(object obj)
		{
			if (obj == null)
			{
				return false;
			}
			if (obj.GetType() != GetType())
			{
				return false;
			}
			return ((RendererImage)obj).Bitmap == Bitmap;
		}

		public override int GetHashCode()
		{
			return Bitmap.GetHashCode();
		}

		public static implicit operator Bitmap(RendererImage image)
		{
			return image.Bitmap;
		}

		public static implicit operator RendererImage(Bitmap image)
		{
			return new RendererGdiImage(image);
		}
	}
}
