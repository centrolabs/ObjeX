using Microsoft.EntityFrameworkCore;

using ObjeX.Core.Models;

namespace ObjeX.Infrastructure.Data;

public class ObjeXDbContext(DbContextOptions<ObjeXDbContext> options) : DbContext(options)
{
    public DbSet<Bucket> Buckets { get; set; } = null!;
    public DbSet<BlobObject> BlobObjects { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bucket>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).IsRequired();
        });

        modelBuilder.Entity<BlobObject>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.BucketName, e.Key }).IsUnique();
            entity.Property(e => e.BucketName).IsRequired();
            entity.Property(e => e.Key).IsRequired();
            entity.Property(e => e.ETag).IsRequired();

            entity.HasOne(e => e.Bucket)
                .WithMany(b => b.Objects)
                .HasForeignKey(e => e.BucketName)
                .HasPrincipalKey(b => b.Name);
        });
    }
}