namespace ReleaseNotes.Application.Interfaces;

public interface ICurrentUserAccessor
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    Guid GetRequiredUserId();
}
