using Microsoft.AspNetCore.SignalR;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Web.Hubs;

namespace ReleaseNotes.Web.Services;

public sealed class SignalRProgressNotifier(IHubContext<ReleaseProgressHub> hubContext) : IProgressNotifier
{
    public Task ReportAsync(Guid jobId, string stage, string message, CancellationToken cancellationToken)
    {
        return hubContext.Clients.All.SendAsync("ReleaseProgress", new
        {
            JobId = jobId,
            Stage = stage,
            Message = message,
            At = DateTimeOffset.UtcNow
        }, cancellationToken);
    }
}
