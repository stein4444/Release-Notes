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
                var baseT = scheduler.BaseTag?.Trim() ?? string.Empty;
                var targetT = scheduler.TargetTag?.Trim() ?? string.Empty;
                var canRun = scheduler.Enabled
                             && scheduler.RepositoryConnectionId != Guid.Empty
                             && (GitIngestMode.IsFullRepositoryHistory(baseT, targetT)
                                 || (!string.IsNullOrWhiteSpace(baseT) && !string.IsNullOrWhiteSpace(targetT)));
                if (canRun)
                {
                    await useCase.ExecuteAsync(
                        new GenerateReleaseNotesRequest(
                            scheduler.RepositoryConnectionId,
                            baseT,
                            targetT,
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
