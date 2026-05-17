using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Application.Models;

namespace ReleaseNotes.Worker;

public sealed class ReleaseNotesSchedulerService(
    IServiceScopeFactory scopeFactory,
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
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var connectionReader = scope.ServiceProvider.GetRequiredService<IRepositoryConnectionReader>();
                    var useCase = scope.ServiceProvider.GetRequiredService<IGenerateReleaseNotesUseCase>();
                    var ownerId = await connectionReader.GetOwnerUserIdAsync(
                        scheduler.RepositoryConnectionId,
                        stoppingToken);
                    if (ownerId is null)
                    {
                        logger.LogWarning(
                            "Scheduler: connection {ConnectionId} not found or has no owner.",
                            scheduler.RepositoryConnectionId);
                        continue;
                    }

                    await useCase.ExecuteAsync(
                        new GenerateReleaseNotesRequest(
                            scheduler.RepositoryConnectionId,
                            baseT,
                            targetT,
                            stoppingToken,
                            ownerId.Value),
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
