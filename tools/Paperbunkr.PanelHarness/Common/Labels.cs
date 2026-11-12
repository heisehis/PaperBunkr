namespace Paperbunkr.PanelHarness.Common;

internal static class Labels
{
    /// <summary>
    /// Where a split's YOLO labels live: <c>images/labels</c>, the folder X-AnyLabeling's "Export YOLO Annotations" writes to by default
    /// (checked 2026-11-07 against its user guide), so pre-labels, corrected labels and the scorer all use one folder.
    /// </summary>
    public static string DefaultDir(string split) => Path.Combine(split, "images", "labels");

    /// <summary>The class list X-AnyLabeling asks for on import and export: one name per line, line number = class id.</summary>
    public static void WriteClasses(string labelsDir) => File.WriteAllText(Path.Combine(labelsDir, "classes.txt"), "panel\n");
}
