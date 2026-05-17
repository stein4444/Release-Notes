using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Infrastructure.Persistence.Entities;

namespace ReleaseNotes.Infrastructure.Persistence;

public sealed class ReleaseNotesDbContext(DbContextOptions<ReleaseNotesDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<ReleaseNoteJobEntity> Jobs => Set<ReleaseNoteJobEntity>();
    public DbSet<ReleaseNoteDocumentEntity> Documents => Set<ReleaseNoteDocumentEntity>();
    public DbSet<RepositoryConnectionEntity> RepositoryConnections => Set<RepositoryConnectionEntity>();
    public DbSet<ServiceIntegrationEntity> ServiceIntegrations => Set<ServiceIntegrationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<UserEntity>()
            .HasIndex(x => x.Email)
            .IsUnique();
        modelBuilder.Entity<UserEntity>()
            .Property(x => x.Email)
            .HasMaxLength(256);
        modelBuilder.Entity<UserEntity>()
            .Property(x => x.DisplayName)
            .HasMaxLength(200);

        modelBuilder.Entity<ReleaseNoteJobEntity>().ToTable("Jobs").HasKey(x => x.Id);
        modelBuilder.Entity<ReleaseNoteDocumentEntity>().ToTable("Documents").HasKey(x => x.Id);
        modelBuilder.Entity<RepositoryConnectionEntity>().ToTable("RepositoryConnections").HasKey(x => x.Id);
        modelBuilder.Entity<ServiceIntegrationEntity>().ToTable("ServiceIntegrations").HasKey(x => x.Id);
        modelBuilder.Entity<UserEntity>().ToTable("Users");

        modelBuilder.Entity<RepositoryConnectionEntity>()
            .HasIndex(x => new { x.OwnerUserId, x.Provider, x.RepositoryPath })
            .IsUnique();

        modelBuilder.Entity<RepositoryConnectionEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ReleaseNoteDocumentEntity>()
            .HasIndex(x => x.OwnerUserId);

        modelBuilder.Entity<ReleaseNoteJobEntity>()
            .HasIndex(x => x.OwnerUserId);

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
