using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ReleaseNotes.Web.Hubs;

[Authorize]
public sealed class ReleaseProgressHub : Hub
{
}
