using Microsoft.EntityFrameworkCore;
using SemtSkoru.Application.Trends;
using SemtSkoru.Domain;
using SemtSkoru.Infrastructure.Persistence;

namespace SemtSkoru.Infrastructure.Trends;

public sealed class DistrictTrendRepository(AppDbContext db) : IDistrictTrendRepository
{
    public Task<DistrictTrendSummary?> GetAsync(string neighborhoodId, string locale, CancellationToken ct) =>
        db.DistrictTrendSummaries.AsNoTracking()
            .FirstOrDefaultAsync(s => s.NeighborhoodId == neighborhoodId && s.Locale == locale, ct);
}
