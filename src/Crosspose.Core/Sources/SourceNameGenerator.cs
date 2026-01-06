using System;

namespace Crosspose.Core.Sources;

public static class SourceNameGenerator
{
    public static string Derive(string address, string prefix)
    {
        try
        {
            var uri = new Uri(address);
            var host = uri.Host.Replace(".", "-");
            return $"{prefix}-{host}";
        }
        catch
        {
            return $"{prefix}-{Guid.NewGuid():N}".Substring(0, 12);
        }
    }
}
