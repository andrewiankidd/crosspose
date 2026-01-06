using System.Text;
using System.Text.Json;

namespace Crosspose.Doctor.Checks;

internal static class AuthTokenInspector
{
    private const int ExpiryBufferSeconds = 60;

    public static bool TryGetExpiration(string token, out DateTimeOffset expiration)
    {
        expiration = default;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        var payload = parts[1];
        if (string.IsNullOrWhiteSpace(payload)) return false;

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(payload));
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("exp", out var expElement) &&
                expElement.TryGetInt64(out var expSeconds))
            {
                expiration = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }

        return false;
    }

    public static bool IsExpired(DateTimeOffset expiration) =>
        expiration <= DateTimeOffset.UtcNow.AddSeconds(ExpiryBufferSeconds);

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input
            .Replace('-', '+')
            .Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
