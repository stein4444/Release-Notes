using System.Net.Http.Headers;

namespace ReleaseNotes.Web.Services;

public sealed class BearerTokenHandler(AuthSession authSession) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(authSession.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authSession.Token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
