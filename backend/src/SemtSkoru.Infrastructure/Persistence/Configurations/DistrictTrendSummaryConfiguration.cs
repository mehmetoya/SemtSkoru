using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence.Configurations;

public class DistrictTrendSummaryConfiguration : IEntityTypeConfiguration<DistrictTrendSummary>
{
    public void Configure(EntityTypeBuilder<DistrictTrendSummary> builder)
    {
        builder.ToTable("DistrictTrendSummaries");

        // Composite key: one row per (district, locale) - see DistrictTrendSummary.Locale's remarks.
        builder.HasKey(s => new { s.NeighborhoodId, s.Locale });
        builder.Property(s => s.NeighborhoodId).HasMaxLength(64);
        builder.Property(s => s.Locale).HasMaxLength(5);

        builder.Property(s => s.SummaryText).HasMaxLength(500).IsRequired();
        builder.Property(s => s.GeneratedAt).IsRequired();

        // "<dimension>:<prev>:<cur>" per meaningfully-changed dimension, comma-joined, up to 6
        // dimensions - comfortably under 300 chars even at 3-digit scores; see
        // DistrictTrendSignature.
        builder.Property(s => s.ComparisonSignature).HasMaxLength(300).IsRequired();
    }
}
