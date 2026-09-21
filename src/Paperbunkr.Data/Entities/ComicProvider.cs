namespace Paperbunkr.Data.Entities;

/// <summary>
/// Which comic database a tracked series, its issues and their wants come from (docs/superpowers/specs/2026-09-20-metron-as-comicvine-alternative-design.md).
/// Chosen per series; ids are only unique within a provider, so every stored external id travels with its provider.
/// </summary>
public enum ComicProvider
{
    ComicVine = 0,
    Metron = 1,
}
