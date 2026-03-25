using Crosspose.Core.Logging.Internal;

namespace Crosspose.Core.Tests.Logging;

/// <summary>
/// Tests secret sanitization by writing through InMemoryLogStore, which invokes
/// SecretCensor.Sanitize internally.
/// </summary>
public class SecretCensorTests
{
    [Fact]
    public void JwtToken_IsRedacted()
    {
        var store = new InMemoryLogStore();
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        store.Write($"Authorization header: {jwt}");

        var snapshot = store.Snapshot();
        Assert.Single(snapshot);
        Assert.DoesNotContain("eyJhbGci", snapshot[0]);
        Assert.Contains("[REDACTED-JWT]", snapshot[0]);
    }

    [Fact]
    public void BearerToken_IsRedacted()
    {
        var store = new InMemoryLogStore();
        store.Write("Using Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9_long_token_value_here for auth");

        var snapshot = store.Snapshot();
        Assert.Single(snapshot);
        Assert.Contains("[REDACTED]", snapshot[0]);
    }

    [Fact]
    public void NormalText_Unchanged()
    {
        var store = new InMemoryLogStore();
        store.Write("docker ps -a --format json");

        var snapshot = store.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal("docker ps -a --format json", snapshot[0]);
    }

    [Fact]
    public void ShortTokenLikeStrings_NotRedacted()
    {
        var store = new InMemoryLogStore();
        store.Write("short.value.here");

        var snapshot = store.Snapshot();
        Assert.Equal("short.value.here", snapshot[0]);
    }

    [Fact]
    public void MultipleSecrets_AllRedacted()
    {
        var store = new InMemoryLogStore();
        var jwt1 = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4iLCJpYXQiOjE1MTYyMzkwMjJ9.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var jwt2 = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiI5ODc2NTQzMjEwIiwibmFtZSI6IkphbmUiLCJpYXQiOjE1MTYyMzkwMjJ9.abc1234567890123456789012345678901234567890";
        store.Write($"Token1: {jwt1} and Token2: {jwt2}");

        var snapshot = store.Snapshot();
        Assert.DoesNotContain("eyJhbGci", snapshot[0]);
    }
}
