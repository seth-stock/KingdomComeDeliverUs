// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text.Json;

namespace Coop.Persistence;

public sealed record ParticipantArtifact(string ParticipantId, string CharacterKind, string CharacterHash, string LedgerHash);
public sealed record CheckpointManifest(int Schema, string WorldId, string CheckpointId, string? ParentId,
    string BranchId, string GameBuild, string ContentFingerprint, string WorldHash, IReadOnlyList<ParticipantArtifact> Participants);
public enum CheckpointRelation { Same, LocalDescendant, RemoteDescendant, Divergent, Unknown, Incompatible }

/// <summary>Immutable, content-addressed bundles. All blobs precede publication of a manifest.
/// This store never edits game saves and never prunes an artifact referenced by a checkpoint.</summary>
public sealed class CheckpointStore
{
    public const int Schema = 1;
    public const int MaxArtifactBytes = 64 * 1024 * 1024;
    private readonly string _root;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public CheckpointStore(string root) { _root = Path.GetFullPath(root); }
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Id(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Expected a UUID in N format.");
    }
    private static void Digest(string hash)
    {
        if (hash.Length != 64 || hash.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Invalid SHA-256.");
    }
    private string BlobPath(string hash) { Digest(hash); return Path.Combine(_root, "blobs", hash); }
    private string ManifestPath(string id) { Id(id); return Path.Combine(_root, "checkpoints", id + ".json"); }
    private static void NoLinks(string path)
    {
        for (var p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Checkpoint storage must not traverse a reparse point.");
    }
    private static void Publish(string path, byte[] bytes)
    {
        NoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string part = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using (var f = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { f.Write(bytes); f.Flush(true); }
            if (!File.ReadAllBytes(part).AsSpan().SequenceEqual(bytes)) throw new IOException("Artifact readback failed.");
            try { File.Move(part, path); }
            catch (IOException) when (File.Exists(path))
            {
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new IOException("Immutable artifact collision.");
            }
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }
    public string PutArtifact(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxArtifactBytes) throw new InvalidDataException("Artifact size outside limits.");
        string hash = Hash(bytes); Publish(BlobPath(hash), bytes); return hash;
    }
    public byte[] ReadArtifact(string hash)
    {
        string p = BlobPath(hash); NoLinks(p);
        var info = new FileInfo(p);
        if (info.Length == 0 || info.Length > MaxArtifactBytes) throw new InvalidDataException("Artifact size outside limits.");
        var bytes = File.ReadAllBytes(p);
        if (Hash(bytes) != hash) throw new InvalidDataException("Artifact checksum differs.");
        return bytes;
    }
    private void Validate(CheckpointManifest m)
    {
        if (m.Schema != Schema) throw new InvalidDataException("Unsupported checkpoint schema.");
        Id(m.WorldId); Id(m.CheckpointId); Id(m.BranchId); if (m.ParentId is not null) Id(m.ParentId);
        if (string.IsNullOrWhiteSpace(m.GameBuild) || m.GameBuild.Length > 256) throw new InvalidDataException("Missing game build.");
        Digest(m.ContentFingerprint);
        if (m.Participants is null || m.Participants.Count is < 1 or > 4) throw new InvalidDataException("Invalid participant count.");
        if (m.Participants.Select(p => p.ParticipantId).Distinct(StringComparer.Ordinal).Count() != m.Participants.Count)
            throw new InvalidDataException("Duplicate participant.");
        ReadArtifact(m.WorldHash);
        foreach (var p in m.Participants)
        {
            Id(p.ParticipantId);
            if (string.IsNullOrWhiteSpace(p.CharacterKind) || p.CharacterKind.Length > 64) throw new InvalidDataException("Missing character kind.");
            ReadArtifact(p.CharacterHash); ReadArtifact(p.LedgerHash);
        }
    }
    public void PublishCheckpoint(CheckpointManifest m)
    {
        Validate(m);
        if (m.ParentId is not null)
        {
            var parent = ReadCheckpoint(m.ParentId);
            if (parent.WorldId != m.WorldId || parent.GameBuild != m.GameBuild || parent.ContentFingerprint != m.ContentFingerprint)
                throw new InvalidDataException("Parent checkpoint is incompatible.");
            if (m.ParentId == m.CheckpointId) throw new InvalidDataException("A checkpoint cannot parent itself.");
        }
        Publish(ManifestPath(m.CheckpointId), JsonSerializer.SerializeToUtf8Bytes(m, Json));
    }
    public CheckpointManifest ReadCheckpoint(string id)
    {
        string p = ManifestPath(id); NoLinks(p);
        if (new FileInfo(p).Length > 64 * 1024) throw new InvalidDataException("Manifest too large.");
        var m = JsonSerializer.Deserialize<CheckpointManifest>(File.ReadAllBytes(p)) ?? throw new InvalidDataException("Missing manifest.");
        if (m.CheckpointId != id) throw new InvalidDataException("Checkpoint identity differs.");
        Validate(m); return m;
    }
    public IEnumerable<CheckpointManifest> Checkpoints()
    {
        string dir = Path.Combine(_root, "checkpoints"); NoLinks(dir);
        if (!Directory.Exists(dir)) yield break;
        foreach (var p in Directory.EnumerateFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            yield return ReadCheckpoint(Path.GetFileNameWithoutExtension(p));
    }
    public static CheckpointRelation Compare(CheckpointManifest local, CheckpointManifest remote,
        Func<string, CheckpointManifest?> lookup)
    {
        if (local.WorldId != remote.WorldId || local.GameBuild != remote.GameBuild || local.ContentFingerprint != remote.ContentFingerprint
            || local.Schema != Schema || remote.Schema != Schema) return CheckpointRelation.Incompatible;
        if (local.CheckpointId == remote.CheckpointId)
            return local.WorldHash == remote.WorldHash && local.BranchId == remote.BranchId && local.ParentId == remote.ParentId
                && local.Participants.SequenceEqual(remote.Participants) ? CheckpointRelation.Same : CheckpointRelation.Incompatible;
        HashSet<string>? Walk(CheckpointManifest start)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { start.CheckpointId };
            var cur = start;
            while (cur.ParentId is { } parent)
            {
                if (!seen.Add(parent)) return null;
                cur = lookup(parent)!;
                if (cur is null || cur.CheckpointId != parent || cur.WorldId != start.WorldId || cur.GameBuild != start.GameBuild
                    || cur.ContentFingerprint != start.ContentFingerprint || cur.Schema != Schema) return null;
            }
            return seen;
        }
        var a = Walk(local); var b = Walk(remote);
        if (a is null || b is null) return CheckpointRelation.Unknown;
        if (a.Contains(remote.CheckpointId)) return CheckpointRelation.LocalDescendant;
        if (b.Contains(local.CheckpointId)) return CheckpointRelation.RemoteDescendant;
        return a.Overlaps(b) ? CheckpointRelation.Divergent : CheckpointRelation.Unknown;
    }
}
