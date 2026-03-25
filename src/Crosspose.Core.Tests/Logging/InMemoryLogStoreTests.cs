using Crosspose.Core.Logging.Internal;

namespace Crosspose.Core.Tests.Logging;

public class InMemoryLogStoreTests
{
    [Fact]
    public void Write_AddsToSnapshot()
    {
        var store = new InMemoryLogStore();
        store.Write("line 1");
        store.Write("line 2");

        var snapshot = store.Snapshot();
        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public void Write_TriggersOnWriteEvent()
    {
        var store = new InMemoryLogStore();
        var received = new List<string>();
        store.OnWrite += line => received.Add(line);

        store.Write("hello");

        Assert.Single(received);
        Assert.Equal("hello", received[0]);
    }

    [Fact]
    public void Write_CapsAt1000Lines()
    {
        var store = new InMemoryLogStore();
        for (int i = 0; i < 1100; i++)
        {
            store.Write($"line {i}");
        }

        var snapshot = store.Snapshot();
        Assert.True(snapshot.Count <= 1000);
    }

    [Fact]
    public void Clear_RemovesAllLines()
    {
        var store = new InMemoryLogStore();
        store.Write("a");
        store.Write("b");

        store.Clear();

        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void ReadAll_ReturnsJoinedLines()
    {
        var store = new InMemoryLogStore();
        store.Write("first");
        store.Write("second");

        var result = store.ReadAll();

        Assert.Contains("first", result);
        Assert.Contains("second", result);
    }

    [Fact]
    public void Write_SanitizesSecrets()
    {
        var store = new InMemoryLogStore();
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        store.Write($"Token: {jwt}");

        var snapshot = store.Snapshot();
        Assert.DoesNotContain("eyJhbGci", snapshot[0]);
        Assert.Contains("[REDACTED-JWT]", snapshot[0]);
    }
}
