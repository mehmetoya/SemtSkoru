using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemtSkoru.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAirQualityReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AirQualityReadings",
                columns: table => new
                {
                    NeighborhoodId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AqiIndex = table.Column<double>(type: "double precision", nullable: false),
                    ReadingTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceLicense = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSuccessfulSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AirQualityReadings", x => x.NeighborhoodId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AirQualityReadings");
        }
    }
}
