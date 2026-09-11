using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence.Configurations;

public class NeighborhoodConfiguration : IEntityTypeConfiguration<Neighborhood>
{
    public void Configure(EntityTypeBuilder<Neighborhood> builder)
    {
        builder.ToTable("Neighborhoods");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id).HasMaxLength(64);
        builder.Property(n => n.Name).IsRequired().HasMaxLength(200);

        builder.Property(n => n.Boundary)
            .HasColumnType("geometry(Polygon, 4326)")
            .IsRequired();
    }
}
