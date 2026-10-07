// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace KcdUs.Agent.Worlds;

/// <summary>Offline progression preparation for the observed KCD1 stream version 20.
/// Does not install saves or acknowledge engine acceptance. Inventory stays in the destination.</summary>
public static class ExactTraitsSave
{
    public const int MaxRawBytes = 256 * 1024 * 1024;
    public const int MaxFileBytes = 64 * 1024 * 1024;
    private const int Block = 32768;
    public static readonly Guid Henry = new("a075f5f3-014e-4fd8-9b02-2f8e06d3e031");
    private sealed record Node(ushort Tag, int Off, int Length)
    {
        public int Begin => Off + 6;
        public int End => Begin + Length;
    }
    private sealed record Archive(byte[] Raw, byte[] Footer);
    public sealed record Traits(byte[] Stats, byte[] Skills, byte[] Perks);
    private static int Int(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b[at..]);
    private static void Put(Span<byte> b, int at, int n) => BinaryPrimitives.WriteInt32LittleEndian(b[at..], n);

    private static Archive Inflate(byte[] file)
    {
        if (file.Length < 72 || file.Length > MaxFileBytes || !file.AsSpan(file.Length - 64, 4).SequenceEqual("0XBP"u8))
            throw new InvalidDataException("Unsupported KCD1 framing.");
        var masked = file.ToArray(); masked.AsSpan(masked.Length - 60, 16).Clear();
        if (!CryptographicOperations.FixedTimeEquals(MD5.HashData(masked), file.AsSpan(file.Length - 60, 16)))
            throw new InvalidDataException("Save checksum differs.");
        using var output = new MemoryStream(); int at = 0, end = file.Length - 64;
        while (at < end)
        {
            if (end - at < 8) throw new InvalidDataException("Truncated block header.");
            int packed = Int(file, at), length = Int(file, at + 4), size = packed == -1 ? length : packed;
            if (length < 0 || length > Block || size < 0 || size > Block || size > end - at - 8
                || output.Length + length > MaxRawBytes) throw new InvalidDataException("Block bounds differ.");
            if (packed == -1) output.Write(file, at + 8, length);
            else
            {
                using var z = new ZLibStream(new MemoryStream(file, at + 8, size, false), CompressionMode.Decompress);
                var bytes = new byte[length]; z.ReadExactly(bytes);
                if (z.ReadByte() != -1) throw new InvalidDataException("Inflated length differs.");
                output.Write(bytes);
            }
            at += size + 8;
        }
        var raw = output.ToArray();
        if (raw.Length < 11 || Int(raw, 0) != 20) throw new InvalidDataException("Only KCD1 stream v20 is supported.");
        var top = Children(raw, 4, raw.Length - 1);
        if (top.Count == 0 || top[^1].Tag != 0x1FD || top[^1].Length != 0) throw new InvalidDataException("Save end marker differs.");
        // One opaque byte follows the end marker on both observed saves; preserve it verbatim.
        return new(raw, file[^64..]);
    }

