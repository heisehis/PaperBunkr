using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Aliases",
                table: "Locations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomImagePath",
                table: "Locations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Locations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImageCheckedAt",
                table: "Locations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImageSource",
                table: "Locations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Locations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Aliases",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "CustomImagePath",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "ImageCheckedAt",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "ImageSource",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Locations");
        }
    }
}
