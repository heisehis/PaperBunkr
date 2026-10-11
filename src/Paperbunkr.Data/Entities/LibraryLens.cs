namespace Paperbunkr.Data.Entities;

/// <summary>
/// The Library's reading-state tab (docs/superpowers/specs/2026-10-04-library-redesign-design.md, Slice 1). CE's "Show only"
/// filter has the same four values (<c>ShowOptionType</c>: All, Read, Reading, Unread) as menu toggles; here they are tabs,
/// and for a series they are mutually exclusive so the three counts add up to <see cref="All"/>: <see cref="Unread"/> means
/// no issue has been opened, <see cref="Read"/> means every issue is read, <see cref="Reading"/> is everything in between.
/// For a single issue they are unread, in progress and read. <see cref="All"/> is first so it is the CLR default.
/// </summary>
public enum LibraryLens
{
    All,
    Reading,
    Unread,
    Read,
}
