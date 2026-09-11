using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence.Configurations;

public class GreenSpaceReadingConfiguration : IEntityTypeConfiguration<GreenSpaceReading>
{
    public void Configure(EntityTypeBuilder<GreenSpaceReading> builder)
    {
        builder.ToTable("GreenSpaceReadings");

        builder.HasKey(r => r.NeighborhoodId);
        builder.Property(r => r.NeighborhoodId).HasMaxLength(64);
        builder.Property(r => r.NearestParkName).HasMaxLength(300);

        builder.OwnsOne(r => r.Source, source => source.ConfigureDataSourceMetadata());
    }
}
