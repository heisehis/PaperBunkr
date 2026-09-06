using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <summary>
    /// The maintenance scheduler's per-task state table + the notification-level setting
    /// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 1).
    ///
    /// <para>
    /// The standalone <c>AppSettings.ScanFoldersOnStartup</c> checkbox is retired - the scheduler's
    /// <c>library-scan</c> / <c>book-scan</c> tasks replace it - and its C# property is removed. The
    /// <b>column is left in place as a dormant column</b> rather than dropped: EF's SQLite
    /// column-drop rebuilds the whole table against the migration's own column list, and a later
    /// migration's table rebuild that still expects the column then fails
    /// (project_paperbunkr_migration_rollback_orphan_column_bug, the same hazard in reverse). The
    /// migration only <i>reads</i> the old value here, to carry it into the two folder-scan task
    /// rows.
    /// </para>
    ///
    /// <para>
    /// <b>Down</b> drops only the new table. It does <b>not</b> drop
    /// <c>ScheduledTaskNotificationLevel</c> - an <c>AppSettings</c> column drop forces EF's SQLite
    /// full-table rebuild, the same rollback-chain hazard.
    /// </para>
    /// </summary>
    public partial class AddScheduledTaskState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScheduledTaskNotificationLevel",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "OnlyFailures");

            migrationBuilder.CreateTable(
                name: "ScheduledTaskStates",
                columns: table => new
                {
                    TaskId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, defaultValue: "Interval"),
                    IntervalHours = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyAtMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    LastRunUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRunStatus = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    LastRunActivityId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledTaskStates", x => x.TaskId);
                });

            // Carry the old ScanFoldersOnStartup preference forward: seed the two folder-scan task
            // rows with Enabled = whatever the (now dormant) flag was. The scheduler seeds the
            // remaining catalog tasks on first run and never touches a row that already exists.
            migrationBuilder.Sql(
                @"INSERT INTO ScheduledTaskStates (TaskId, Enabled, Mode, IntervalHours, DailyAtMinutes)
                  SELECT 'library-scan',
                         COALESCE((SELECT ScanFoldersOnStartup FROM AppSettings WHERE Id = 1), 0),
                         'Interval', 6, 0
                  WHERE NOT EXISTS (SELECT 1 FROM ScheduledTaskStates WHERE TaskId = 'library-scan');");
            migrationBuilder.Sql(
                @"INSERT INTO ScheduledTaskStates (TaskId, Enabled, Mode, IntervalHours, DailyAtMinutes)
                  SELECT 'book-scan',
                         COALESCE((SELECT ScanFoldersOnStartup FROM AppSettings WHERE Id = 1), 0),
                         'Interval', 6, 0
                  WHERE NOT EXISTS (SELECT 1 FROM ScheduledTaskStates WHERE TaskId = 'book-scan');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ScheduledTaskStates");
        }
    }
}
