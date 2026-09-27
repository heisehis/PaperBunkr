using System;
using System.Collections.Generic;
using System.Linq;
using FluentIcons.Common;

namespace Paperbunkr.App.Services;

/// <summary>One bundled legal/notice document shown under About → Legal &amp; notices.</summary>
/// <param name="FileName">The file next to the exe (copied there by the csproj), also the name other documents link to.</param>
/// <param name="Preformatted">Plain text shown verbatim (<c>LICENSE</c>) rather than parsed as markdown.</param>
public sealed record LegalDocument(string FileName, string Title, string Description, Symbol Icon, bool Preformatted = false);

/// <summary>
/// The documents the About section lists and the viewer opens (docs/superpowers/specs/2026-09-26-about-polish-design.md §5-6),
/// described once so the rows, the viewer title and in-document links all agree.
/// </summary>
public static class LegalDocuments
{
    public static IReadOnlyList<LegalDocument> All { get; } =
    [
        new("LICENSE", "License", "GNU AGPL v3, the license the source code is released under", Symbol.DocumentText, Preformatted: true),
        new("PRIVACY.md", "Privacy notice", "What stays on your computer and what leaves it", Symbol.Shield),
        new("TERMS.md", "Terms of use", "Plain-language terms for using the app", Symbol.DocumentText),
        new("COMICVINE_NOTICE.md", "ComicVine & Metron notice", "Your own key or login, request limits, and data ownership", Symbol.Warning),
        new("THIRD-PARTY-NOTICES.md", "Open-source notices", "The open-source software, fonts and artwork Paperbunkr includes", Symbol.BookOpen),
    ];

    /// <summary>The document with this file name (case-insensitive, a leading <c>./</c> ignored), or null.</summary>
    public static LegalDocument? Find(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        string name = fileName.Trim();
        if (name.StartsWith("./", StringComparison.Ordinal))
        {
            name = name[2..];
        }

        return All.FirstOrDefault(d => string.Equals(d.FileName, name, StringComparison.OrdinalIgnoreCase));
    }
}
