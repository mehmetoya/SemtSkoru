using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence.Configurations;

public class ComparisonSummaryConfiguration : IEntityTypeConfiguration<ComparisonSummary>
{
    public void Configure(EntityTypeBuilder<ComparisonSummary> builder)
    {
        builder.ToTable("ComparisonSummaries");

        // Composite canonical-pair-plus-locale key - see ComparisonSummary.NeighborhoodIdA/B/
        // Locale's remarks. Order here (A, then B, then Locale) matches every
        // FindAsync([idLo, idHi, locale], ct) call in ComparisonSummaryOrchestrator, which is the
        // only place this key's declared order matters - FindAsync matches key VALUES positionally
        // against this exact declaration order, not by property name.
        builder.HasKey(c => new { c.NeighborhoodIdA, c.NeighborhoodIdB, c.Locale });
        builder.Property(c => c.NeighborhoodIdA).HasMaxLength(64);
        builder.Property(c => c.NeighborhoodIdB).HasMaxLength(64);
        builder.Property(c => c.Locale).HasMaxLength(5);

        builder.Property(c => c.SummaryText).HasMaxLength(500).IsRequired();
        builder.Property(c => c.GeneratedAt).IsRequired();

        // Two DistrictSummarySignature values ("|"-joined dimension values, each comfortably
        // under 200 chars - see that class's own remarks) joined by "||" - comfortably under 420
        // even with full headroom on both sides; see ComparisonSummarySignature.
        builder.Property(c => c.ScoreSignature).HasMaxLength(420).IsRequired();
    }
}
