using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemtSkoru.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGreenSpaceReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GreenSpaceReadings",
                columns: table => new
                {
                    NeighborhoodId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NearestParkName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    NearestParkDistanceMeters = table.Column<double>(type: "double precision", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceLicense = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSuccessfulSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GreenSpaceReadings", x => x.NeighborhoodId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GreenSpaceReadings");
        }
    }
}
