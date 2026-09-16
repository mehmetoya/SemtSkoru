using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemtSkoru.Infrastructure.Migrations
{
    /// <summary>
    /// DistrictSummaries was added (AddDistrictSummaries) after
    /// EnableRowLevelSecurityOnAllTables, so it missed that sweep - see that migration's remarks
    /// for why every public table needs this (Supabase's PostgREST is on by default regardless
    /// of whether this app uses it).
    /// </summary>
    public partial class EnableRowLevelSecurityOnDistrictSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE public."DistrictSummaries" ENABLE ROW LEVEL SECURITY;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE public."DistrictSummaries" DISABLE ROW LEVEL SECURITY;""");
        }
    }
}
