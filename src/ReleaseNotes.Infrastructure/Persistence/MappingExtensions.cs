using System.Text.Json;
using ReleaseNotes.Domain.Models;
using ReleaseNotes.Infrastructure.Persistence.Entities;

namespace ReleaseNotes.Infrastructure.Persistence;

public static class MappingExtensions
{
    public static ReleaseNoteJobEntity ToEntity(this ReleaseNoteJob model) => new()
    {
        Id = model.Id,
        Repository = model.Repository,
        BaseTag = model.BaseTag,
        TargetTag = model.TargetTag,
        Status = model.Status,
        ErrorMessage = model.ErrorMessage,
        CreatedAt = model.CreatedAt,
        CompletedAt = model.CompletedAt
    };

    public static ReleaseNoteDocumentEntity ToEntity(this ReleaseNoteDocument model) => new()
    {
        Id = model.Id,
        Repository = model.Repository,
        BaseTag = model.BaseTag,
        TargetTag = model.TargetTag,
        AiSummary = model.AiSummary,
        GeneratedAt = model.GeneratedAt,
        EntriesJson = JsonSerializer.Serialize(model.Entries, ReleaseNoteJson.EntryOptions)
    };

    public static ReleaseNoteDocument ToModel(this ReleaseNoteDocumentEntity entity) => new()
    {
        Id = entity.Id,
        Repository = entity.Repository,
        BaseTag = entity.BaseTag,
        TargetTag = entity.TargetTag,
        AiSummary = entity.AiSummary,
        GeneratedAt = entity.GeneratedAt,
        Entries = JsonSerializer.Deserialize<IReadOnlyCollection<ReleaseNoteEntry>>(entity.EntriesJson, ReleaseNoteJson.EntryOptions)
                  ?? Array.Empty<ReleaseNoteEntry>()
    };
}
