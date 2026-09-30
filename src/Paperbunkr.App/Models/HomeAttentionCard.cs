using Avalonia.Media;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Models;

/// <summary>
/// Home's Needs Attention banner (docs/superpowers/specs/2026-09-28-home-improvements-design.md I3): the resolver's pick plus the
/// series cover, resolved in the background snapshot pass.
/// </summary>
public sealed class HomeAttentionCard
{
    public required HomeAttention Attention { get; init; }

    public required IBrush CoverBrush { get; init; }

    public IImage? CoverImage { get; init; }

    public string Headline => Attention.Headline;

    public string Reason => Attention.Reason;

    public string ActionLabel => Attention.ActionLabel;
}
