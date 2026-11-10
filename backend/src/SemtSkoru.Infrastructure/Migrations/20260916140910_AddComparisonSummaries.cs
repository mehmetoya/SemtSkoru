using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemtSkoru.Infrastructure.Migrations
{
    /// <summary>
    /// Unlike DistrictSummaries (whose RLS enablement shipped as a separate later sweep -
    /// EnableRowLevelSecurityOnDistrictSummaries - because it was added between the original
    /// EnableRowLevelSecurityOnAllTables migration and that sweep), this new table enables RLS in
    /// the SAME migration that creates it, closing that gap from the start rather than leaving it
    /// briefly reachable over Supabase's default-on PostgREST endpoint - see
    /// EnableRowLevelSecurityOnAllTables's remarks for the full "why every public table needs
    /// this" rationale (this app never uses PostgREST itself; RLS with zero policies just denies
    /// the anon/authenticated roles PostgREST grants access to by default, while this app's own
    /// backend - which connects as the table-owning role - is unaffected).
    /// </summary>
    public partial class AddComparisonSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComparisonSummaries",
                columns: table => new
                {
                    NeighborhoodIdA = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NeighborhoodIdB = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SummaryText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ScoreSignature = table.Column<string>(type: "character varying(420)", maxLength: 420, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonSummaries", x => new { x.NeighborhoodIdA, x.NeighborhoodIdB });
                });

            migrationBuilder.Sql("""ALTER TABLE public."ComparisonSummaries" ENABLE ROW LEVEL SECURITY;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComparisonSummaries");
        }
    }
}
