using Microsoft.Extensions.Logging;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Application.Models;
using ReleaseNotes.Domain.Models;

namespace ReleaseNotes.Application;

public sealed class GenerateReleaseNotesUseCase(
    IGitSourceClient gitSourceClient,
    IRuleEngine ruleEngine,
    IReleaseNotesRepository repository,
    IDistributedLockService lockService,
    IProgressNotifier progressNotifier,
    ILogger<GenerateReleaseNotesUseCase> logger) : IGenerateReleaseNotesUseCase
{
    public async Task<Guid> ExecuteAsync(GenerateReleaseNotesRequest request, CancellationToken cancellationToken)
    {
        var lockKey = $"release-notes:{request.Repository}:{request.BaseTag}:{request.TargetTag}";
        await using var acquiredLock = await lockService.TryAcquireAsync(lockKey, TimeSpan.FromMinutes(5), cancellationToken);
        if (acquiredLock is null)
        {
            throw new InvalidOperationException("Generation is already running for this release range.");
        }

        var job = new ReleaseNoteJob
        {
            Repository = request.Repository,
            BaseTag = request.BaseTag,
            TargetTag = request.TargetTag
        };

        await repository.SaveJobAsync(job, cancellationToken);
        await progressNotifier.ReportAsync(job.Id, "job", "Job created", cancellationToken);

        try
        {
            var artifacts = await gitSourceClient.GetArtifactsAsync(request.Repository, request.BaseTag, request.TargetTag, cancellationToken);
            await progressNotifier.ReportAsync(job.Id, "ingestion", $"Fetched {artifacts.Count} artifacts", cancellationToken);

            var entries = new List<ReleaseNoteEntry>(artifacts.Count);
            foreach (var artifact in artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ruleResult = ruleEngine.Classify(artifact);
                entries.Add(ruleResult);
            }

            var summary = BuildSummary(entries);
            var document = new ReleaseNoteDocument
            {
                Repository = request.Repository,
                BaseTag = request.BaseTag,
                TargetTag = request.TargetTag,
                AiSummary = summary,
                Entries = entries
            };

            await repository.SaveDocumentAsync(document, cancellationToken);
            await progressNotifier.ReportAsync(job.Id, "done", "Document stored", cancellationToken);

            job.Status = "Completed";
            job.CompletedAt = DateTimeOffset.UtcNow;
            await repository.UpdateJobAsync(job, cancellationToken);
            return document.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Release note generation failed for {Repository} {Base}->{Target}", request.Repository, request.BaseTag, request.TargetTag);
            job.Status = "Failed";
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
            await repository.UpdateJobAsync(job, cancellationToken);
            throw;
        }
    }

    private static string BuildSummary(IReadOnlyCollection<ReleaseNoteEntry> entries)
    {
        if (entries.Count == 0)
        {
            return "No changes detected for selected range.";
        }

        var grouped = entries
            .GroupBy(x => x.Category)
            .Select(g => $"{g.Key}: {g.Count()}");

        return $"Release summary -> {string.Join(", ", grouped)}";
    }
}
