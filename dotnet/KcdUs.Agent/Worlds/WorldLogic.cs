// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Security.Cryptography;
using System.Text.Json;
using KcdUs.Wire;

namespace KcdUs.Agent.Worlds;

/// <summary>How far one copy of a shared world has got: the world's id, the play time and the time of the newest save.</summary>
public sealed record WorldStamp(string WorldId, double Hours, long SavedUnix, string Name = "")
{
    public string Encode() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{WorldId}|{Hours:0.0000}|{SavedUnix}|{Safe.Clean(Name, 40)}");

    public static WorldStamp? Decode(string[] f, int at)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        if (f.Length < at + 3 || f[at].Length == 0 || f[at] == "-") return null;
        if (!double.TryParse(f[at + 1], System.Globalization.NumberStyles.Float, c, out var h) || !long.TryParse(f[at + 2], System.Globalization.NumberStyles.None, c, out var u)) return null;
        if (!double.IsFinite(h) || h < 0 || u < 0) return null;
        return new WorldStamp(Safe.Clean(f[at], 40), h, u, f.Length > at + 3 ? f[at + 3] : "");
    }
}

public enum ResolvePolicy { Furthest, Host, Newest }
public enum Winner { Same, Local, Remote }

/// <summary>
/// When two players reconnect, each holding their own copy of the world, which copy goes on? The copy that is behind is replaced by the other
/// (and kept as a backup); each player's own Henry is carried over separately (the card). Pure, so it is testable and the same on both sides:
/// both ends compute the answer and agree.
/// </summary>
public static class WorldResolve
{
    /// <summary>Two copies within this many hours of play (about 3 minutes) count as the same stretch of play; then the newer save wins.</summary>
    public const double EqualHours = 0.05;

    public static ResolvePolicy? ParsePolicy(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "furthest" => ResolvePolicy.Furthest, "host" => ResolvePolicy.Host, "newest" => ResolvePolicy.Newest, _ => null,
    };

    public static string PolicyName(ResolvePolicy p) => p.ToString().ToLowerInvariant();

    public static Winner Decide(WorldStamp local, WorldStamp remote, ResolvePolicy policy, bool localIsHost)
    {
        if (local.WorldId != remote.WorldId) throw new ArgumentException("these are two different worlds");
        if (Math.Abs(local.Hours - remote.Hours) < 1e-9 && local.SavedUnix == remote.SavedUnix) return Winner.Same;
        switch (policy)
        {
            case ResolvePolicy.Host: return localIsHost ? Winner.Local : Winner.Remote;
            case ResolvePolicy.Furthest when Math.Abs(local.Hours - remote.Hours) >= EqualHours:
                return local.Hours > remote.Hours ? Winner.Local : Winner.Remote;
            default:
                if (local.SavedUnix == remote.SavedUnix) return localIsHost ? Winner.Local : Winner.Remote;
                return local.SavedUnix > remote.SavedUnix ? Winner.Local : Winner.Remote;
        }
    }
}

/// <summary>One shared world as this computer knows it: where its copy is, and how far that copy has got.</summary>
public sealed class WorldRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>The playline folder holding this player's copy: the player's own game (the host's), or a slot the mod took for it (<see cref="Slot"/>).</summary>
    public int Playline { get; set; } = -1;
    /// <summary>The playline is one this world took from an empty slot, so a newer copy may replace what is there (the old one is backed up).</summary>
    public bool Slot { get; set; }
    public double Hours { get; set; }
    public long SavedUnix { get; set; }
    /// <summary>The saved Henry (WorldCard JSON) that goes back into a world that replaces this one.</summary>
    public string Card { get; set; } = "";
    /// <summary>The player's own game that this copy replaced (so it can be found again).</summary>
    public int HomePlayline { get; set; } = -1;
    public string ArchivedLeaseId { get; set; } = "";
    public bool ArchivedWasSlot { get; set; }
    public string HomeLeaseId { get; set; } = "";
    public string VerificationSaveSha256 { get; set; } = "";
    public WorldStamp Stamp => new(Id, Hours, SavedUnix, Name);
}

