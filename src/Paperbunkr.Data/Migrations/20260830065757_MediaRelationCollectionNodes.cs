using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class MediaRelationCollectionNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TargetSeriesId",
                table: "MediaRelations",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "SourceSeriesId",
                table: "MediaRelations",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "SourceCollectionId",
                table: "MediaRelations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetCollectionId",
                table: "MediaRelations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaRelations_SourceCollectionId",
                table: "MediaRelations",
                column: "SourceCollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaRelations_TargetCollectionId",
                table: "MediaRelations",
                column: "TargetCollectionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MediaRelation_OneSourceTarget",
                table: "MediaRelations",
                sql: "((\"SourceSeriesId\" IS NOT NULL) + (\"SourceCollectionId\" IS NOT NULL)) = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MediaRelation_OneTargetTarget",
                table: "MediaRelations",
                sql: "((\"TargetSeriesId\" IS NOT NULL) + (\"TargetCollectionId\" IS NOT NULL)) = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaRelations_Collections_SourceCollectionId",
                table: "MediaRelations",
                column: "SourceCollectionId",
                principalTable: "Collections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaRelations_Collections_TargetCollectionId",
                table: "MediaRelations",
                column: "TargetCollectionId",
                principalTable: "Collections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaRelations_Collections_SourceCollectionId",
                table: "MediaRelations");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaRelations_Collections_TargetCollectionId",
                table: "MediaRelations");

            migrationBuilder.DropIndex(
                name: "IX_MediaRelations_SourceCollectionId",
                table: "MediaRelations");

            migrationBuilder.DropIndex(
                name: "IX_MediaRelations_TargetCollectionId",
                table: "MediaRelations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MediaRelation_OneSourceTarget",
                table: "MediaRelations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MediaRelation_OneTargetTarget",
                table: "MediaRelations");

            migrationBuilder.DropColumn(
                name: "SourceCollectionId",
                table: "MediaRelations");

            migrationBuilder.DropColumn(
                name: "TargetCollectionId",
                table: "MediaRelations");

            migrationBuilder.AlterColumn<int>(
                name: "TargetSeriesId",
                table: "MediaRelations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SourceSeriesId",
                table: "MediaRelations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
