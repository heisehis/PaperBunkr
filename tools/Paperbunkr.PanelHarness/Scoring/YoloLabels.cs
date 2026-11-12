using System.Globalization;
using System.Text;
using Paperbunkr.App.Services.Reader.Panels;

namespace Paperbunkr.PanelHarness.Scoring;

/// <summary>
/// YOLO detection label files: one line per box, <c>class cx cy w h</c>, all normalised to the image. The panel model has one class (0 = panel);
/// lines of any other class are ignored, so a two-class file from an older export still reads as its panels.
/// </summary>
internal static class YoloLabels
{
    public const int PanelClass = 0;

    public static List<PanelRect> Read(string path)
    {
        var boxes = new List<PanelRect>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var parts = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int cls) || cls != PanelClass)
            {
                continue;
            }

            double cx = D(parts[1]), cy = D(parts[2]), w = D(parts[3]), h = D(parts[4]);
            boxes.Add(Clamp(new PanelRect(cx - (w / 2), cy - (h / 2), w, h)));
        }

        return boxes;
    }

    public static void Write(string path, IEnumerable<PanelRect> boxes)
    {
        var sb = new StringBuilder();
        foreach (var b in boxes.Select(Clamp))
        {
            sb.Append(PanelClass).Append(' ')
              .Append(F(b.X + (b.Width / 2))).Append(' ')
              .Append(F(b.Y + (b.Height / 2))).Append(' ')
              .Append(F(b.Width)).Append(' ')
              .Append(F(b.Height)).Append('\n');
        }

        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>Keeps a box inside the page (labelling tools can leave a pixel or two outside).</summary>
    public static PanelRect Clamp(PanelRect r)
    {
        double x = Math.Clamp(r.X, 0, 1), y = Math.Clamp(r.Y, 0, 1);
        double right = Math.Clamp(r.Right, 0, 1), bottom = Math.Clamp(r.Bottom, 0, 1);
        return new PanelRect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static double D(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}