    private static List<Node> Children(byte[] b, int start, int end)
    {
        if (start < 0 || end < start || end > b.Length) throw new InvalidDataException("Invalid TLV interval.");
        var nodes = new List<Node>();
        while (start < end)
        {
            if (end - start < 6) throw new InvalidDataException("Truncated TLV.");
            ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(start)); int length = Int(b, start + 2);
            if (length < 0 || length > end - start - 6) throw new InvalidDataException("Invalid TLV length.");
            nodes.Add(new(tag, start, length)); start += length + 6;
            if (nodes.Count > 100_000) throw new InvalidDataException("Excessive field count.");
        }
        return nodes;
    }
    private static Node One(List<Node> nodes, ushort tag)
    {
        var found = nodes.Where(n => n.Tag == tag).ToArray();
        return found.Length == 1 ? found[0] : throw new InvalidDataException($"Expected one field 0x{tag:x4}.");
    }
    private static byte[] Bytes(byte[] b, Node n) => b[n.Begin..n.End];
    private static List<Node> Chain(byte[] b)
    {
        var chain = new List<Node>(); var nodes = Children(b, 4, b.Length - 1);
        foreach (ushort tag in new ushort[] { 0x1F4, 0x1F6, 0x7317, 0x3529 })
        {
            var n = One(nodes, tag); chain.Add(n);
            if (tag != 0x3529) nodes = Children(b, n.Begin, n.End);
        }
        var list = chain[^1]; if (list.Length < 4) throw new InvalidDataException("Missing soul count.");
        var souls = Children(b, list.Begin + 4, list.End);
        if (Int(b, list.Begin) != souls.Count || souls.Any(n => n.Tag != 0x115E || n.Length < 16)) throw new InvalidDataException("Soul count/framing differs.");
        var candidates = souls.Where(n => new Guid(b.AsSpan(n.Begin, 16)) == Henry).ToArray();
        if (candidates.Length != 1) throw new InvalidDataException("Canonical Henry absent or duplicated.");
        var soul = candidates[0]; chain.Add(soul); nodes = Children(b, soul.Begin + 16, soul.End);
        var name = Bytes(b, One(nodes, 0x1302));
        if (name.Length != 29 || !name.AsSpan(16, 5).SequenceEqual("Dude\0"u8)
            || BinaryPrimitives.ReadUInt64LittleEndian(name.AsSpan(21)) != 0x7777) throw new InvalidDataException("Henry identity differs.");
        var state = One(nodes, 0x12FC); chain.Add(state);
        var core = One(Children(b, state.Begin, state.End), 0x092A); chain.Add(core);
        return chain;
    }

    public static Traits Capture(byte[] file)
    {
        var a = Inflate(file); var core = Chain(a.Raw)[^1]; var fields = Children(a.Raw, core.Begin, core.End);
        var t = new Traits(Bytes(a.Raw, One(fields, 0x1385)), Bytes(a.Raw, One(fields, 0x138D)), Bytes(a.Raw, One(fields, 0x137E)));
        Validate(t); return t;
    }
    private static SortedDictionary<uint, uint> Xp(byte[] b)
    {
        if (b.Length < 4 || b.Length > 1028 || b.Length % 8 != 4 || Int(b, b.Length - 4) != -1) throw new InvalidDataException("XP framing differs.");
        var pairs = new SortedDictionary<uint, uint>();
        for (int at = 0; at < b.Length - 4; at += 8)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)), xp = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at + 4));
            if (id > 127 || !pairs.TryAdd(id, xp)) throw new InvalidDataException("Unsupported/duplicate XP key.");
        }
        return pairs;
    }
    private static void Validate(Traits t)
    {
        _ = Xp(t.Stats); _ = Xp(t.Skills);
        if (t.Perks.Length > 256 * 1024) throw new InvalidDataException("Perk state exceeds bounds.");
        var perks = Children(t.Perks, 0, t.Perks.Length);
        if (perks.Count > 2048) throw new InvalidDataException("Excessive perk instance count.");
        foreach (var (tag, length) in new (ushort, int)[] { (0x03DC, 104), (0x03DA, 8), (0x03DB, 114) })
            if (One(perks, tag).Length != length) throw new InvalidDataException("Perk metadata framing differs from the observed build.");
        foreach (var perk in perks)
        {
            if (perk.Tag is 0x03DC or 0x03DA or 0x03DB) continue;
            if (perk.Tag is not (0x03D8 or 0x03D9)) throw new InvalidDataException("Unknown perk instance record.");
            var fields = Children(t.Perks, perk.Begin, perk.End);
            if (One(fields, 0x1378).Length != 8) throw new InvalidDataException("Perk instance identity differs.");
            var state = One(fields, 0x137E);
            var guid = One(Children(t.Perks, state.Begin, state.End), 0x1379);
            if (guid.Length != 16 || new Guid(t.Perks.AsSpan(guid.Begin, 16)) == Guid.Empty)
                throw new InvalidDataException("Perk class identity differs.");
        }
    }
    private static byte[] WorldStats(byte[] source, byte[] target)
    {
        var s = Xp(source); var d = Xp(target); s.Remove(8);
        // The game's arrays are sparse; an omitted XP entry means no earned XP.
        foreach (uint id in new uint[] { 0, 1, 2, 3 }) s.TryAdd(id, 0U);
        if (d.TryGetValue(8, out uint story)) s[8] = story;
        var result = new byte[s.Count * 8 + 4]; int at = 0;
        foreach (var (id, xp) in s)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at), id);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(at + 4), xp); at += 8;
        }
        Put(result, at, -1); return result;
    }
    private static byte[] Encode(Archive a)
    {
        using var output = new MemoryStream(); var header = new byte[8];
        for (int at = 0; at < a.Raw.Length; at += Block)
        {
            int length = Math.Min(Block, a.Raw.Length - at); using var packed = new MemoryStream();
            using (var z = new ZLibStream(packed, CompressionLevel.Optimal, true)) z.Write(a.Raw, at, length);
            var part = packed.ToArray(); bool store = part.Length >= length;
            Put(header, 0, store ? -1 : part.Length); Put(header, 4, length); output.Write(header);
            if (store) output.Write(a.Raw, at, length); else output.Write(part);
        }
        var footer = a.Footer.ToArray(); footer.AsSpan(4, 16).Clear(); output.Write(footer);
        var file = output.ToArray(); MD5.HashData(file).CopyTo(file.AsSpan(file.Length - 60, 16));
        if (file.Length > MaxFileBytes) throw new InvalidDataException("Prepared save exceeds bounds.");
        return file;
    }

    public static byte[] Prepare(byte[] destination, Traits t)
    {
        Validate(t); var a = Inflate(destination); var chain = Chain(a.Raw); var core = chain[^1];
        var fields = Children(a.Raw, core.Begin, core.End);
        var replacements = new Dictionary<ushort, byte[]> { [0x1385] = WorldStats(t.Stats, Bytes(a.Raw, One(fields, 0x1385))), [0x138D] = t.Skills, [0x137E] = t.Perks };
        foreach (ushort tag in replacements.Keys) _ = One(fields, tag);
        using var payload = new MemoryStream();
        foreach (var f in fields)
        {
            if (!replacements.TryGetValue(f.Tag, out var value)) payload.Write(a.Raw, f.Off, f.Length + 6);
            else
            {
                var h = new byte[6]; BinaryPrimitives.WriteUInt16LittleEndian(h, f.Tag); Put(h, 2, value.Length);
                payload.Write(h); payload.Write(value);
            }
        }
        var bytes = payload.ToArray(); int delta = bytes.Length - core.Length, length = checked(a.Raw.Length + delta);
        if (length > MaxRawBytes) throw new InvalidDataException("Prepared stream exceeds bounds.");
        var raw = new byte[length]; a.Raw.AsSpan(0, core.Begin).CopyTo(raw); bytes.CopyTo(raw.AsSpan(core.Begin));
        a.Raw.AsSpan(core.End).CopyTo(raw.AsSpan(core.Begin + bytes.Length));
        foreach (var n in chain) Put(raw, n.Off + 2, checked(n.Length + delta));
        var result = Encode(new(raw, a.Footer)); var readback = Capture(result);
        if (!readback.Stats.SequenceEqual(replacements[0x1385]) || !readback.Skills.SequenceEqual(t.Skills) || !readback.Perks.SequenceEqual(t.Perks)) throw new InvalidDataException("Prepared traits readback differs.");
        return result;
    }
}
