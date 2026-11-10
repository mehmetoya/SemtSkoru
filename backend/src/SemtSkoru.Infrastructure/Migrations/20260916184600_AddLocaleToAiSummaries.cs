using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemtSkoru.Infrastructure.Migrations
{
    /// <summary>
    /// Adds locale awareness to all 3 cached-AI-text tables (see DistrictSummary/
    /// DistrictTrendSummary/ComparisonSummary's own updated remarks, and
    /// SemtSkoru.Application.Localization.AiLocale) - every AI feature generates its prose in the
    /// visitor's actual locale now instead of Turkish-only.
    ///
    /// Every one of these 3 tables may already hold real, already-paid-for Turkish generations
    /// from before this migration - this must never lose them. The new "Locale" column is added
    /// with `DEFAULT 'tr'`, which Postgres applies to every EXISTING row for free as part of
    /// ADD COLUMN (no separate UPDATE statement needed, and - since Postgres 11 - no full table
    /// rewrite either, for a constant default like this one): every pre-migration row ends up
    /// exactly as if it had always been explicitly generated for "tr", which is the truth. Each
    /// table's primary key is then widened to include Locale, matching
    /// DistrictSummaryConfiguration/DistrictTrendSummaryConfiguration/
    /// ComparisonSummaryConfiguration's new composite HasKey declarations - ComparisonSummaries'
    /// widens from (NeighborhoodIdA, NeighborhoodIdB) to (NeighborhoodIdA, NeighborhoodIdB,
    /// Locale), in that exact order, since ComparisonSummaryOrchestrator.GetOrGenerateAsync's
    /// FindAsync([idLo, idHi, locale], ct) call matches key values positionally against this
    /// declaration order.
    ///
    /// All 3 tables already had RLS enabled from their own creation migrations (see
    /// AddDistrictSummaries/AddScoreSnapshotsAndDistrictTrendSummaries/AddComparisonSummaries) -
    /// altering an existing table's column/PK needs no new RLS statement here; only a genuinely
    /// NEW table would.
    /// </summary>
    public partial class AddLocaleToAiSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_DistrictTrendSummaries",
                table: "DistrictTrendSummaries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DistrictSummaries",
                table: "DistrictSummaries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ComparisonSummaries",
                table: "ComparisonSummaries");

            // defaultValue: "tr" (not ""): every row that already exists in these tables today is
            // a real, already-generated Turkish summary - this backfills it as such for free,
            // rather than leaving it as an empty/unrecognized locale AiLocale.NormalizeOrDefault
            // would otherwise have to paper over at read time.
            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "DistrictTrendSummaries",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "tr");

            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "DistrictSummaries",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "tr");

            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "ComparisonSummaries",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "tr");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DistrictTrendSummaries",
                table: "DistrictTrendSummaries",
                columns: new[] { "NeighborhoodId", "Locale" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_DistrictSummaries",
                table: "DistrictSummaries",
                columns: new[] { "NeighborhoodId", "Locale" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ComparisonSummaries",
                table: "ComparisonSummaries",
                columns: new[] { "NeighborhoodIdA", "NeighborhoodIdB", "Locale" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_DistrictTrendSummaries",
                table: "DistrictTrendSummaries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DistrictSummaries",
                table: "DistrictSummaries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ComparisonSummaries",
                table: "ComparisonSummaries");

            migrationBuilder.DropColumn(
                name: "Locale",
                table: "DistrictTrendSummaries");

            migrationBuilder.DropColumn(
                name: "Locale",
                table: "DistrictSummaries");

            migrationBuilder.DropColumn(
                name: "Locale",
                table: "ComparisonSummaries");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DistrictTrendSummaries",
                table: "DistrictTrendSummaries",
                column: "NeighborhoodId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DistrictSummaries",
                table: "DistrictSummaries",
                column: "NeighborhoodId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ComparisonSummaries",
                table: "ComparisonSummaries",
                columns: new[] { "NeighborhoodIdA", "NeighborhoodIdB" });
        }
    }
}
