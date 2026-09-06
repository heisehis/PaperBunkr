using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Scheduling;

/// <summary>
/// Reads, seeds and writes <see cref="ScheduledTaskState"/> rows
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1).
/// Same short-lived-context convention as <c>ActivityHistoryStore</c>. Three tasks mirror
/// pre-existing <see cref="AppSettings"/> columns so the old backup / sweep / cover-verify code
/// keeps working - reads overlay the legacy value, writes go to both.
/// </summary>
public class ScheduledRunStore
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public ScheduledRunStore()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    internal ScheduledRunStore(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>Insert a row for every catalog task that doesn't have one yet, using descriptor defaults.</summary>
    public void SeedMissing(IEnumerable<ScheduledTaskDescriptor> catalog)
    {
        try
        {
            using var context = _contextFactory();
            var existing = context.ScheduledTaskStates.Select(s => s.TaskId).ToHashSet();
            bool added = false;

            foreach (var d in catalog)
            {
                if (existing.Contains(d.Id))
                {
                    continue;
                }

                context.ScheduledTaskStates.Add(new ScheduledTaskState
                {
                    TaskId = d.Id,
                    Enabled = SeedEnabled(context, d),
                    Mode = d.DefaultMode,
                    IntervalHours = (int)Math.Round(d.DefaultInterval.TotalHours),
                    DailyAtMinutes = 3 * 60,
                });
                added = true;
            }

            if (added)
            {
                context.SaveChanges();
            }
        }
        catch (Exception)
        {
            // Best-effort - the scheduler runs from in-memory catalog defaults for the session.
        }
    }

    private static bool SeedEnabled(PaperbunkrDbContext context, ScheduledTaskDescriptor d)
    {
        if (d.Id == ScheduledTaskCatalog.DbBackup)
        {
            return context.GetOrCreateAppSettings().AutoBackupEnabled;
        }

        return d.DefaultEnabled;
    }

    /// <summary>Load every state row, with legacy-column values overlaid onto the mirrored tasks.</summary>
    public IReadOnlyList<ScheduledTaskState> LoadAll()
    {
        try
        {
            using var context = _contextFactory();
            var rows = context.ScheduledTaskStates.AsNoTracking().ToList();
            var settings = context.GetOrCreateAppSettings();

            foreach (var row in rows)
            {
                switch (row.TaskId)
                {
                    case ScheduledTaskCatalog.DbBackup:
                        row.Enabled = settings.AutoBackupEnabled;
                        row.IntervalHours = settings.AutoBackupMinIntervalHours;
                        break;
                    case ScheduledTaskCatalog.ContentTypeSweep:
                        row.LastRunUtc = MaxNullable(row.LastRunUtc, settings.LastContentTypeSweepUtc);
                        break;
                    case ScheduledTaskCatalog.VerifyCovers:
                        row.LastRunUtc = MaxNullable(row.LastRunUtc, settings.LastCoverVerificationUtc);
                        break;
                }
            }

            return rows;
        }
        catch (Exception)
        {
            return Array.Empty<ScheduledTaskState>();
        }
    }

    public ScheduledTaskState? Load(string taskId) =>
        LoadAll().FirstOrDefault(s => s.TaskId == taskId);

    public void SetSchedule(string taskId, ScheduleMode mode, int intervalHours, int dailyAtMinutes)
    {
        Mutate(taskId, (row, settings) =>
        {
            row.Mode = mode;
            row.IntervalHours = Math.Max(1, intervalHours);
            row.DailyAtMinutes = Math.Clamp(dailyAtMinutes, 0, 1439);
            if (taskId == ScheduledTaskCatalog.DbBackup)
            {
                settings.AutoBackupMinIntervalHours = row.IntervalHours;
            }
        });
    }

    public void SetEnabled(string taskId, bool enabled)
    {
        Mutate(taskId, (row, settings) =>
        {
            row.Enabled = enabled;
            if (taskId == ScheduledTaskCatalog.DbBackup)
            {
                settings.AutoBackupEnabled = enabled;
            }
        });
    }

    /// <summary>Stamp a completed (or failed) run. Skipped cycles are not recorded.</summary>
    public void RecordRun(string taskId, ScheduledRunStatus status, DateTime whenUtc)
    {
        if (status == ScheduledRunStatus.Skipped)
        {
            return;
        }

        Mutate(taskId, (row, settings) =>
        {
            row.LastRunUtc = whenUtc;
            row.LastRunStatus = status;
            if (taskId == ScheduledTaskCatalog.ContentTypeSweep)
            {
                settings.LastContentTypeSweepUtc = whenUtc;
            }
            else if (taskId == ScheduledTaskCatalog.VerifyCovers)
            {
                settings.LastCoverVerificationUtc = whenUtc;
            }
        });
    }

    private void Mutate(string taskId, Action<ScheduledTaskState, AppSettings> apply)
    {
        try
        {
            using var context = _contextFactory();
            var row = context.ScheduledTaskStates.FirstOrDefault(s => s.TaskId == taskId);
            if (row is null)
            {
                var d = ScheduledTaskCatalog.Find(taskId);
                row = new ScheduledTaskState
                {
                    TaskId = taskId,
                    Mode = d?.DefaultMode ?? ScheduleMode.Interval,
                    IntervalHours = d is null ? 24 : (int)Math.Round(d.DefaultInterval.TotalHours),
                    DailyAtMinutes = 3 * 60,
                    Enabled = d?.DefaultEnabled ?? false,
                };
                context.ScheduledTaskStates.Add(row);
            }

            apply(row, context.GetOrCreateAppSettings());
            context.SaveChanges();
        }
        catch (Exception)
        {
            // Best-effort persistence.
        }
    }

    private static DateTime? MaxNullable(DateTime? a, DateTime? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        return a.Value >= b.Value ? a : b;
    }
}
