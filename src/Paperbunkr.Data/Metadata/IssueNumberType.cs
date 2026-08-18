namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Classifies <c>Issue.Number</c>/<c>Volume</c>'s raw string shape (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase1-canonical-metadata-design.md §10) - derived only, never stored.
/// See <see cref="IssueMetadataExtensions.NumberType"/> for the exact classification rules.
/// </summary>
public enum IssueNumberType
{
    Unknown,
    Numeric,
    Fraction,
    Annual,
    Special,
    AlphaNumeric,
    Text,
}
