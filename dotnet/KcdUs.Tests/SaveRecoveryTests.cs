using KcdUs.Agent.Worlds;
using Xunit;

namespace KcdUs.Tests;

public sealed class SaveRecoveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coop-save-recovery-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(_dir, "saves");
    private string Backup => Path.Combine(_dir, "archive");
    private byte[] Old => WorldTests.FakeSave(WorldTests.Desc(1000000001, 1), seed: 1);
    private byte[] New => WorldTests.FakeSave(WorldTests.Desc(1000000002, 2), seed: 2);
    private string Slot => Path.Combine(Root, "playline4");
    public SaveRecoveryTests()
    {
        Directory.CreateDirectory(Slot); File.WriteAllBytes(Path.Combine(Slot, "old.whs"), Old);
        File.WriteAllText(Path.Combine(Slot, "metadata.txt"), "keep");
    }
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    [Theory]
    [InlineData("OriginalBackedUp", false)]
    [InlineData("ReplacementInstalled", true)]
    [InlineData("OriginalRemoved", true)]
    [InlineData("DiskCommitted", true)]
    public void InterruptedInstallRecoversWithoutLosingOriginal(string phase, bool newInstalled)
    {
        var tx = new SaveInstallTransaction(Root, Backup, p => { if (p == phase) throw new IOException("power loss"); });
        Assert.Throws<IOException>(() => tx.Install(4, "world", New, true));
        new SaveInstallTransaction(Root, Backup).Recover();
        Assert.Contains(Directory.GetFiles(Backup, "old.whs", SearchOption.AllDirectories), p => File.ReadAllBytes(p).SequenceEqual(Old));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(Slot, "metadata.txt")));
        if (newInstalled) { Assert.Equal(New, File.ReadAllBytes(Path.Combine(Slot, "world.whs"))); Assert.False(File.Exists(Path.Combine(Slot, "old.whs"))); }
        else Assert.Equal(Old, File.ReadAllBytes(Path.Combine(Slot, "old.whs")));
        new SaveInstallTransaction(Root, Backup).Recover(); // repeat recovery is harmless
    }
    [Fact] public void ExternalSaveAfterCrashIsPreservedAndBlocksRecovery()
    {
        var tx = new SaveInstallTransaction(Root, Backup, p => { if (p == "ReplacementInstalled") throw new IOException(); });
        Assert.Throws<IOException>(() => tx.Install(4, "world", New, true));
        File.WriteAllBytes(Path.Combine(Slot, "external.whs"), Old);
        Assert.Throws<IOException>(() => new SaveInstallTransaction(Root, Backup).Recover());
        Assert.Equal(Old, File.ReadAllBytes(Path.Combine(Slot, "external.whs")));
    }
    [Fact] public void DamagedArchiveBlocksCleanup()
    {
        var tx = new SaveInstallTransaction(Root, Backup, p => { if (p == "ReplacementInstalled") throw new IOException(); });
        Assert.Throws<IOException>(() => tx.Install(4, "world", New, true));
        File.WriteAllText(Directory.GetFiles(Backup, "old.whs", SearchOption.AllDirectories).Single(), "damaged");
        Assert.Throws<InvalidDataException>(() => new SaveInstallTransaction(Root, Backup).Recover());
        Assert.Equal(Old, File.ReadAllBytes(Path.Combine(Slot, "old.whs")));
    }
    [Fact] public void DamagedRegistryDoesNotSilentlyBecomeAnEmptyOne()
    {
        string path = Path.Combine(_dir, "worlds.json"); File.WriteAllText(path, "{interrupted");
        Assert.Throws<InvalidDataException>(() => WorldRegistry.Load(path));
        Assert.Equal("{interrupted", File.ReadAllText(path));
    }
}
