using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemtSkoru.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceCadence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cadence",
                table: "GreenSpaceReadings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Periodic");

            migrationBuilder.AddColumn<string>(
                name: "Cadence",
                table: "AirQualityReadings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Live");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Cadence",
                table: "GreenSpaceReadings");

            migrationBuilder.DropColumn(
                name: "Cadence",
                table: "AirQualityReadings");
        }
    }
}
