using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Models;

/// <summary>
/// Shared comma-separated-list tokenizer for bulk-edit "list field" diff/merge
/// (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md §4) - one implementation
/// backing every list-type <see cref="BulkFieldDescriptor"/> instead of one per field.
/// </summary>
public static class ListFieldTokens
{
    public static HashSet<string> Parse(string? value)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return tokens;
        }

        foreach (string token in value.Split(','))
        {
            string trimmed = token.Trim();
            if (trimmed.Length > 0)
            {
                tokens.Add(trimmed);
            }
        }

        return tokens;
    }

    public static string Join(IEnumerable<string> tokens) => string.Join(", ", tokens.OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
}
