using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RemoteSeriesId",
                table: "Series",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemoteSourceId",
                table: "Series",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteContentStamp",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemoteIssueId",
                table: "Issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemoteSourceId",
                table: "Issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RemoteSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InstanceId = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    CertFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedPassword = table.Column<string>(type: "TEXT", nullable: true),
                    LastCatalogEtag = table.Column<string>(type: "TEXT", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsOffline = table.Column<bool>(type: "INTEGER", nullable: false),
                    HostChanged = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteSources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Series_RemoteSourceId_RemoteSeriesId",
                table: "Series",
                columns: new[] { "RemoteSourceId", "RemoteSeriesId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Issues_RemoteSourceId_RemoteIssueId",
                table: "Issues",
                columns: new[] { "RemoteSourceId", "RemoteIssueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemoteSources_InstanceId",
                table: "RemoteSources",
                column: "InstanceId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Issues_RemoteSources_RemoteSourceId",
                table: "Issues",
                column: "RemoteSourceId",
                principalTable: "RemoteSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Series_RemoteSources_RemoteSourceId",
                table: "Series",
                column: "RemoteSourceId",
                principalTable: "RemoteSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately a no-op - the RemoteSources table, the Issues/Series columns and their indexes are
            // left in place as orphans on down-migrate. DropForeignKey/DropColumn on Issues and Series make
            // EF's SQLite provider rebuild the whole table from the previous migration's model snapshot,
            // which silently drops other orphaned columns and breaks earlier Down() steps in the same
            // rollback chain (see AddSmoothScrolling's Down() for the failure this avoids). Nothing reads
            // these columns unless remote sharing is in use, and every existing row has them NULL.
        }
    }
}
