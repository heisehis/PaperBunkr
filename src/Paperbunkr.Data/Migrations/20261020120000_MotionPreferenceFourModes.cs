using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Paperbunkr.Data.Migrations
{
    /// <summary>
    /// Motion v2 (docs/superpowers/specs/2026-10-20-motion-system-v2-foundation-overlays-design.md §4.1): the Reduced Motion
    /// System/On/Off choice becomes Motion System/Full/Reduced/Off. The old "Off" meant "reduced motion off" - full motion - so it
    /// maps to Full, not to the new Off. One CASE so no value is rewritten twice.
    /// </summary>
    public partial class MotionPreferenceFourModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE AppSettings SET MotionPreference = CASE MotionPreference WHEN 'On' THEN 'Reduced' WHEN 'Off' THEN 'Full' ELSE MotionPreference END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE AppSettings SET MotionPreference = CASE MotionPreference WHEN 'Reduced' THEN 'On' WHEN 'Off' THEN 'On' WHEN 'Full' THEN 'Off' ELSE MotionPreference END");
        }
    }
}