/// <summary>worlds.json: the shared worlds this computer knows, and which one is active.</summary>
public sealed class WorldRegistry
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public string Active { get; set; } = "";
    public List<WorldRecord> Worlds { get; set; } = new();
    /// <summary>This player's Henry as last read from the game (WorldCard text): what goes onto a world that replaces the one he is in.</summary>
    public string MyCard { get; set; } = "";
    public string MyCharacterSha256 { get; set; } = "";
    public PendingWorldLoad? PendingLoad { get; set; }
    /// <summary>The playline of the game this player had before a shared world replaced it, where "send my Henry home" goes (-1: none known).</summary>
    public int HomePlayline { get; set; } = -1;
    public string HomeLeaseId { get; set; } = "";

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCDUS", "worlds.json");

    public static WorldRegistry Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            if (File.Exists(path))
            {
                var registry = JsonSerializer.Deserialize<WorldRegistry>(File.ReadAllText(path), Json);
                if (registry?.Worlds is null || registry.MyCharacterSha256 is null || registry.HomeLeaseId is null || registry.Worlds.Any(w => w is null || w.Id is null || w.ArchivedLeaseId is null || w.HomeLeaseId is null))
                    throw new InvalidDataException("The shared-world registry is incomplete; restore a verified copy before changing saves.");
                if (registry.PendingLoad is { } pending)
                {
                    bool Digest(string? h) => h is { Length: 64 } && h.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
                    if (pending.Playline < 0 || pending.Playline >= SaveStore.MaxPlaylines || pending.WorldId is not { Length: > 0 and <= 40 }
                        || pending.Name is null || pending.TransferId is null || !Digest(pending.SaveSha256)
                        || pending.CharacterSha256 is null || (pending.CharacterSha256.Length > 0 && !Digest(pending.CharacterSha256))
                        || pending.Phase is not ("Prepared" or "Installed") || pending.From is < 0 or > 249
                        || !double.IsFinite(pending.Hours) || pending.Hours < 0 || pending.SavedUnix < 0)
                        throw new InvalidDataException("Pending world-load journal is damaged; preserve the save archives and restore its verified copy.");
                }
                return registry;
            }
        }
        catch (JsonException e) { throw new InvalidDataException("The shared-world registry is damaged; restore a verified copy before changing saves.", e); }
        return new();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".part";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(this, Json);
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            if (!File.ReadAllBytes(tmp).AsSpan().SequenceEqual(bytes)) throw new IOException("World registry readback failed.");
            File.Move(tmp, path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    public WorldRecord? Find(string id) => Worlds.FirstOrDefault(w => w.Id == id);
    public WorldRecord? ActiveWorld => Find(Active);

    public WorldRecord Upsert(string id, string name)
    {
        var w = Find(id);
        if (w is null) Worlds.Add(w = new WorldRecord { Id = id, Name = name });
        else if (name.Length > 0) w.Name = name;
        return w;
    }

    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>The slots shared worlds took (taken even if their folder is empty for a moment).</summary>
    public IEnumerable<int> SlotsInUse() => Worlds.Where(w => w.Slot && w.Playline >= 0).Select(w => w.Playline);
}

public sealed record PendingWorldLoad(string WorldId, string Name, int Playline, string SaveSha256,
    string CharacterSha256, int From, string TransferId, double Hours, long SavedUnix, string Phase, bool Home = false);

/// <summary>A file in pieces, over the relay's text frames: <c>woffer</c> says what is coming, <c>wchunk</c> carries a piece (base64), and the receiver checks the whole against a SHA-256.</summary>
public static class WorldTransfer
{
    /// <summary>Bytes per piece; base64 makes it 40000 characters, inside the relay's 65535-character frame with room for the header.</summary>
    public const int ChunkBytes = 30_000;
    public const int MaxFileBytes = 64 * 1024 * 1024;

    public sealed record Offer(string TransferId, string WorldId, int Bytes, int Chunks, string Sha256);

    public static Offer MakeOffer(string transferId, string worldId, byte[] file) =>
        new(transferId, worldId, file.Length, (file.Length + ChunkBytes - 1) / ChunkBytes, Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant());

    public static IEnumerable<(int Index, string Base64)> Pieces(byte[] file)
    {
        for (int i = 0, o = 0; o < file.Length; i++, o += ChunkBytes)
            yield return (i, Convert.ToBase64String(file, o, Math.Min(ChunkBytes, file.Length - o)));
    }

    /// <summary>Collects the pieces of one offer. Pieces may arrive in any order or twice; <see cref="Complete"/> is true when all are here and the whole matches the SHA-256.</summary>
    public sealed class Receiver
    {
        private readonly byte[]?[] _parts;
        public Offer Offer { get; }
        public Receiver(Offer o)
        {
            if (o.Bytes <= 0 || o.Bytes > MaxFileBytes || o.Chunks != (o.Bytes + ChunkBytes - 1) / ChunkBytes) throw new InvalidDataException("bad offer");
            Offer = o; _parts = new byte[o.Chunks][];
        }
        public int Have => _parts.Count(p => p is not null);
        public bool Add(int index, string base64)
        {
            if (index < 0 || index >= _parts.Length) return false;
            int expected = Math.Min(ChunkBytes, Offer.Bytes - index * ChunkBytes);
            if (base64.Length != ((expected + 2) / 3) * 4) return false;
            try
            {
                var bytes = Convert.FromBase64String(base64);
                if (bytes.Length != expected) return false;
                if (_parts[index] is { } previous) return previous.AsSpan().SequenceEqual(bytes);
                _parts[index] = bytes; return true;
            }
            catch (FormatException) { return false; }
        }
        public IEnumerable<int> Missing() { for (int i = 0; i < _parts.Length; i++) if (_parts[i] is null) yield return i; }
        public bool Complete => Have == _parts.Length;

        /// <summary>The whole file if every piece is here and it is what was offered; otherwise null.</summary>
        public byte[]? Assemble()
        {
            if (!Complete) return null;
            var o = new MemoryStream(Offer.Bytes);
            foreach (var p in _parts) o.Write(p!);
            var all = o.ToArray();
            if (all.Length != Offer.Bytes) return null;
            return Convert.ToHexString(SHA256.HashData(all)).ToLowerInvariant() == Offer.Sha256 ? all : null;
        }
    }
}
