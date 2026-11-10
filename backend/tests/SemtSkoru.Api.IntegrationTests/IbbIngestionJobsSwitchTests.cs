using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SemtSkoru.Infrastructure.Ingestion;
using Testcontainers.PostgreSql;

namespace SemtSkoru.Api.IntegrationTests;

// Ingestion:IbbJobsEnabled (Program.cs): production turns the İBB jobs off on Render because
// İBB only accepts Turkish IPs. Hangfire persists recurring jobs in the database, so turning the
// switch off must REMOVE registrations an earlier deploy made, not merely skip adding them.
public class IbbIngestionJobsSwitchTests : IAsyncLifetime
{
    private static readonly string[] GeminiJobIds = ["district-summary-generation", "score-snapshot"];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory(string? ibbJobsEnabled) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
            if (ibbJobsEnabled is not null)
            {
                builder.UseSetting("Ingestion:IbbJobsEnabled", ibbJobsEnabled);
            }
        });

    // Program.cs registers recurring jobs as a side effect of building the host, which
    // accessing Services triggers.
    private static HashSet<string> RecurringJobIds(WebApplicationFactory<Program> factory)
    {
        using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
        return connection.GetRecurringJobs().Select(j => j.Id).ToHashSet();
    }

    [Fact]
    public async Task By_default_every_Ibb_job_in_the_catalog_is_registered_alongside_the_Gemini_jobs()
    {
        await using var factory = CreateFactory(ibbJobsEnabled: null);

        var ids = RecurringJobIds(factory);

        // Also pins the catalog's RecurringJobIds to the literal IDs Program.cs registers - if
        // they drifted apart, disabling the switch would silently fail to remove a job.
        Assert.All(IbbIngestionJobCatalog.All, job => Assert.Contains(job.RecurringJobId, ids));
        Assert.All(GeminiJobIds, id => Assert.Contains(id, ids));
        Assert.Equal(IbbIngestionJobCatalog.All.Count + GeminiJobIds.Length, ids.Count);
    }

    [Fact]
    public async Task Disabling_removes_previously_registered_Ibb_jobs_but_keeps_the_Gemini_jobs()
    {
        // A previous deploy (switch on) left the İBB jobs persisted in Hangfire's tables...
        await using (var previousDeploy = CreateFactory(ibbJobsEnabled: "true"))
        {
            Assert.Contains("parking-ingestion", RecurringJobIds(previousDeploy));
        }

        // ...and the next deploy, with the switch off, must clean them up.
        await using var factory = CreateFactory(ibbJobsEnabled: "false");

        var ids = RecurringJobIds(factory);

        Assert.All(IbbIngestionJobCatalog.All, job => Assert.DoesNotContain(job.RecurringJobId, ids));
        Assert.Equal(GeminiJobIds.ToHashSet(), ids);
    }
}
