using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence.Configurations;

public class HealthAccessReadingConfiguration : IEntityTypeConfiguration<HealthAccessReading>
{
    public void Configure(EntityTypeBuilder<HealthAccessReading> builder)
    {
        builder.ToTable("HealthAccessReadings");

        builder.HasKey(r => r.NeighborhoodId);
        builder.Property(r => r.NeighborhoodId).HasMaxLength(64);

        builder.OwnsOne(r => r.Source, source => source.ConfigureDataSourceMetadata());
    }
}
