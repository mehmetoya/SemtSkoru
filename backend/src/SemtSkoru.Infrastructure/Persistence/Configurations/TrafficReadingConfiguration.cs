using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence.Configurations;

public class TrafficReadingConfiguration : IEntityTypeConfiguration<TrafficReading>
{
    public void Configure(EntityTypeBuilder<TrafficReading> builder)
    {
        builder.ToTable("TrafficReadings");

        builder.HasKey(r => r.NeighborhoodId);
        builder.Property(r => r.NeighborhoodId).HasMaxLength(64);

        builder.OwnsOne(r => r.Source, source => source.ConfigureDataSourceMetadata());
    }
}
