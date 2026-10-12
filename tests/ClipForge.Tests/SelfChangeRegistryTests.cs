using ClipForge.Core;
using Xunit;

namespace ClipForge.Tests;

/// <summary>
/// The self-change registry behind paste capture suppression: a paste
/// registers the content hashes of the changes it is about to cause, and a
/// capture matching a registration is dropped, consuming it. These tests pin
/// the semantics that replaced the old fixed time window, which discarded
/// every copy made during the window — ours and the user's alike. The one
/// property that matters most: a genuine copy of DIFFERENT content is never
/// suppressed, whenever it happens.
/// </summary>
public sealed class SelfChangeRegistryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    private const string StagedHash = "Text:AAAA";
    private const string SnapshotHash = "Text:BBBB";
    private const string UserHash = "Text:CCCC";

    [Fact]
    public void RegisteredHashIsConsumedExactlyOnce()
    {
        var registry = new SelfChangeRegistry(Lifetime, () => T0);
        registry.Register(StagedHash);

        Assert.True(registry.TryConsume(StagedHash));
        // The registration is spent: a second identical capture is a normal
        // capture now (and meets the usual duplicate check downstream).
        Assert.False(registry.TryConsume(StagedHash));
    }

    [Fact]
    public void UnregisteredHashIsNotConsumed()
    {
        var registry = new SelfChangeRegistry(Lifetime, () => T0);

        Assert.False(registry.TryConsume(UserHash));
    }

    [Fact]
    public void GenuineCopyOfDifferentContentIsKeptAndConsumesNothing()
    {
        var registry = new SelfChangeRegistry(Lifetime, () => T0);
        registry.Register(StagedHash);
        registry.Register(SnapshotHash);

        // A copy the user made mid-paste, of content we never staged or
        // restored: not ours, so not suppressed — and it must not eat either
        // registration on its way through.
        Assert.False(registry.TryConsume(UserHash));
        Assert.True(registry.TryConsume(StagedHash));
        Assert.True(registry.TryConsume(SnapshotHash));
    }

    [Fact]
    public void TwoRegistrationsOfTheSameHashConsumeTwice()
    {
        // A paste can cause the same content to change twice (stage a clip
        // that is also what the snapshot restores): one suppression each.
        var registry = new SelfChangeRegistry(Lifetime, () => T0);
        registry.Register(StagedHash);
        registry.Register(StagedHash);

        Assert.True(registry.TryConsume(StagedHash));
        Assert.True(registry.TryConsume(StagedHash));
        Assert.False(registry.TryConsume(StagedHash));
    }

    [Fact]
    public void RegistrationExpiresAfterItsLifetime()
    {
        // A registered change that is never captured (its update was missed)
        // must not linger and suppress an identical genuine copy later.
        var now = T0;
        var registry = new SelfChangeRegistry(Lifetime, () => now);
        registry.Register(StagedHash);

        now = T0 + Lifetime + TimeSpan.FromSeconds(1);
        Assert.False(registry.TryConsume(StagedHash));
    }

    [Fact]
    public void RegistrationIsStillLiveBeforeItsLifetime()
    {
        var now = T0;
        var registry = new SelfChangeRegistry(Lifetime, () => now);
        registry.Register(StagedHash);

        now = T0 + Lifetime - TimeSpan.FromSeconds(1);
        Assert.True(registry.TryConsume(StagedHash));
    }

    [Fact]
    public void ExpiredRegistrationDoesNotBlockAFreshOne()
    {
        var now = T0;
        var registry = new SelfChangeRegistry(Lifetime, () => now);
        registry.Register(StagedHash);

        now = T0 + Lifetime + TimeSpan.FromSeconds(1);
        registry.Register(UserHash);

        Assert.False(registry.TryConsume(StagedHash));
        Assert.True(registry.TryConsume(UserHash));
    }
}
