using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SemtSkoru.Infrastructure.Migrations
{
    /// <summary>
    /// Supabase's security advisor flags every public-schema table as "RLS Disabled in Public,
    /// EXTERNAL" - this app never queries Postgres through Supabase's own PostgREST/client SDK
    /// (the API only ever connects via a direct Npgsql connection string, see Program.cs), but
    /// PostgREST is still enabled by default on every Supabase project and grants the anon/
    /// authenticated roles SELECT (and, depending on default privileges, write) access to any
    /// public table with RLS off - i.e. every one of these tables was reachable directly over
    /// the internet via Supabase's REST endpoint, bypassing this app's API entirely.
    ///
    /// Enabling RLS with zero policies denies ALL access to the anon/authenticated roles by
    /// default (exactly what's wanted - PostgREST access was never an intended path) while
    /// leaving this app's own backend untouched: it connects as the table-owning role (the
    /// `postgres.<project-ref>` pooler user - see docs/deployment.md), and plain ENABLE ROW
    /// LEVEL SECURITY (not FORCE) never applies to a table's owner, only to other roles.
    /// </summary>
    public partial class EnableRowLevelSecurityOnAllTables : Migration
    {
        private static readonly string[] Tables =
        [
            "AirQualityReadings",
            "GreenSpaceReadings",
            "TrafficReadings",
            "ParkingReadings",
            "HealthAccessReadings",
            "TransitAccessReadings",
            "Neighborhoods",
            "__EFMigrationsHistory",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"""ALTER TABLE public."{table}" ENABLE ROW LEVEL SECURITY;""");
            }

            // spatial_ref_sys (PostGIS's own reference-systems table) is deliberately NOT
            // included here - live-verified (2026-09-16) that this app's connection role isn't
            // its owner ("must be owner of table spatial_ref_sys", Postgres error 42501; it's
            // owned by Supabase's own internal role). Fixing that advisory item requires a
            // Supabase-side action (their dashboard/support), not something this app's own
            // migrations can do - see docs/deployment.md.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables.Reverse())
            {
                migrationBuilder.Sql($"""ALTER TABLE public."{table}" DISABLE ROW LEVEL SECURITY;""");
            }
        }
    }
}
