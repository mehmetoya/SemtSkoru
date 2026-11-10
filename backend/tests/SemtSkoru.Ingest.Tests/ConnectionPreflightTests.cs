using Npgsql;

namespace SemtSkoru.Ingest.Tests;

public class ConnectionPreflightTests
{
    [Fact]
    public void Key_value_form_is_accepted_unchanged_in_meaning()
    {
        var (connectionString, error) = ConnectionPreflight.Normalize(
            "Host=aws-1-eu-west-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.abcd;Password=s3cret;SSL Mode=Require");

        Assert.Null(error);
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("aws-1-eu-west-1.pooler.supabase.com", builder.Host);
        Assert.Equal("postgres.abcd", builder.Username);
        Assert.Equal("s3cret", builder.Password);
        Assert.Equal(SslMode.Require, builder.SslMode);
    }

    [Theory]
    [InlineData("postgresql://postgres.abcd:p%40ss%3Bword@aws-1-eu-west-1.pooler.supabase.com:6543/postgres")]
    [InlineData("postgres://postgres.abcd:p%40ss%3Bword@aws-1-eu-west-1.pooler.supabase.com:6543/postgres")]
    public void Supabase_uri_form_is_converted_to_key_value_with_tls(string uri)
    {
        var (connectionString, error) = ConnectionPreflight.Normalize("  " + uri + "\n");

        Assert.Null(error);
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("aws-1-eu-west-1.pooler.supabase.com", builder.Host);
        Assert.Equal(6543, builder.Port);
        Assert.Equal("postgres", builder.Database);
        Assert.Equal("postgres.abcd", builder.Username);
        Assert.Equal("p@ss;word", builder.Password); // percent-escapes decoded, ';' survives key=value quoting
        Assert.Equal(SslMode.Require, builder.SslMode);
    }

    [Fact]
    public void Uri_without_port_or_database_gets_postgres_defaults()
    {
        var (connectionString, error) = ConnectionPreflight.Normalize("postgresql://postgres.abcd:pw@db.example.com");

        Assert.Null(error);
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("postgres", builder.Database);
    }

    [Theory]
    [InlineData("abc1234")] // shaped like what was in the Keychain on 2026-11-10: 7 characters, no '='
    [InlineData("not a connection string")]
    public void Garbage_gets_a_readable_error_instead_of_an_exception(string raw)
    {
        var (connectionString, error) = ConnectionPreflight.Normalize(raw);

        Assert.Null(connectionString);
        Assert.Contains("key=value", error);
        Assert.DoesNotContain(raw, error); // never echo the (possibly secret) input back
    }

    [Fact]
    public void Wrong_password_is_described_without_retry_advice_loops()
    {
        var ex = new PostgresException("password authentication failed for user \"postgres\"", "FATAL", "FATAL", PostgresErrorCodes.InvalidPassword);

        var reason = ConnectionPreflight.Describe(ex);

        Assert.Contains("wrong password", reason);
        Assert.Contains("Do not retry repeatedly", reason);
    }

    [Fact]
    public void Circuit_breaker_is_described_as_a_wait()
    {
        var ex = new PostgresException(
            "(ECIRCUITBREAKER) too many authentication failures, new connections are temporarily blocked", "FATAL", "FATAL", "XX000");

        var reason = ConnectionPreflight.Describe(ex);

        Assert.Contains("ECIRCUITBREAKER", reason);
        Assert.Contains("Wait", reason);
    }

    [Fact]
    public async Task Unreachable_server_fails_the_preflight_with_one_line()
    {
        // Port 1 on localhost: nothing listens there, so the connect fails fast without any network.
        var reason = await ConnectionPreflight.CheckAsync(
            "Host=127.0.0.1;Port=1;Database=postgres;Username=u;Password=p;Timeout=3", CancellationToken.None);

        Assert.NotNull(reason);
        Assert.StartsWith("Database connection failed", reason);
        Assert.DoesNotContain("\n", reason);
    }
}
