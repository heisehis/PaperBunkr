using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComicMetadataEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PublisherEntityId",
                table: "Series",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublisherEntityId",
                table: "Issues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Upc",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ComicMetadataExternalIds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntityKind = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComicMetadataExternalIds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Creators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Creators", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Publishers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Publishers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreatorCredits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatorId = table.Column<int>(type: "INTEGER", nullable: false),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatorCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreatorCredits_Creators_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "Creators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CreatorCredits_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocationAppearances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationAppearances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocationAppearances_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationAppearances_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamAppearances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamAppearances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamAppearances_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamAppearances_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Series_PublisherEntityId",
                table: "Series",
                column: "PublisherEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_PublisherEntityId",
                table: "Issues",
                column: "PublisherEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_ComicMetadataExternalIds_EntityKind_EntityId_Provider",
                table: "ComicMetadataExternalIds",
                columns: new[] { "EntityKind", "EntityId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComicMetadataExternalIds_EntityKind_Provider_ExternalId",
                table: "ComicMetadataExternalIds",
                columns: new[] { "EntityKind", "Provider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreatorCredits_CreatorId_IssueId_Role",
                table: "CreatorCredits",
                columns: new[] { "CreatorId", "IssueId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreatorCredits_IssueId",
                table: "CreatorCredits",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_Creators_Name",
                table: "Creators",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_LocationAppearances_IssueId",
                table: "LocationAppearances",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationAppearances_LocationId_IssueId",
                table: "LocationAppearances",
                columns: new[] { "LocationId", "IssueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Name",
                table: "Locations",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Publishers_Name",
                table: "Publishers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TeamAppearances_IssueId",
                table: "TeamAppearances",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamAppearances_TeamId_IssueId",
                table: "TeamAppearances",
                columns: new[] { "TeamId", "IssueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Name",
                table: "Teams",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_Issues_Publishers_PublisherEntityId",
                table: "Issues",
                column: "PublisherEntityId",
                principalTable: "Publishers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Series_Publishers_PublisherEntityId",
                table: "Series",
                column: "PublisherEntityId",
                principalTable: "Publishers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Issues.PublisherEntityId (with its index and foreign key) and Issues.Upc are left in place as an orphan on down-migrate, not dropped: a change to Issues
            // triggers SQLite's full-table rebuild from the previous model snapshot, which still
            // expects the unmapped LibraryGroupField/LibrarySortField columns and fails with
            // "no such column: LibraryGroupField" (same rule and reason as
            // 20260905064447_AddIssueDuplicateAcknowledged).

            migrationBuilder.DropForeignKey(
                name: "FK_Series_Publishers_PublisherEntityId",
                table: "Series");

            migrationBuilder.DropTable(
                name: "ComicMetadataExternalIds");

            migrationBuilder.DropTable(
                name: "CreatorCredits");

            migrationBuilder.DropTable(
                name: "LocationAppearances");

            migrationBuilder.DropTable(
                name: "Publishers");

            migrationBuilder.DropTable(
                name: "TeamAppearances");

            migrationBuilder.DropTable(
                name: "Creators");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Series_PublisherEntityId",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "PublisherEntityId",
                table: "Series");

        }
    }
}
