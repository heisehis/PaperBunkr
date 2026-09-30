namespace cYo.Common.Text;

/// <summary>
/// Lets the linked <see cref="ExtendedStringComparer"/> compile without Paperbunkr.Common's whole
/// <c>StringUtility</c>. Its only caller is the <c>IgnoreArticles</c> comparison mode, which the thumbnail
/// handler never uses (<see cref="Paperbunkr.ShellThumbnails.CoverImageRules"/> passes <c>IgnoreCase</c> only).
/// </summary>
internal static class StringUtilityShim
{
    public static int IndexAfterArticle(this string s) => 0;
}
