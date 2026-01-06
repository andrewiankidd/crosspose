using System.Text.Json;

namespace Crosspose.Core.Orchestration;

internal static class JsonExtensions
{
    public static string GetPropertyOrDefault(this JsonElement element, string propertyName, string defaultValue = "")
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? defaultValue;
            return value.ToString();
        }
        return defaultValue;
    }
}
