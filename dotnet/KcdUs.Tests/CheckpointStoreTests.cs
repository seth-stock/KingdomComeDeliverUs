using Coop.Persistence;
using Xunit;

public sealed class CheckpointStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coop-checkpoint-" + Guid.NewGuid().ToString("N"));
    private readonly string _world = Guid.NewGuid().ToString("N"), _participant = Guid.NewGuid().ToString("N");
    private CheckpointStore Store => new(_root);
    private CheckpointManifest Create(string? parent = null)
    {
        string blob = Store.PutArtifact([1, 2, 3]);
        return new(1, _world, Guid.NewGuid().ToString("N"), parent, Guid.NewGuid().ToString("N"), "test-build",
            CheckpointStore.Hash([7]), blob, [new(_participant, "Henry", blob, blob)]);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    [Fact] public void DescendantsAndDivergenceUseAncestryNotTimestamps()
    {
        var a = Create(); Store.PublishCheckpoint(a);
        var b = Create(a.CheckpointId); Store.PublishCheckpoint(b);
        var c = Create(a.CheckpointId); Store.PublishCheckpoint(c);
        Assert.Equal(CheckpointRelation.LocalDescendant, CheckpointStore.Compare(b, a, Store.ReadCheckpoint));
        Assert.Equal(CheckpointRelation.RemoteDescendant, CheckpointStore.Compare(a, b, Store.ReadCheckpoint));
        Assert.Equal(CheckpointRelation.Divergent, CheckpointStore.Compare(b, c, Store.ReadCheckpoint));
    }
    [Fact] public void MissingParentAndCyclesCannotClaimAncestry()
    {
        var a = Create(); var b = Create(a.CheckpointId);
        Assert.Equal(CheckpointRelation.Unknown, CheckpointStore.Compare(a, b, _ => null));
        var cycle = b with { ParentId = b.CheckpointId };
        Assert.Equal(CheckpointRelation.Unknown, CheckpointStore.Compare(a, cycle, _ => cycle));
    }
    [Fact] public void MissingCharacterPreventsPublication()
    {
        var a = Create() with { Participants = [new(_participant, "Henry", new string('a', 64), new string('b', 64))] };
        Assert.Throws<FileNotFoundException>(() => Store.PublishCheckpoint(a));
        Assert.Empty(Store.Checkpoints());
    }
    [Fact] public void ImmutablePublicationAndReadbackDetectCorruption()
    {
        var a = Create(); Store.PublishCheckpoint(a); Store.PublishCheckpoint(a);
        Assert.Throws<IOException>(() => Store.PublishCheckpoint(a with { BranchId = Guid.NewGuid().ToString("N") }));
        File.WriteAllBytes(Path.Combine(_root, "blobs", a.WorldHash), [9]);
        Assert.Throws<InvalidDataException>(() => Store.ReadCheckpoint(a.CheckpointId));
    }
    [Fact] public void SameIdWithDifferentParticipantsIsNotTheSameCheckpoint()
    {
        var a = Create(); var b = a with { Participants = [new(Guid.NewGuid().ToString("N"), "Henry", a.WorldHash, a.WorldHash)] };
        Assert.Equal(CheckpointRelation.Incompatible, CheckpointStore.Compare(a, b, _ => null));
    }
    [Fact] public void RejectsPathTraversalAndIncompatibleParents()
    {
        Assert.Throws<InvalidDataException>(() => Store.ReadArtifact("../../save"));
        var a = Create(); Store.PublishCheckpoint(a);
        Assert.Throws<InvalidDataException>(() => Store.PublishCheckpoint(Create(a.CheckpointId) with { GameBuild = "other" }));
    }
}
