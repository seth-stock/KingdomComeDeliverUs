// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text.Json;

namespace KcdUs.Agent.Worlds;

/// <summary>Recoverable replacement of the saves owned by a shared-world slot.
/// Originals are COPIED and verified before the destination changes. Disk completion
/// is deliberately distinct from engine-load verification.</summary>
public sealed class SaveInstallTransaction
{
    public sealed record Journal(int Schema, string SaveRoot, int Playline, string WorldId, string NewHash,
        Dictionary<string, string> Originals, bool DiskCommitted);
    private readonly string _root, _archive;
    private readonly Action<string>? _fault;
    public SaveInstallTransaction(string root, string archive, Action<string>? fault = null)
    { _root = Path.GetFullPath(root); _archive = Path.GetFullPath(archive); _fault = fault; }
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    private static string HashFile(string p) => Hash(File.ReadAllBytes(p));
    private static void NoLinks(string path)
    {
        for (var p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Save transactions must not traverse a reparse point.");
    }
    private static void DurableWrite(string p, byte[] bytes, bool replace = false)
    {
        NoLinks(p); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        string tmp = p + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { f.Write(bytes); f.Flush(true); }
            if (HashFile(tmp) != Hash(bytes)) throw new IOException("Save readback failed.");
            File.Move(tmp, p, replace);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    private FileStream Lock()
    {
        NoLinks(_root); NoLinks(_archive); Directory.CreateDirectory(_archive); Directory.CreateDirectory(_root);
        return new FileStream(Path.Combine(_root, ".kcdus-save.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private string Slot(int i)
    {
        if (i < 0 || i >= SaveStore.MaxPlaylines) throw new InvalidDataException("Invalid playline.");
        string dir = Path.Combine(_root, "playline" + i); NoLinks(dir); return dir;
    }
    private static bool Digest(string s) => s.Length == 64 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private void Validate(Journal j, string dir)
    {
        if (j.Schema != 1 || !string.Equals(Path.GetFullPath(j.SaveRoot), _root, StringComparison.OrdinalIgnoreCase)
            || !Digest(j.NewHash) || j.Originals is null) throw new InvalidDataException("Invalid save transaction.");
        Slot(j.Playline);
        foreach (var (name, hash) in j.Originals)
        {
            if (Path.GetFileName(name) != name || name is "." or ".." || !name.EndsWith(".whs", StringComparison.OrdinalIgnoreCase)
                || !Digest(hash)) throw new InvalidDataException("Invalid original save reference.");
            string p = Path.Combine(dir, "originals", name); NoLinks(p);
            if (HashFile(p) != hash) throw new InvalidDataException("An archived original save is damaged.");
        }
    }
    private void SaveJournal(string dir, Journal j)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(j);
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Save transaction has too many original files.");
        DurableWrite(Path.Combine(dir, "transaction.json"), bytes, true);
    }
    private void CheckCurrent(Journal j)
    {
        string slot = Slot(j.Playline);
        if (!Directory.Exists(slot)) return;
        foreach (string p in Directory.EnumerateFiles(slot, "*.whs"))
        {
            NoLinks(p); string name = Path.GetFileName(p), hash = HashFile(p);
            if (name.Equals("world.whs", StringComparison.OrdinalIgnoreCase) && hash == j.NewHash) continue;
            if (!j.Originals.TryGetValue(name, out var expected) || hash != expected)
                throw new IOException("The playline changed outside the transaction; all copies were preserved. Resolve the conflict before continuing.");
        }
    }
    private void Complete(string dir, Journal j)
    {
        Validate(j, dir); CheckCurrent(j);
        string slot = Slot(j.Playline), target = Path.Combine(slot, "world.whs");
        if (!File.Exists(target) || HashFile(target) != j.NewHash) throw new IOException("Replacement save is not installed.");
        foreach (string p in Directory.EnumerateFiles(slot, "*.whs").ToList())
        {
            if (Path.GetFileName(p).Equals("world.whs", StringComparison.OrdinalIgnoreCase)) continue;
            // Copies in originals/ have been verified before this cleanup.
            if (HashFile(p) != j.Originals[Path.GetFileName(p)]) throw new IOException("Save changed during cleanup.");
            File.Delete(p); _fault?.Invoke("OriginalRemoved");
        }
        SaveJournal(dir, j with { DiskCommitted = true }); _fault?.Invoke("DiskCommitted");
    }
    private void RecoverCore()
    {
        string transactions = Path.Combine(_archive, "transactions"); NoLinks(transactions);
        if (!Directory.Exists(transactions)) return;
        foreach (string dir in Directory.EnumerateDirectories(transactions).OrderBy(x => x, StringComparer.Ordinal))
        {
            NoLinks(dir); string p = Path.Combine(dir, "transaction.json");
            if (!File.Exists(p)) continue; // No published journal: no destination mutation was allowed.
            if (new FileInfo(p).Length > 1024 * 1024) throw new InvalidDataException("Save journal too large.");
            var j = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(p)) ?? throw new InvalidDataException("Missing transaction.");
            if (j.DiskCommitted) continue;
            Validate(j, dir); CheckCurrent(j);
            string target = Path.Combine(Slot(j.Playline), "world.whs");
            if (File.Exists(target) && HashFile(target) == j.NewHash) Complete(dir, j);
            else
            {
                // No replacement reached disk. The old files must still be intact.
                foreach (var (name, hash) in j.Originals)
                    if (!File.Exists(Path.Combine(Slot(j.Playline), name)) || HashFile(Path.Combine(Slot(j.Playline), name)) != hash)
                        throw new IOException("Original save missing before replacement; preserved archive requires manual recovery.");
                // A canceled transaction is recorded separately; do not claim a new save was installed.
                DurableWrite(Path.Combine(dir, "canceled.json"), JsonSerializer.SerializeToUtf8Bytes(j), true);
                File.Move(p, Path.Combine(dir, "transaction.canceled.json"));
            }
        }
    }
    public void Recover() { using var guard = Lock(); RecoverCore(); }
    public string Install(int playline, string worldId, byte[] file, bool replace)
    {
        if (!SaveInfo.Validate(file, out string why)) throw new InvalidDataException("Invalid save: " + why);
        using var guard = Lock(); RecoverCore();
        string slot = Slot(playline);
        var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(slot))
            foreach (string p in Directory.EnumerateFiles(slot, "*.whs"))
            { NoLinks(p); originals.Add(Path.GetFileName(p), HashFile(p)); }
        if (!replace && originals.Count != 0) throw new InvalidOperationException("The playline holds a game of the player's.");
        string dir = Path.Combine(_archive, "transactions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (name, hash) in originals)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(slot, name));
            if (Hash(bytes) != hash) throw new IOException("Original changed during capture.");
            DurableWrite(Path.Combine(dir, "originals", name), bytes);
        }
        DurableWrite(Path.Combine(dir, "replacement.whs"), file);
        var j = new Journal(1, _root, playline, worldId, Hash(file), originals, false);
        Validate(j, dir); SaveJournal(dir, j); _fault?.Invoke("OriginalBackedUp");
        CheckCurrent(j);
        foreach (var (name, hash) in originals)
            if (!File.Exists(Path.Combine(slot, name)) || HashFile(Path.Combine(slot, name)) != hash) throw new IOException("Original changed before installation.");
        Directory.CreateDirectory(slot);
        DurableWrite(Path.Combine(slot, "world.whs"), file, true); _fault?.Invoke("ReplacementInstalled");
        Complete(dir, j);
        return Path.Combine(slot, "world.whs");
    }
}
