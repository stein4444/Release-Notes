using ReleaseNotes.Application.Interfaces;

namespace ReleaseNotes.Infrastructure.Services;

public sealed class NullProgressNotifier : IProgressNotifier
{
    public Task ReportAsync(Guid jobId, string stage, string message, CancellationToken cancellationToken) => Task.CompletedTask;
}
