namespace ReleaseNotes.Application.Interfaces;

public interface IProgressNotifier
{
    Task ReportAsync(Guid jobId, string stage, string message, CancellationToken cancellationToken);
}
