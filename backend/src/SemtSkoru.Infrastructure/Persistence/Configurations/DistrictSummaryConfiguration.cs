using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence.Configurations;

public class DistrictSummaryConfiguration : IEntityTypeConfiguration<DistrictSummary>
{
    public void Configure(EntityTypeBuilder<DistrictSummary> builder)
    {
        builder.ToTable("DistrictSummaries");

        // Composite key: one row per (district, locale) - see DistrictSummary.Locale's remarks.
        builder.HasKey(s => new { s.NeighborhoodId, s.Locale });
        builder.Property(s => s.NeighborhoodId).HasMaxLength(64);
        builder.Property(s => s.Locale).HasMaxLength(5);

        builder.Property(s => s.SummaryText).HasMaxLength(500).IsRequired();
        builder.Property(s => s.GeneratedAt).IsRequired();

        // Six "|"-joined dimension values (each an int or the literal "null") - comfortably
        // under 200 chars even at 3 digits each; see DistrictSummarySignature.
        builder.Property(s => s.ScoreSignature).HasMaxLength(200).IsRequired();
    }
}
