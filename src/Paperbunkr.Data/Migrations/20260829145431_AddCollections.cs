using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCollections : Migration
    {
        // Hand-edited from the scaffolded migration (docs/superpowers/specs/2026-08-27-collections-
        // design.md). The scaffolder dropped Categories/CategorySeries outright with no data copy;
        // this version creates the new tables first, copies the old rows across (preserving
        // Category.Id -> Collection.Id so copied membership stays valid), and only then drops the
        // old tables. In practice nothing ever created a Category row, but the migration is written
        // to survive a non-empty table.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    AccentColor = table.Column<string>(type: "TEXT", nullable: true),
                    CoverImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    IsAutoCover = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CollectionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CollectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: true),
                    BookId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionItems", x => x.Id);
                    table.CheckConstraint("CK_CollectionItem_OneTarget", "((\"SeriesId\" IS NOT NULL) + (\"IssueId\" IS NOT NULL) + (\"BookId\" IS NOT NULL)) = 1");
                    table.ForeignKey(
                        name: "FK_CollectionItems_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionItems_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionItems_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionItems_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Copy old Category rows across, preserving Id so the membership copy below stays valid.
            migrationBuilder.Sql(
                "INSERT INTO \"Collections\" (\"Id\", \"Name\", \"SortOrder\", \"IsAutoCover\") " +
                "SELECT \"Id\", \"Name\", \"SortOrder\", 1 FROM \"Categories\";");

            // Copy old Category<->Series memberships. SortOrder starts at 0 for every copied row
            // (the old skip-nav join had no ordering); a real order is assigned once the user
            // reorders the collection.
            migrationBuilder.Sql(
                "INSERT INTO \"CollectionItems\" (\"CollectionId\", \"SeriesId\", \"SortOrder\") " +
                "SELECT \"CategoriesId\", \"SeriesId\", 0 FROM \"CategorySeries\";");

            migrationBuilder.DropTable(name: "CategorySeries");
            migrationBuilder.DropTable(name: "Categories");

            migrationBuilder.RenameColumn(
                name: "LibraryActiveCategoryId",
                table: "AppSettings",
                newName: "LibraryActiveCollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_BookId",
                table: "CollectionItems",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_CollectionId_BookId",
                table: "CollectionItems",
                columns: new[] { "CollectionId", "BookId" },
                unique: true,
                filter: "\"BookId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_CollectionId_IssueId",
                table: "CollectionItems",
                columns: new[] { "CollectionId", "IssueId" },
                unique: true,
                filter: "\"IssueId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_CollectionId_SeriesId",
                table: "CollectionItems",
                columns: new[] { "CollectionId", "SeriesId" },
                unique: true,
                filter: "\"SeriesId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_IssueId",
                table: "CollectionItems",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItems_SeriesId",
                table: "CollectionItems",
                column: "SeriesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort inverse: only Series membership can be restored (Categories only ever
            // grouped series). Issue/Book membership and every Collection appearance field
            // (Description/AccentColor/Cover) are dropped - the old schema had nowhere to hold them.

            migrationBuilder.RenameColumn(
                name: "LibraryActiveCollectionId",
                table: "AppSettings",
                newName: "LibraryActiveCategoryId");

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategorySeries",
                columns: table => new
                {
                    CategoriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategorySeries", x => new { x.CategoriesId, x.SeriesId });
                    table.ForeignKey(
                        name: "FK_CategorySeries_Categories_CategoriesId",
                        column: x => x.CategoriesId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CategorySeries_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                "INSERT INTO \"Categories\" (\"Id\", \"Name\", \"SortOrder\") " +
                "SELECT \"Id\", \"Name\", \"SortOrder\" FROM \"Collections\";");

            migrationBuilder.Sql(
                "INSERT INTO \"CategorySeries\" (\"CategoriesId\", \"SeriesId\") " +
                "SELECT DISTINCT \"CollectionId\", \"SeriesId\" FROM \"CollectionItems\" WHERE \"SeriesId\" IS NOT NULL;");

            migrationBuilder.DropTable(name: "CollectionItems");
            migrationBuilder.DropTable(name: "Collections");

            migrationBuilder.CreateIndex(
                name: "IX_CategorySeries_SeriesId",
                table: "CategorySeries",
                column: "SeriesId");
        }
    }
}
