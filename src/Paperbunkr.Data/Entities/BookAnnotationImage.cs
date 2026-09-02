namespace Paperbunkr.Data.Entities;

/// <summary>
/// A captured rectangular region of a <see cref="Book"/> PDF page (docs/superpowers/specs/
/// 2026-09-01-books-reader-ergonomics-and-annotations-design.md) - cropped to a standalone PNG under
/// <c>%AppData%\Paperbunkr\annotations\</c>, referenced here by <see cref="ImagePath"/>.
/// <see cref="RectX"/>/<see cref="RectY"/>/<see cref="RectWidth"/>/<see cref="RectHeight"/> are
/// fractions (0-1) of the page's own width/height, not pixels - stays correct regardless of the
/// zoom level the page was rendered at when captured.
/// </summary>
public class BookAnnotationImage
{
    public int Id { get; set; }

    public int BookId { get; set; }

    public Book? Book { get; set; }

    public int PageIndex { get; set; }

    public double RectX { get; set; }

    public double RectY { get; set; }

    public double RectWidth { get; set; }

    public double RectHeight { get; set; }

    public string ImagePath { get; set; } = string.Empty;

    public string? Note { get; set; }

    public DateTime CreatedTime { get; set; }
}
