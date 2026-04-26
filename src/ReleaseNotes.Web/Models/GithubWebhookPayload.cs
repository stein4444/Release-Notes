namespace ReleaseNotes.Web.Models;

public sealed record GithubWebhookPayload(string Repository, string BaseTag, string TargetTag);
