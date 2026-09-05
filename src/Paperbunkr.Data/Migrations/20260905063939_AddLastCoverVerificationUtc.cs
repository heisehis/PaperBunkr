using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLastCoverVerificationUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastCoverVerificationUtc",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastCoverVerificationUtc",
                table: "AppSettings");
        }
    }
}
