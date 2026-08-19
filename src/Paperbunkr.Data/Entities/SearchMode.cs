namespace Paperbunkr.Data.Entities;

/// <summary>
/// Library search box's field scope, matching CE's real QuickSearch mode dropdown
/// (<c>ComicBookAllPropertiesMatcher.MatcherOption</c> in
/// <c>_reference/ComicRackCE/ComicRack.Engine/Metadata/ComicBook/Matcher/
/// ComicBookAllPropertiesMatcher.cs:138-230</c>) - each mode searches a fixed field set. All of
/// CE's underlying fields already exist on <see cref="Issue"/>, so this needed no schema change
/// beyond the mode itself.
/// </summary>
public enum SearchMode
{
    All,
    Series,
    Writer,
    Artists,
    Descriptive,
    File,
    Catalog,
}
