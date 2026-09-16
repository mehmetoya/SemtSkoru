using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence.Configurations;

public class ScoreSnapshotConfiguration : IEntityTypeConfiguration<ScoreSnapshot>
{
    public void Configure(EntityTypeBuilder<ScoreSnapshot> builder)
    {
        builder.ToTable("ScoreSnapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.NeighborhoodId).HasMaxLength(64).IsRequired();

        // Null means "the district's overall score" - one of the 6 known dimension keys
        // otherwise (see ScoreSnapshot's remarks); "transitAccess" is the longest at 14 chars.
        builder.Property(s => s.Dimension).HasMaxLength(32);

        builder.Property(s => s.Score).IsRequired();
        builder.Property(s => s.RecordedAt).IsRequired();

        // Every query this table serves (ScoreSnapshotJob.GetBaselineAsync) filters by
        // NeighborhoodId + RecordedAt and orders by RecordedAt - this index covers both the
        // "latest RecordedAt for this district" lookup and the "all rows at that RecordedAt"
        // fetch without a table scan, which matters once this table has accumulated many weeks
        // of history across all 39 districts.
        builder.HasIndex(s => new { s.NeighborhoodId, s.RecordedAt });
    }
}
