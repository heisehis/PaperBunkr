namespace Paperbunkr.Data.Entities;

/// <summary>
/// One alternate title for a <see cref="Series"/> - native-script, romanized, or a foreign-language
/// localization (docs/superpowers/specs/2026-08-19-metadata-model-multi-value-titles-design.md).
/// <see cref="Series.Name"/> remains the single primary/sort title everywhere it already is (Library
/// grid, sort, existing search) - this table is purely additive alternates, so nothing that already
/// reads <c>Series.Name</c> needs to change. Deliberately lean (no <c>Language</c>/<c>Source</c>/
/// <c>Preferred</c> columns): the only consumer this pass is search, which needs the value and enough
/// of a label to distinguish rows, nothing more - an over-engineered generic title-value-object was
/// one of the concrete over-engineering findings the originating architecture review flagged.
/// </summary>
public class SeriesTitle
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public Series? Series { get; set; }

    public string Value { get; set; } = string.Empty;

    public SeriesTitleType Type { get; set; }
}
