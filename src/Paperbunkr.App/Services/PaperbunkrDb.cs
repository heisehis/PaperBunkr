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
    /// <summary>
    /// Crash-safety pragmas (docs/superpowers/specs/2026-08-29-db-corruption-safeguards-design.md
    /// §1), set on every context: WAL mode moves writes off the main file so a hard kill mid-write
    /// can at worst leave an incomplete <c>-wal</c> tail (auto-discarded on next open) rather than
    /// page-level corruption; <c>synchronous=FULL</c> guarantees zero committed-transaction loss
    /// even on true power loss; <c>busy_timeout</c> is cheap insurance against this app's many
    /// short-lived contexts racing each other into <c>SQLITE_BUSY</c>. <c>journal_mode</c> is sticky
    /// per-file so this is a no-op after the first call, but it's cheap enough to just always run.
    /// </summary>
    public static PaperbunkrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
            .UseSqlite($"Data Source={PaperbunkrDbContext.GetDefaultDatabasePath()}")
            .Options;
        var context = new PaperbunkrDbContext(options);
        context.Database.OpenConnection();
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode = 'WAL';");
        context.Database.ExecuteSqlRaw("PRAGMA synchronous = 'FULL';");
        context.Database.ExecuteSqlRaw("PRAGMA busy_timeout = 5000;");
        context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = 'ON';");
        return context;
    }

    /// <summary>Applies pending migrations (creating the SQLite file on first run) and seeds the built-in system smart lists. The only DB-prep entry point - no demo/placeholder data is ever seeded, so the library only ever contains what the user actually migrates or adds.</summary>
    public static void EnsureCreated()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        SeedSystemSmartLists(context);
        BackfillCharacterIndex(context);

        // Deterministically create the AppSettings singleton row here, synchronously, on a single
        // context - not left to whichever caller happens to touch it first. Confirmed necessary the
        // hard way: App.axaml.cs's post-EnsureCreated auto-backup trigger (docs/superpowers/specs/
        // 2026-08-29-db-corruption-safeguards-design.md §2) runs on a background thread that can
        // race the main thread's own first GetOrCreateAppSettings() call (SkinService.
        // ApplyPersistedSettings()) on a genuinely fresh install - both see no row yet and both try
        // to INSERT Id=1, and the loser throws "UNIQUE constraint failed: AppSettings.Id". Once this
        // row exists, every later GetOrCreateAppSettings() call is a plain SELECT, which is race-free.
        context.GetOrCreateAppSettings();
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
                RootGroup = new SmartListConditionGroup
                {
                    Mode = SmartListGroupMode.And,
                    Conditions =
                    {
                        new() { Field = field, Operator = op, Value = value, Value2 = value2, SortOrder = 0 },
                    },
                },
            });
        }

        context.SaveChanges();
    }
}
