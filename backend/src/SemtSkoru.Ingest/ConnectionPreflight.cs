using Npgsql;

namespace SemtSkoru.Ingest;

/// <summary>
/// Turns whatever the owner pasted into the Keychain into a usable Npgsql connection string, and
/// proves it can log in ONCE before any job runs.
///
/// Why a preflight at all: live-verified (2026-11-10) that a wrong password made each of the six
/// jobs open its own connection and fail authentication separately. Six failed logins within
/// seconds tripped Supabase's pooler circuit breaker ("ECIRCUITBREAKER too many authentication
/// failures, new connections are temporarily blocked"), which then also rejected the corrected
/// password. One login attempt per run keeps a bad credential from locking the project out.
/// </summary>
public static class ConnectionPreflight
{
    /// <summary>
    /// Supabase's dashboard shows the pooler connection string as a URI
    /// (<c>postgresql://user:pass@host:5432/postgres</c>), but Npgsql only accepts key=value form
    /// and rejects a URI with an opaque "Format of the initialization string" error. Accept both.
    /// Returns null with an error message for input that is neither.
    /// </summary>
    public static (string? ConnectionString, string? Error) Normalize(string raw)
    {
        var value = raw.Trim();

        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            {
                return (null, "The connection string looks like a postgresql:// URI but could not be parsed.");
            }

            var userInfo = uri.UserInfo.Split(':', 2);
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
                Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')) is { Length: > 0 } db ? db : "postgres",
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
                // Supabase requires TLS; the URI form from its dashboard doesn't spell that out.
                SslMode = SslMode.Require,
            };
            return (builder.ConnectionString, null);
        }

        try
        {
            return (new NpgsqlConnectionStringBuilder(value).ConnectionString, null);
        }
        catch (ArgumentException)
        {
            return (null,
                "The connection string is not valid. Expected key=value form " +
                "(Host=...;Port=5432;Database=postgres;Username=postgres.<ref>;Password=...;SSL Mode=Require) " +
                "or a postgresql:// URI.");
        }
    }

    /// <summary>
    /// Opens and closes one connection. Returns null on success, or a one-line, secret-free reason.
    /// </summary>
    public static async Task<string?> CheckAsync(string connectionString, CancellationToken ct)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Describe(ex);
        }
    }

    public static string Describe(Exception ex) => ex switch
    {
        PostgresException { SqlState: PostgresErrorCodes.InvalidPassword } =>
            "Database login failed: wrong password (28P01). Update the Keychain item with the exact " +
            "ConnectionStrings__Default value from Render. Do not retry repeatedly: Supabase blocks " +
            "new connections for a while after several failed logins.",
        PostgresException pg when pg.MessageText.Contains("ECIRCUITBREAKER", StringComparison.Ordinal) =>
            "Supabase is temporarily blocking new connections after too many failed logins " +
            "(ECIRCUITBREAKER). Wait about 10-15 minutes before trying again.",
        PostgresException pg => $"Database connection failed: {pg.SqlState}: {pg.MessageText}",
        NpgsqlException { InnerException: { } inner } => $"Database connection failed: {inner.GetType().Name}: {inner.Message}",
        _ => $"Database connection failed: {ex.GetType().Name}: {ex.Message}",
    };
}
