// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;

namespace KcdUs.Agent.Worlds;

/// <summary>Immutable, local personal snapshots. Never falls back to a different playline.</summary>
public sealed class CharacterCheckpoint
{
    private readonly string _root;
    public CharacterCheckpoint(string backupRoot) => _root = Path.Combine(backupRoot, "characters");
    private static void NoLinks(string path)
    {
        for (var d = new DirectoryInfo(Path.GetFullPath(path)); d != null; d = d.Parent)
            if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Character snapshots must not traverse a reparse point.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Character snapshot is a link.");
    }
    private string FileFor(string hash)
    {
        if (hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Invalid character snapshot digest.");
        string path = Path.Combine(_root, hash + ".whs"); NoLinks(path); return path;
    }
    public string Capture(byte[] source)
    {
        _ = ExactTraitsSave.CaptureCharacter(source);
        string hash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(), path = FileFor(hash);
        Directory.CreateDirectory(_root);
        if (File.Exists(path)) { _ = Read(hash); return hash; }
        string part = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using (var stream = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(source); stream.Flush(true); }
            if (!File.ReadAllBytes(part).AsSpan().SequenceEqual(source)) throw new IOException("Character snapshot readback failed.");
            File.Move(part, path);
        }
        finally { if (File.Exists(part)) File.Delete(part); }
        return hash;
    }
    public ExactTraitsSave.CharacterState Read(string hash)
    {
        string path = FileFor(hash);
        using var stream = File.OpenRead(path);
        if (stream.Length > ExactTraitsSave.MaxFileBytes) throw new InvalidDataException("Character snapshot exceeds bounds.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != hash)
            throw new InvalidDataException("Character snapshot is damaged; world replacement was refused.");
        return ExactTraitsSave.CaptureCharacter(bytes);
    }
}
