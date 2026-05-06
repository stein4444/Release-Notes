namespace ReleaseNotes.Web.Models;

public sealed record GithubWebhookPayload(Guid RepositoryConnectionId, string BaseTag, string TargetTag);
