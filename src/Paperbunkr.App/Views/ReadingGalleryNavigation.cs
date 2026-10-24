using System;

namespace Paperbunkr.App.Views;

/// <summary>
/// Up/Down through the gallery's two bands - <paramref name="folders"/> compact folder cards (<c>folderColumns</c> across), then the wide list
/// cards (<c>listColumns</c> across) - where a plain "index ± columns" would drift at the seam. Pure index maths so it can be tested without a window.
/// </summary>
internal static class ReadingGalleryNavigation
{
    /// <summary>Where Up or Down from <paramref name="index"/> lands. <c>Leaves</c> is set when Up runs off the top row (the caller hands over to the
    /// Continue row or header); a null <c>Target</c> without it means there is nowhere further to go.</summary>
    public static (int? Target, bool Leaves) Vertical(bool down, int index, int folders, int total, int folderColumns, int listColumns)
    {
        folderColumns = Math.Max(1, folderColumns);
        listColumns = Math.Max(1, listColumns);
        int lists = total - folders;

        if (index < folders)
        {
            int row = index / folderColumns, column = index % folderColumns;
            if (!down)
            {
                return row == 0 ? (null, true) : (index - folderColumns, false);
            }

            if (index + folderColumns < folders)
            {
                return (index + folderColumns, false);
            }

            if (row < (folders - 1) / folderColumns)
            {
                return (folders - 1, false);   // the next folder row is shorter: its last card
            }

            return lists == 0 ? (null, false) : (folders + Math.Min(lists - 1, Math.Min(listColumns - 1, column * listColumns / folderColumns)), false);
        }

        int listIndex = index - folders;
        int listRow = listIndex / listColumns, listColumn = listIndex % listColumns;
        if (down)
        {
            if (listIndex + listColumns < lists)
            {
                return (index + listColumns, false);
            }

            return listRow < (lists - 1) / listColumns ? (total - 1, false) : (null, false);
        }

        if (listRow > 0)
        {
            return (index - listColumns, false);
        }

        if (folders == 0)
        {
            return (null, true);
        }

        int lastFolderRow = (folders - 1) / folderColumns * folderColumns;
        return (Math.Min(folders - 1, lastFolderRow + Math.Min(folderColumns - 1, listColumn * folderColumns / listColumns)), false);
    }

    /// <summary>Whether <paramref name="index"/> is in the left-most column of its band (Left from there is left for the nav rail).</summary>
    public static bool IsFirstColumn(int index, int folders, int folderColumns, int listColumns) =>
        index < folders ? index % Math.Max(1, folderColumns) == 0 : (index - folders) % Math.Max(1, listColumns) == 0;
}
