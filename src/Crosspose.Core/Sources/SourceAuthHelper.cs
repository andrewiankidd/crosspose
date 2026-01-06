using System.Text;

namespace Crosspose.Core.Sources;

public static class SourceAuthHelper
{
    public static SourceAuthResult HandleAuthFailure(string url, SourceAuth? auth)
    {
        // Azure Container Registry stub
        if (url.Contains(".azurecr.io", StringComparison.OrdinalIgnoreCase))
        {
            return new SourceAuthResult(false, "Azure Container Registry authentication required.");
        }

        if (auth is null || string.IsNullOrWhiteSpace(auth.Username))
        {
            return new SourceAuthResult(false, "Credentials not provided.");
        }

        return new SourceAuthResult(false, "Authentication failed.");
    }

    public static void ApplyAuth(HttpRequestMessage req, SourceAuth? auth)
    {
        if (auth is null) return;

        if (!string.IsNullOrWhiteSpace(auth.BearerToken))
        {
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.BearerToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(auth.Username))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{auth.Username}:{auth.Password ?? string.Empty}"));
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", token);
        }
    }
}
