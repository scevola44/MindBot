using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindBot.Infrastructure.State.Migrations;

/// <inheritdoc />
public partial class AddWriteJobRelativeFolder : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_WriteJobs_Filename",
            table: "WriteJobs");

        // Every write job queued before this migration was destined for the fleeting-note folder,
        // so that is the correct backfill for any job still pending across the upgrade.
        migrationBuilder.AddColumn<string>(
            name: "RelativeFolder",
            table: "WriteJobs",
            type: "TEXT",
            nullable: false,
            defaultValue: "05 - Fleeting");

        migrationBuilder.CreateIndex(
            name: "IX_WriteJobs_RelativeFolder_Filename_Status",
            table: "WriteJobs",
            columns: ["RelativeFolder", "Filename", "Status"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_WriteJobs_RelativeFolder_Filename_Status",
            table: "WriteJobs");

        migrationBuilder.DropColumn(
            name: "RelativeFolder",
            table: "WriteJobs");

        migrationBuilder.CreateIndex(
            name: "IX_WriteJobs_Filename",
            table: "WriteJobs",
            column: "Filename");
    }
}
