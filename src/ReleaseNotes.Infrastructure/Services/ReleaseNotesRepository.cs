using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Domain.Models;
using ReleaseNotes.Infrastructure.Persistence;

namespace ReleaseNotes.Infrastructure.Services;

public sealed class ReleaseNotesRepository(ReleaseNotesDbContext dbContext) : IReleaseNotesRepository
{
    public async Task SaveJobAsync(ReleaseNoteJob job, CancellationToken cancellationToken)
    {
        await dbContext.Jobs.AddAsync(job.ToEntity(), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateJobAsync(ReleaseNoteJob job, CancellationToken cancellationToken)
    {
        dbContext.Jobs.Update(job.ToEntity());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveDocumentAsync(ReleaseNoteDocument document, CancellationToken cancellationToken)
    {
        await dbContext.Documents.AddAsync(document.ToEntity(), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReleaseNoteDocument?> GetDocumentAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity?.ToModel();
    }

    public async Task<IReadOnlyCollection<ReleaseNoteDocument>> GetLatestAsync(int count, CancellationToken cancellationToken)
    {
        var entities = await dbContext.Documents.AsNoTracking()
            .OrderByDescending(x => x.GeneratedAt)
            .Take(count)
            .ToListAsync(cancellationToken);

        return entities.Select(d => d.ToModel()).ToList();
    }
}
