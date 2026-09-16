using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SemtSkoru.Infrastructure.Migrations
{
    /// <summary>
    /// Adds the score-trend feature's two new tables (see ScoreSnapshot/DistrictTrendSummary's
    /// own remarks for what each is for). Both get RLS enabled with zero policies in the SAME
    /// migration that creates them, unlike DistrictSummaries (which needed a separate follow-up
    /// migration because it shipped before EnableRowLevelSecurityOnAllTables existed) - see that
    /// migration's remarks for why every public table needs this regardless of whether this app
    /// uses Supabase's PostgREST/client SDK: it's on by default on every Supabase project and
    /// grants anon/authenticated roles access to any public table with RLS off.
    /// </summary>
    public partial class AddScoreSnapshotsAndDistrictTrendSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DistrictTrendSummaries",
                columns: table => new
                {
                    NeighborhoodId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SummaryText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ComparisonSignature = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DistrictTrendSummaries", x => x.NeighborhoodId);
                });

            migrationBuilder.CreateTable(
                name: "ScoreSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NeighborhoodId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Dimension = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScoreSnapshots_NeighborhoodId_RecordedAt",
                table: "ScoreSnapshots",
                columns: new[] { "NeighborhoodId", "RecordedAt" });

            migrationBuilder.Sql("""ALTER TABLE public."DistrictTrendSummaries" ENABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""ALTER TABLE public."ScoreSnapshots" ENABLE ROW LEVEL SECURITY;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DistrictTrendSummaries");

            migrationBuilder.DropTable(
                name: "ScoreSnapshots");
        }
    }
}
