using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// App-side access to the real Paperbunkr library database (docs/onboarding.md §5), replacing
/// the ViewModels' earlier in-memory sample data. Each call to <see cref="CreateContext"/> opens
/// a fresh short-lived <see cref="PaperbunkrDbContext"/> - EF Core contexts aren't meant to be
/// held/shared long-term, and this app has no DI container to own a scoped-context lifetime.
/// </summary>
public static class PaperbunkrDb
{
    public static PaperbunkrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={PaperbunkrDbContext.GetDefaultDatabasePath()}")
            .Options;
        return new PaperbunkrDbContext(options);
    }

    /// <summary>Applies pending migrations (creating the SQLite file on first run) and seeds the built-in system smart lists. The only DB-prep entry point - no demo/placeholder data is ever seeded, so the library only ever contains what the user actually migrates or adds.</summary>
    public static void EnsureCreated()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        SeedSystemSmartLists(context);
        BackfillCharacterIndex(context);
    }

    /// <summary>
    /// One-time backfill of the derived <c>Character</c> index over existing <c>Issue.Characters</c>
    /// text (docs/superpowers/specs/2026-08-27-metadata-model-phase4g-age-progression-design.md).
    /// Guarded on "no Character rows yet" so a normal launch does no work; a library that genuinely
    /// has no character metadata just re-checks a cheap <c>Any()</c> each time.
    /// </summary>
    private static void BackfillCharacterIndex(PaperbunkrDbContext context)
    {
        if (context.Characters.Any())
        {
            return;
        }

        Paperbunkr.Data.Metadata.CharacterResolver.RebuildAll(context);
    }

    /// <summary>
    /// Called by App.axaml.cs to decide fresh-install routing, so it must apply pending
    /// migrations itself first. On a genuinely fresh SQLite file (first launch ever, or the db
    /// was deleted) there's no <c>Series</c> table yet, and querying it without migrating first
    /// throws (confirmed empirically: SqliteException "no such table: Series"), which would
    /// otherwise crash startup before the fresh-install routing ever runs. <c>Database.Migrate()</c>
    /// is idempotent, so the later explicit call to it in <see cref="EnsureCreated"/> is a safe
    /// no-op.
    /// </summary>
    public static bool HasAnySeries()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        return context.Series.Any();
    }

    /// <summary>
    /// Seeds the built-in system smart lists (docs/superpowers/specs/2026-08-06-smart-lists-design.md
    /// §5), idempotent on <c>IsSystem</c> rows already existing. Every rule value is taken directly
    /// from ComicRackCE's actual default-list source (<c>ComicLibrary.cs</c>/
    /// <c>EngineConfiguration.cs</c>), not invented — see the spec for citations.
    /// </summary>
    private static void SeedSystemSmartLists(PaperbunkrDbContext context)
    {
        if (context.SmartLists.Any(s => s.IsSystem))
        {
            return;
        }

        var systemLists = new (string Name, SmartListField Field, SmartListOperator Operator, string Value, string? Value2)[]
        {
            ("My Favorites", SmartListField.Rating, SmartListOperator.GreaterThan, "3", null),
            ("Recently Added", SmartListField.Added, SmartListOperator.WithinLastDays, "14", null),
            ("Recently Read", SmartListField.Opened, SmartListOperator.WithinLastDays, "14", null),
            ("Never Read", SmartListField.ReadPercentage, SmartListOperator.LessThan, "10", null),
            ("Reading", SmartListField.ReadPercentage, SmartListOperator.InRange, "10", "95"),
            ("Read", SmartListField.ReadPercentage, SmartListOperator.GreaterThan, "95", null),
            ("Missing Files", SmartListField.IsMissing, SmartListOperator.Is, "true", null),
            ("Duplicate Candidates", SmartListField.Duplicate, SmartListOperator.Is, "true", null),
        };

        int sortOrder = 0;
        foreach (var (name, field, op, value, value2) in systemLists)
        {
            context.SmartLists.Add(new SmartList
            {
                Name = name,
                IsSystem = true,
                SortOrder = sortOrder++,
                Conditions = new List<SmartListCondition>
                {
                    new() { Field = field, Operator = op, Value = value, Value2 = value2, SortOrder = 0 },
                },
            });
        }

        context.SaveChanges();
    }
}
