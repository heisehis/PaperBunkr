namespace Paperbunkr.Data.Entities;

/// <summary>What the last auto-classify pass concluded about a series (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md).</summary>
public enum ContentTypeCheck
{
    /// <summary>Never checked.</summary>
    None,

    /// <summary>Checked; a type was applied or queued as a suggestion.</summary>
    Matched,

    /// <summary>Checked; no provider returned a usable match. Retried after <c>ContentTypeClassificationService.NoMatchRetryDays</c>.</summary>
    NoMatch,

    /// <summary>A person said "not a comic": never searched again.</summary>
    Skipped,
}
