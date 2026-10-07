// SPDX-License-Identifier: GPL-3.0-only
using KcdUs.Agent.Worlds;
namespace KcdUs.Tests;

public class CharacterCheckpointTests
{
    [Fact]
    public void NativeCoreAndInventoryReplaceExactlyWithoutEditingSourceOrWorldKnowledge()
    {
        var source = CharacterFixture.Save(1_790_000_100, 2, 7, 1);
        var world = CharacterFixture.Save(1_790_000_200, 10, 9000, 22); var original = world.ToArray();
        var state = ExactTraitsSave.CaptureCharacter(source);
        state.Core[0x138B] = new float[] { 23, 52, 39, 67, 0, 0 }.SelectMany(BitConverter.GetBytes).ToArray();
        var staged = ExactTraitsSave.PrepareCharacter(world, state);
        var actual = ExactTraitsSave.CaptureCharacter(staged);
        Assert.True(ExactTraitsSave.SameProgressionAndInventory(state, actual));
        Assert.Equal(state.Core[0x138B], actual.Core[0x138B]);
        Assert.Equal(original, world);
        Assert.Equal(10, SaveInfo.Read(staged)!.Hours);
        Assert.Equal(staged, ExactTraitsSave.PrepareCharacter(staged, state));
    }
    [Fact]
    public void WorldReferencedCoreStateCannotBeCarriedBlindly()
    {
        var source = CharacterFixture.Save(1_790_000_100, 2);
        var state = ExactTraitsSave.CaptureCharacter(source); state.Core[0x137D][6] = 1;
        Assert.Throws<InvalidDataException>(() => ExactTraitsSave.PrepareCharacter(source, state));
    }
    [Fact]
    public void SnapshotsAreContentAddressedAndDamagedDataDoesNotFallBack()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kcdus-character-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CharacterCheckpoint(dir); var file = CharacterFixture.Save(1_790_000_100, 2);
            string hash = store.Capture(file); Assert.Equal(hash, store.Capture(file));
            Assert.True(ExactTraitsSave.SameProgressionAndInventory(ExactTraitsSave.CaptureCharacter(file), store.Read(hash)));
            File.WriteAllText(Path.Combine(dir, "characters", hash + ".whs"), "damaged");
            Assert.Throws<InvalidDataException>(() => store.Read(hash));
            Assert.Throws<InvalidDataException>(() => store.Capture(file));
            Assert.Throws<InvalidDataException>(() => store.Read("../../wrong"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public void ReadbackRejectsChangedInventoryAndProgressionButAllowsResourcesToTick()
    {
        var a = ExactTraitsSave.CaptureCharacter(CharacterFixture.Save(1_790_000_100, 2));
        var b = ExactTraitsSave.CaptureCharacter(CharacterFixture.Save(1_790_000_100, 2));
        b.Core[0x138B][0] ^= 1; Assert.True(ExactTraitsSave.SameProgressionAndInventory(a, b));
        var changed = ExactTraitsSave.CaptureCharacter(CharacterFixture.Save(1_790_000_100, 2, 8));
        Assert.False(ExactTraitsSave.SameProgressionAndInventory(a, changed));
        changed = ExactTraitsSave.CaptureCharacter(CharacterFixture.Save(1_790_000_100, 2, 7, 2));
        Assert.False(ExactTraitsSave.SameProgressionAndInventory(a, changed));
    }
}
