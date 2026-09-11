using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence.Configurations;

public class AirQualityReadingConfiguration : IEntityTypeConfiguration<AirQualityReading>
{
    public void Configure(EntityTypeBuilder<AirQualityReading> builder)
    {
        builder.ToTable("AirQualityReadings");

        builder.HasKey(r => r.NeighborhoodId);
        builder.Property(r => r.NeighborhoodId).HasMaxLength(64);

        builder.OwnsOne(r => r.Source, source => source.ConfigureDataSourceMetadata());
    }
}
