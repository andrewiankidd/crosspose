namespace Crosspose.Ui;

public sealed class ChartSourceListItem
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsOci { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? BearerToken { get; set; }
    public string? Filter { get; set; }
    public string Display => $"{Name} ({(IsOci ? "OCI" : "Helm")})";
}
