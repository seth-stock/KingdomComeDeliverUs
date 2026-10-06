using KcdUs.Agent.Worlds;
using Xunit;

namespace KcdUs.Tests;
public sealed class SlotLeaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coop-slots-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(_dir, "saves");
    private string Archive => Path.Combine(_dir, "archive");
    private string Slot => Path.Combine(Root, "playline4");
    public SlotLeaseTests()
    {
        for (int i = 0; i < 5; i++) { Directory.CreateDirectory(Path.Combine(Root, "playline" + i)); File.WriteAllText(Path.Combine(Root, "playline" + i, "own.whs"), "original " + i); }
        Directory.CreateDirectory(Path.Combine(Slot, "metadata")); File.WriteAllText(Path.Combine(Slot, "metadata", "name.txt"), "my world");
    }
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    private SlotLeaseManager Manager(Action<string>? fault = null) => new(Root, Archive, () => false, fault);
    [Fact] public void FullFiveSlotsCanLeaseAndRestoreWithoutLosingCoopProgress()
    {
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(Slot, "own.whs"), timestamp);
        var m = Manager(); var lease = m.Archive(4);
        Assert.Empty(Directory.GetFiles(Slot)); Assert.Equal(5, Directory.GetDirectories(Root).Length);
        File.WriteAllText(Path.Combine(Slot, "world.whs"), "co-op progress");
        m.Restore(lease.Id); m.Restore(lease.Id);
        Assert.Equal("original 4", File.ReadAllText(Path.Combine(Slot, "own.whs")));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(Path.Combine(Slot, "own.whs")));
        Assert.Equal("my world", File.ReadAllText(Path.Combine(Slot, "metadata", "name.txt")));
        Assert.Contains(Directory.GetFiles(_dir, "world.whs", SearchOption.AllDirectories), p => File.ReadAllText(p) == "co-op progress");
        for (int i = 0; i < 4; i++) Assert.Equal("original " + i, File.ReadAllText(Path.Combine(Root, "playline" + i, "own.whs")));
    }
    [Theory] [InlineData("SwitchPrepared")] [InlineData("OldRenamed")] [InlineData("NewRenamed")] [InlineData("SwitchCommitted")]
    public void EachInterruptedSwitchRecovers(string phase)
    {
        Assert.Throws<IOException>(() => Manager(p => { if (p == phase) throw new IOException("power loss"); }).Archive(4));
        var m = Manager(); m.Recover(); m.Recover(); var lease = Assert.Single(m.List());
        Assert.Empty(Directory.GetFiles(Slot)); m.Restore(lease.Id);
        Assert.Equal("original 4", File.ReadAllText(Path.Combine(Slot, "own.whs")));
    }
    [Fact] public void RunningGameAndSixthSlotAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new SlotLeaseManager(Root, Archive, () => true).Archive(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => Manager().Archive(5));
        Assert.Equal("original 4", File.ReadAllText(Path.Combine(Slot, "own.whs")));
    }
    [Fact] public void LogicalHomeDoesNotFollowAReusedSlotAndBothWorldsCanBeRestored()
    {
        var registry = new WorldRegistry { Active = "home", HomePlayline = 4 };
        var home = registry.Upsert("home", "my world"); home.Playline = 4; home.Slot = false;
        var m = Manager(); var original = m.Archive(4, l => SlotBindings.Archive(registry, l));
        Assert.Equal(-1, registry.HomePlayline); Assert.Equal(original.Id, registry.HomeLeaseId);
        Assert.Equal(-1, home.Playline); Assert.Equal("", registry.Active);
        File.WriteAllText(Path.Combine(Slot, "world.whs"), "co-op");
        var coop = registry.Upsert("coop", "our world"); coop.Playline = 4; coop.Slot = true;
        m.Restore(original.Id, l => SlotBindings.Archive(registry, l)); SlotBindings.Restore(registry, original);
        Assert.Equal(4, registry.HomePlayline); Assert.Equal(4, home.Playline); Assert.False(home.Slot);
        Assert.Equal(-1, coop.Playline); Assert.NotEmpty(coop.ArchivedLeaseId);
        string coopLease = coop.ArchivedLeaseId;
        m.Restore(coopLease, l => SlotBindings.Archive(registry, l));
        SlotBindings.Restore(registry, m.List().Single(l => l.Id == coopLease));
        Assert.Equal("co-op", File.ReadAllText(Path.Combine(Slot, "world.whs"))); Assert.Equal(4, coop.Playline); Assert.True(coop.Slot);
        Assert.Equal(-1, registry.HomePlayline); Assert.NotEmpty(registry.HomeLeaseId);
    }
    [Fact] public void ChangedSlotAfterPreparedSwitchIsPreserved()
    {
        Assert.Throws<IOException>(() => Manager(p => { if (p == "SwitchPrepared") throw new IOException(); }).Archive(4));
        File.WriteAllText(Path.Combine(Slot, "external.whs"), "new cloud save");
        Assert.Throws<IOException>(() => Manager().Recover());
        Assert.Equal("new cloud save", File.ReadAllText(Path.Combine(Slot, "external.whs")));
    }
}
