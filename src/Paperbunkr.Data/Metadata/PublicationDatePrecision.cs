namespace Paperbunkr.Data.Metadata;

/// <summary>
/// How much of <c>Issue.Year</c>/<c>Month</c>/<c>Day</c> is actually known (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase1-canonical-metadata-design.md §12) - never fabricates precision
/// that isn't there (a Year-only issue reports <see cref="Year"/>, not <see cref="Day"/>).
/// </summary>
public enum PublicationDatePrecision
{
    Unknown,
    Year,
    Month,
    Day,
}
