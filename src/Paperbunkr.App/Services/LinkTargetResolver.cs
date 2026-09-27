using System;

namespace Paperbunkr.App.Services;

public enum LinkTargetKind
{
    /// <summary>An http(s) address - opened in the default browser.</summary>
    Web,

    /// <summary>One of the bundled <see cref="LegalDocuments"/> - opened in the in-app viewer.</summary>
    Document,

    /// <summary>Anything else (anchors, mailto:, unknown files) - ignored.</summary>
    Ignored,
}

/// <summary>What clicking a <see cref="MarkdownLite"/> link does (docs/superpowers/specs/2026-09-26-about-polish-design.md §2).</summary>
public static class LinkTargetResolver
{
    public static LinkTargetKind Resolve(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return LinkTargetKind.Ignored;
        }

        if (Uri.TryCreate(target.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return LinkTargetKind.Web;
        }

        return LegalDocuments.Find(target) is null ? LinkTargetKind.Ignored : LinkTargetKind.Document;
    }
}
