// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdUs.Agent.Worlds;

/// <summary>One DLC a save says it used: the game's own index of it (the number in kcd.log's "DLC list:", for example 9 = Band of Bastards) and its name.</summary>
public sealed record DlcUse(int Id, string Name);

/// <summary>
/// The DLC list in a KCD1 save, and taking entries out of it ("downgrading" a world for a player who does not have that DLC).
///
/// What the engine does (decompiled: FUN_1805b96a4 builds the Load screen's help line, FUN_1805b9828 lists the save's DLC that are not enabled): every save's
/// header block (tag 0x1F5, the first block of the stream) carries a list of the DLC the save used, and the Load screen disables a save whose list names a DLC
/// that is switched off (@ui_load_disabled_by_dlc). The list is the only thing that decides it; the world itself is the same bytes. So a world that was made with a
/// DLC on can be loaded without it once the list no longer names it. What is in the world from that DLC (its items, its quests) is the DLC's own data: the
/// game leaves what it does not know alone (seen: see docs/DLC-DOWNGRADE.md), and the player cannot use it without the DLC.
///
/// Layout after the description text "kind|n|quest|objective|place|unix|date|hours|":  00 00, two int32 (200, 200), one flag byte, a uint16 COUNT, then COUNT entries
/// of (int32 index, zero-terminated name). Checked on real saves with and without a DLC. A header that does not look exactly like that is left alone (null).
/// </summary>
public static partial class ExactTraitsSave
{
    private static readonly Regex HeaderDesc = new(@"(\d+)\|(\d+)\|([^|\0]*)\|([^|\0]*)\|([^|\0]*)\|(\d{9,10})\|([^|\0]*)\|(\d+(?:\.\d+)?)\|", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const int DlcGapBytes = 2 + 8 + 1;   // 00 00, 200, 200, flag

    private sealed record DlcList(int CountAt, int EntriesAt, int EntriesEnd, List<DlcUse> Entries);

    private static DlcList? FindDlcList(byte[] raw, Node header)
    {
        var block = raw.AsSpan(header.Begin, header.Length);
        var m = HeaderDesc.Match(Encoding.Latin1.GetString(block));
        if (!m.Success) return null;
        int at = m.Index + m.Length + DlcGapBytes;            // the uint16 count
        if (at + 2 > block.Length) return null;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(block[at..]);
        if (count > 32) return null;
        int p = at + 2;
        var entries = new List<DlcUse>();
        for (int i = 0; i < count; i++)
        {
            if (p + 5 > block.Length) return null;
            int id = BinaryPrimitives.ReadInt32LittleEndian(block[p..]);
            int nul = block[(p + 4)..].IndexOf((byte)0);
            if (id < 0 || id > 1000 || nul < 1 || nul > 120) return null;
            string name = Encoding.Latin1.GetString(block.Slice(p + 4, nul));
            if (name.Any(c => c < 32 || c > 126)) return null;
            entries.Add(new DlcUse(id, name));
            p += 4 + nul + 1;
        }
        // what follows the list is the engine's own and is not interpreted; it must at least still be inside the block
        return new DlcList(header.Begin + at, header.Begin + at + 2, header.Begin + p, entries);
    }

    private static Node? HeaderNode(byte[] raw)
    {
        var top = Children(raw, 4, raw.Length - 1);
        var h = top.Where(n => n.Tag == 0x1F5).ToArray();
        return h.Length == 1 ? h[0] : null;
    }

    /// <summary>The DLC this save says it used (empty: none); null when the header is not laid out as expected (nothing is claimed then).</summary>
    public static IReadOnlyList<DlcUse>? ReadDlcUse(byte[] file)
    {
        try
        {
            var raw = Inflate(file).Raw;
            return HeaderNode(raw) is { } h && FindDlcList(raw, h) is { } l ? l.Entries : null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException) { return null; }
    }

    /// <summary>
    /// The same save with every DLC entry for which <paramref name="remove"/> is true taken out of its header (the block's length, the checksum and the block framing
    /// are rewritten). Nothing else changes. Returns the file untouched, and <paramref name="removed"/> empty, when there is nothing to take out or the header is
    /// not one this knows how to edit.
    /// </summary>
    public static byte[] StripDlc(byte[] file, Func<DlcUse, bool> remove, out IReadOnlyList<DlcUse> removed)
    {
        removed = Array.Empty<DlcUse>();
        Archive a;
        try { a = Inflate(file); }
        catch (Exception e) when (e is InvalidDataException or IOException) { return file; }
        if (HeaderNode(a.Raw) is not { } header || FindDlcList(a.Raw, header) is not { } list) return file;
        var gone = list.Entries.Where(remove).ToList();
        if (gone.Count == 0) return file;
        // rebuild the entries that stay (their bytes are copied verbatim)
        var kept = new MemoryStream();
        int p = list.EntriesAt;
        foreach (var e in list.Entries)
        {
            int len = 4 + Encoding.Latin1.GetByteCount(e.Name) + 1;
            if (!gone.Contains(e)) kept.Write(a.Raw, p, len);
            p += len;
        }
        var raw = new MemoryStream();
        raw.Write(a.Raw, 0, list.CountAt);
        var count = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(count, (ushort)(list.Entries.Count - gone.Count)); raw.Write(count);
        raw.Write(kept.ToArray());
        raw.Write(a.Raw, list.EntriesEnd, a.Raw.Length - list.EntriesEnd);
        var bytes = raw.ToArray();
        int removedBytes = list.EntriesEnd - list.EntriesAt - (int)kept.Length;
        Put(bytes, header.Off + 2, header.Length - removedBytes);   // the block's own length (a top-level node: nothing else wraps it)
        removed = gone;
        var result = Encode(new(bytes, a.Footer));
        if (ReadDlcUse(result) is not { } check || check.Count != list.Entries.Count - gone.Count) { removed = Array.Empty<DlcUse>(); return file; }   // never hand back what does not read back
        return result;
    }
}
