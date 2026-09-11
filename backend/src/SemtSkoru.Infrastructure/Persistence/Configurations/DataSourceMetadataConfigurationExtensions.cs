using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SemtSkoru.Domain;

namespace SemtSkoru.Infrastructure.Persistence.Configurations;

internal static class DataSourceMetadataConfigurationExtensions
{
    public static void ConfigureDataSourceMetadata<TOwner>(this OwnedNavigationBuilder<TOwner, DataSourceMetadata> source)
        where TOwner : class
    {
        source.Property(s => s.SourceName).HasColumnName("SourceName").HasMaxLength(300).IsRequired();
        source.Property(s => s.SourceUrl).HasColumnName("SourceUrl").HasMaxLength(500).IsRequired();
        source.Property(s => s.SourceLicense).HasColumnName("SourceLicense").HasMaxLength(300).IsRequired();
        source.Property(s => s.FetchedAt).HasColumnName("FetchedAt").IsRequired();
        source.Property(s => s.PublishedAt).HasColumnName("PublishedAt").IsRequired();
        source.Property(s => s.LastSuccessfulSyncAt).HasColumnName("LastSuccessfulSyncAt").IsRequired();
        source.Property(s => s.Cadence).HasColumnName("Cadence").HasConversion<string>().HasMaxLength(20).IsRequired();
    }
}
