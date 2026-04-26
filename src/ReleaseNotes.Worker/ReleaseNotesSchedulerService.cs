using Microsoft.Extensions.Logging;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Application.Models;

namespace ReleaseNotes.Worker;

public sealed class ReleaseNotesSchedulerService(
    IGenerateReleaseNotesUseCase useCase,
    ILogger<ReleaseNotesSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // У MVP планувальник використовує фіксований приклад конфігурації.
                await useCase.ExecuteAsync(
                    new GenerateReleaseNotesRequest("owner/repo", "v1.0.0", "v1.1.0", stoppingToken),
                    stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled release notes generation failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }
}
