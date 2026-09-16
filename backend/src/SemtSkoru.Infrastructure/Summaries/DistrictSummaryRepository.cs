using Microsoft.EntityFrameworkCore;
using SemtSkoru.Application.Summaries;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Summaries;

public sealed class DistrictSummaryRepository(AppDbContext db) : IDistrictSummaryRepository
{
    public Task<DistrictSummary?> GetAsync(string neighborhoodId, CancellationToken ct) =>
        db.DistrictSummaries.AsNoTracking().FirstOrDefaultAsync(s => s.NeighborhoodId == neighborhoodId, ct);
}
