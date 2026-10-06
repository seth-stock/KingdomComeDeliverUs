// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace KcdUs.Agent.Worlds;

/// <summary>Explicit, offline slot leasing. The game still sees only its five
/// supported playlines. Whole directories (including metadata) are archived;
/// a durable journal allows either rename of a slot switch to be recovered.</summary>
public sealed class SlotLeaseManager
{
    public sealed record Switch(int Schema, string SaveRoot, int Playline, string LeaseId, bool Restore,
        Dictionary<string, string> OldFiles, Dictionary<string, string> NewFiles, bool Complete);
    public sealed record Lease(int Schema, string SaveRoot, int Playline, string Id, Dictionary<string, string> OriginalFiles, bool Restored);
    private readonly string _root, _archive, _switches;
    private readonly Func<bool> _running;
    private readonly Action<string>? _fault;
    public SlotLeaseManager(string root, string archive, Func<bool>? gameRunning = null, Action<string>? fault = null)
    {
        _root = Path.GetFullPath(root); _archive = Path.GetFullPath(archive);
        _switches = Path.Combine(Path.GetDirectoryName(_root)!, ".kcdus-slot-switches");
        _running = gameRunning ?? (() => Process.GetProcessesByName("KingdomCome").Length > 0); _fault = fault;
        if (_archive.Equals(_root, StringComparison.OrdinalIgnoreCase) || _archive.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Slot archives must live outside the game's saves directory.");
    }
    private void Stopped() { if (_running()) throw new InvalidOperationException("Close Kingdom Come before archiving or restoring a slot."); }
    private static void NoLinks(string path)
    {
        for (var p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Slot operations cannot traverse reparse points.");
    }
    private string Slot(int i)
    {
        if (i < 0 || i >= SaveStore.MaxPlaylines) throw new ArgumentOutOfRangeException(nameof(i));
        string p = Path.Combine(_root, "playline" + i); NoLinks(p); return p;
    }
    private string LeaseDir(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid lease ID.");
        string p = Path.Combine(_archive, "slot-leases", id); NoLinks(p); return p;
    }
    private static string Hash(string p)
    { using var f = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
    private static Dictionary<string, string> Scan(string dir)
    {
        NoLinks(dir); var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string sub)
        {
            NoLinks(sub);
            foreach (string p in Directory.EnumerateFiles(sub)) { NoLinks(p); files.Add(Path.GetRelativePath(dir, p), Hash(p)); }
            foreach (string p in Directory.EnumerateDirectories(sub)) Visit(p);
        }
        if (Directory.Exists(dir)) Visit(dir);
        return files;
    }
    private static bool Equal(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var h) && h == p.Value);
    private static void Verify(string dir, Dictionary<string, string> expected)
    { if (!Equal(Scan(dir), expected)) throw new IOException("Slot contents changed or archive verification failed. All copies were preserved."); }
    private static void WriteJson<T>(string p, T value)
    {
        NoLinks(p); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Slot metadata exceeds the recoverable journal limit.");
        string temp = p + "." + Guid.NewGuid().ToString("N") + ".part";
        using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes); f.Flush(true); }
        if (!File.ReadAllBytes(temp).AsSpan().SequenceEqual(bytes)) throw new IOException("Journal readback failed.");
        File.Move(temp, p, true);
    }
    private static T Read<T>(string p)
    {
        NoLinks(p); if (new FileInfo(p).Length > 4 * 1024 * 1024) throw new InvalidDataException("Slot journal too large.");
        return JsonSerializer.Deserialize<T>(File.ReadAllBytes(p)) ?? throw new InvalidDataException("Missing slot journal.");
    }
    private static void CopyTree(string source, string destination, Dictionary<string, string> files)
    {
        Directory.CreateDirectory(destination);
        foreach (var (name, _) in files)
        {
            string input = Path.GetFullPath(Path.Combine(source, name)), output = Path.GetFullPath(Path.Combine(destination, name));
            if (!input.StartsWith(Path.GetFullPath(source) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !output.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid archive path.");
            NoLinks(input); NoLinks(output); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            DateTime written = File.GetLastWriteTimeUtc(input), created = File.GetCreationTimeUtc(input);
            FileAttributes attributes = File.GetAttributes(input);
            using (var from = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var to = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { from.CopyTo(to); to.Flush(true); }
            File.SetLastWriteTimeUtc(output, written);
            File.SetCreationTimeUtc(output, created);
            File.SetAttributes(output, attributes);
        }
        // Preserve empty subdirectories too; saves and their metadata stay together.
        if (Directory.Exists(source))
            foreach (string sub in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            { NoLinks(sub); Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, sub))); }
        Verify(destination, files); Verify(source, files);
    }
    private FileStream Guard()
    {
        Stopped(); NoLinks(_root); NoLinks(_archive); NoLinks(_switches); Directory.CreateDirectory(_archive);
        Directory.CreateDirectory(_root);
        // Shared with ordinary received-world installation.
        return new FileStream(Path.Combine(_root, ".kcdus-save.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private void Validate(Lease lease)
    {
        if (lease.Schema != 1 || !string.Equals(lease.SaveRoot, _root, StringComparison.OrdinalIgnoreCase) || lease.OriginalFiles is null)
            throw new InvalidDataException("Invalid slot lease.");
        Slot(lease.Playline); LeaseDir(lease.Id);
    }
    public IReadOnlyList<Lease> List()
    {
        string dir = Path.Combine(_archive, "slot-leases"); NoLinks(dir);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateDirectories(dir).Where(p => File.Exists(Path.Combine(p, "lease.json")))
            .Select(p =>
            {
                var lease = Read<Lease>(Path.Combine(p, "lease.json")); Validate(lease);
                if (lease.Id != Path.GetFileName(p)) throw new InvalidDataException("Lease identity differs from its archive directory.");
                return lease;
            }).ToList();
    }
    private void Finish(string dir, Switch s)
    {
        if (s.Schema != 1 || s.SaveRoot != _root || s.OldFiles is null || s.NewFiles is null) throw new InvalidDataException("Invalid slot switch.");
        string leaseDir = LeaseDir(s.LeaseId), slot = Slot(s.Playline), old = Path.Combine(dir, "old"), stage = Path.Combine(dir, "new");
        var lease = Read<Lease>(Path.Combine(leaseDir, "lease.json")); Validate(lease);
        if (lease.Playline != s.Playline || lease.Id != s.LeaseId) throw new InvalidDataException("Lease identity or playline differs.");
        Stopped();
        if (Directory.Exists(stage))
        {
            Verify(stage, s.NewFiles);
            if (!Directory.Exists(old))
            {
                Verify(slot, s.OldFiles); Stopped();
                if (Directory.Exists(slot)) Directory.Move(slot, old); else Directory.CreateDirectory(old);
                _fault?.Invoke("OldRenamed");
            }
            else { Verify(old, s.OldFiles); if (Directory.Exists(slot)) throw new IOException("Unexpected slot during recovery."); }
            Stopped(); Directory.Move(stage, slot); _fault?.Invoke("NewRenamed");
        }
        Verify(slot, s.NewFiles); Verify(old, s.OldFiles);
        // The displaced tree remains in the same-volume recovery directory; never delete it.
        WriteJson(Path.Combine(leaseDir, "lease.json"), lease with { Restored = s.Restore });
        WriteJson(Path.Combine(dir, "switch.json"), s with { Complete = true });
        _fault?.Invoke("SwitchCommitted");
    }
    private void RecoverCore()
    {
        if (!Directory.Exists(_switches)) return;
        foreach (string dir in Directory.EnumerateDirectories(_switches))
        {
            NoLinks(dir); string p = Path.Combine(dir, "switch.json"); if (!File.Exists(p)) continue;
            var s = Read<Switch>(p); if (!s.Complete) Finish(dir, s);
        }
    }
    public void Recover() { using var guard = Guard(); RecoverCore(); }
    private void Swap(Lease lease, string? replacement, Dictionary<string, string> replacementFiles, bool restore)
    {
        string dir = Path.Combine(_switches, Guid.NewGuid().ToString("N")), stage = Path.Combine(dir, "new");
        Directory.CreateDirectory(dir);
        if (replacement is null) Directory.CreateDirectory(stage); else CopyTree(replacement, stage, replacementFiles);
        var oldFiles = Scan(Slot(lease.Playline));
        var s = new Switch(1, _root, lease.Playline, lease.Id, restore, oldFiles, replacementFiles, false);
        WriteJson(Path.Combine(dir, "switch.json"), s); _fault?.Invoke("SwitchPrepared"); Finish(dir, s);
    }
    public Lease Archive(int playline, Action<Lease>? bind = null)
    {
        using var guard = Guard(); RecoverCore();
        string slot = Slot(playline); var original = Scan(slot);
        if (original.Count == 0) throw new InvalidOperationException("The slot is already empty.");
        var lease = new Lease(1, _root, playline, Guid.NewGuid().ToString("N"), original, false);
        string dir = LeaseDir(lease.Id); CopyTree(slot, Path.Combine(dir, "original"), original);
        WriteJson(Path.Combine(dir, "lease.json"), lease); _fault?.Invoke("ArchiveVerified");
        bind?.Invoke(lease);
        Swap(lease, null, [], false); return lease;
    }
    public void Restore(string id, Action<Lease>? bindOutgoing = null)
    {
        using var guard = Guard(); RecoverCore();
        string dir = LeaseDir(id); var lease = Read<Lease>(Path.Combine(dir, "lease.json")); Validate(lease);
        if (lease.Id != id) throw new InvalidDataException("Lease identity differs from its archive directory.");
        if (lease.Restored) return;
        string original = Path.Combine(dir, "original"); Verify(original, lease.OriginalFiles);
        var outgoing = Scan(Slot(lease.Playline));
        if (outgoing.Count > 0)
        {
            var displaced = new Lease(1, _root, lease.Playline, Guid.NewGuid().ToString("N"), outgoing, false);
            string displacedDir = LeaseDir(displaced.Id);
            CopyTree(Slot(lease.Playline), Path.Combine(displacedDir, "original"), outgoing);
            WriteJson(Path.Combine(displacedDir, "lease.json"), displaced);
            bindOutgoing?.Invoke(displaced);
        }
        // The outgoing co-op tree is retained by Swap's old/ recovery directory.
        Swap(lease, original, lease.OriginalFiles, true);
    }
}
