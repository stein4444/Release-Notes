using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Application.Models;

namespace ReleaseNotes.Worker;

public sealed class ReleaseNotesSchedulerService(
    IGenerateReleaseNotesUseCase useCase,
    IOptions<SchedulerOptions> options,
    ILogger<ReleaseNotesSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var scheduler = options.Value;
                if (scheduler.Enabled && scheduler.RepositoryConnectionId != Guid.Empty &&
                    !string.IsNullOrWhiteSpace(scheduler.BaseTag) &&
                    !string.IsNullOrWhiteSpace(scheduler.TargetTag))
                {
                    await useCase.ExecuteAsync(
                        new GenerateReleaseNotesRequest(
                            scheduler.RepositoryConnectionId,
                            scheduler.BaseTag.Trim(),
                            scheduler.TargetTag.Trim(),
                            stoppingToken),
                        stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled release notes generation failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
