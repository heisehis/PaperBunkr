using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <summary>
    /// Adds <c>BookFormat.Fb2</c>/<c>Mobi</c> (docs/superpowers/specs/2026-09-01-books-format-
    /// ingestion-fb2-mobi-design.md). Deliberately empty: <c>Book.Format</c> is
    /// <c>HasConversion&lt;string&gt;().HasMaxLength(32)</c> with no CHECK constraint enumerating
    /// allowed values (confirmed by inspecting <c>PaperbunkrDbContext.cs</c> and by running
    /// <c>dotnet ef migrations add</c>, which produced this empty body) - the column already accepts
    /// any string up to 32 characters, so new enum members are a code-level change only. Kept as a
    /// real migration anyway for the historical/traceability record, matching every other
    /// schema-touching spec in this project.
    /// </summary>
    public partial class AddFb2MobiBookFormat : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
