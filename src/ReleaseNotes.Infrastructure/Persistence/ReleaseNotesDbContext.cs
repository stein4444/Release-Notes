using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Infrastructure.Persistence.Entities;

namespace ReleaseNotes.Infrastructure.Persistence;

public sealed class ReleaseNotesDbContext(DbContextOptions<ReleaseNotesDbContext> options) : DbContext(options)
{
    public DbSet<ReleaseNoteJobEntity> Jobs => Set<ReleaseNoteJobEntity>();
    public DbSet<ReleaseNoteDocumentEntity> Documents => Set<ReleaseNoteDocumentEntity>();
    public DbSet<RepositoryConnectionEntity> RepositoryConnections => Set<RepositoryConnectionEntity>();
    public DbSet<ServiceIntegrationEntity> ServiceIntegrations => Set<ServiceIntegrationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReleaseNoteJobEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ReleaseNoteDocumentEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<RepositoryConnectionEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ServiceIntegrationEntity>().HasKey(x => x.Id);

        modelBuilder.Entity<RepositoryConnectionEntity>()
            .HasIndex(x => new { x.Provider, x.RepositoryPath })
            .IsUnique();

        modelBuilder.Entity<RepositoryConnectionEntity>()
            .Property(x => x.DisplayName)
            .HasMaxLength(200);

        modelBuilder.Entity<RepositoryConnectionEntity>()
            .Property(x => x.Provider)
            .HasMaxLength(50);

        modelBuilder.Entity<RepositoryConnectionEntity>()
            .Property(x => x.RepositoryPath)
            .HasMaxLength(300);

        modelBuilder.Entity<ServiceIntegrationEntity>()
            .Property(x => x.Provider)
            .HasMaxLength(50);

        modelBuilder.Entity<ServiceIntegrationEntity>()
            .Property(x => x.DisplayName)
            .HasMaxLength(200);
    }
}
