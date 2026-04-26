using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Infrastructure.Persistence.Entities;

namespace ReleaseNotes.Infrastructure.Persistence;

public sealed class ReleaseNotesDbContext(DbContextOptions<ReleaseNotesDbContext> options) : DbContext(options)
{
    public DbSet<ReleaseNoteJobEntity> Jobs => Set<ReleaseNoteJobEntity>();
    public DbSet<ReleaseNoteDocumentEntity> Documents => Set<ReleaseNoteDocumentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReleaseNoteJobEntity>().HasKey(x => x.Id);
        modelBuilder.Entity<ReleaseNoteDocumentEntity>().HasKey(x => x.Id);
    }
}
