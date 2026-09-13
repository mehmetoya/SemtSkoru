using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence.Configurations;

public class TransitAccessReadingConfiguration : IEntityTypeConfiguration<TransitAccessReading>
{
    public void Configure(EntityTypeBuilder<TransitAccessReading> builder)
    {
        builder.ToTable("TransitAccessReadings");

        builder.HasKey(r => r.NeighborhoodId);
        builder.Property(r => r.NeighborhoodId).HasMaxLength(64);

        builder.OwnsOne(r => r.Source, source => source.ConfigureDataSourceMetadata());
    }
}
