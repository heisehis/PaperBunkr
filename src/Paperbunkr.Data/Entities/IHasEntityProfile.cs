namespace Paperbunkr.Data.Entities;

/// <summary>
/// The image and profile fields shared by <see cref="Character"/>, <see cref="Team"/> and <see cref="Creator"/>
/// (docs/superpowers/specs/2026-11-10-series-structure-and-discovery-design.md §3.3), so one code path fills and shows them.
/// </summary>
public interface IHasEntityProfile
{
    int Id { get; }

    string Name { get; }

    string? ImageUrl { get; set; }

    EntityImageSource ImageSource { get; set; }

    DateTime? ImageCheckedAt { get; set; }

    string? CustomImagePath { get; set; }

    string? Description { get; set; }

    string? Aliases { get; set; }
}
