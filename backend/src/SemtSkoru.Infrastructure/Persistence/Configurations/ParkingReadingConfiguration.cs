using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence.Configurations;

public class ParkingReadingConfiguration : IEntityTypeConfiguration<ParkingReading>
{
    public void Configure(EntityTypeBuilder<ParkingReading> builder)
    {
        builder.ToTable("ParkingReadings");

        builder.HasKey(r => r.NeighborhoodId);
        builder.Property(r => r.NeighborhoodId).HasMaxLength(64);

        builder.OwnsOne(r => r.Source, source => source.ConfigureDataSourceMetadata());
    }
}
