// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdUs.Agent.Worlds;

/// <summary>
/// What a save says about itself, read from its first block: the description the game keeps at the start of the stream,
/// <c>kind|number|quest|objective|place|unixtime|date|hours|</c> (checked on the real saves: 5.6 MB files of 32 KB zlib blocks and a 64-byte footer that
/// begins "0XBP"). The mod never writes a save: it only reads this line, to tell which of two copies of a world has been played further.
/// </summary>
public sealed record SaveInfo(int Kind, int Number, string Quest, string Objective, string Place, long SavedUnix, string Date, double Hours)
{
    private static readonly Regex Desc = new(@"(\d+)\|(\d+)\|([^|\0]*)\|([^|\0]*)\|([^|\0]*)\|(\d{9,10})\|([^|\0]*)\|(\d+(?:\.\d+)?)\|", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public const int FooterBytes = 64;
    public const string FooterMagic = "0XBP";

    private static byte[] InflateBlock(ReadOnlySpan<byte> bytes, int expected)
    {
        using var z = new ZLibStream(new MemoryStream(bytes.ToArray()), CompressionMode.Decompress);
        byte[] raw = new byte[expected];
        int at = 0;
        while (at < raw.Length)
        {
            int read = z.Read(raw.AsSpan(at));
            if (read == 0) throw new InvalidDataException("Block shorter than declared.");
            at += read;
        }
        if (z.ReadByte() != -1) throw new InvalidDataException("Block longer than declared.");
        return raw;
    }

    public static SaveInfo? Parse(string text)
    {
        var m = Desc.Match(text);
        if (!m.Success) return null;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        if (!int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.None, c, out int kind)
            || !int.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.None, c, out int number)
            || !long.TryParse(m.Groups[6].Value, System.Globalization.NumberStyles.None, c, out long unix)
            || !double.TryParse(m.Groups[8].Value, System.Globalization.NumberStyles.Float, c, out double hours)
            || !double.IsFinite(hours) || hours < 0) return null;
        return new SaveInfo(kind, number, m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value, unix, m.Groups[7].Value, hours);
    }

    /// <summary>The first <paramref name="maxBlocks"/> inflated blocks of a save file; null if the framing is wrong.</summary>
    public static byte[]? Head(ReadOnlySpan<byte> file, int maxBlocks = 2)
    {
        var o = new MemoryStream();
        int pos = 0, n = 0;
        while (pos + 8 <= file.Length - FooterBytes && n < maxBlocks)
        {
            int clen = BitConverter.ToInt32(file[pos..]), rlen = BitConverter.ToInt32(file[(pos + 4)..]);
            if (rlen < 0 || rlen > 1 << 20) return null;
            if (clen == -1)
            {
                if (rlen > file.Length - FooterBytes - pos - 8) return null;
                o.Write(file.Slice(pos + 8, rlen)); pos += 8 + rlen;
            }
            else
            {
                if (clen < 0 || clen > file.Length - FooterBytes - pos - 8) return null;
                try
                {
                    o.Write(InflateBlock(file.Slice(pos + 8, clen), rlen));
                }
                catch (InvalidDataException) { return null; }
                pos += 8 + clen;
            }
            n++;
        }
        return o.ToArray();
    }

    public static SaveInfo? Read(ReadOnlySpan<byte> file)
    {
        var head = Head(file);
        return head is null ? null : Parse(Encoding.Latin1.GetString(head));
    }

    public static SaveInfo? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long length = stream.Length;
            if (length < FooterBytes + 8 || length > WorldTransfer.MaxFileBytes) return null;
            byte[] bytes = new byte[(int)length]; stream.ReadExactly(bytes);
            return Read(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Is this a whole save file: every block inflates, the blocks end exactly where the footer starts, and the footer is the game's. Used on a file that came over the wire.</summary>
    public static bool Validate(ReadOnlySpan<byte> file, out string why)
    {
        why = "";
        if (file.Length < FooterBytes + 8) { why = "too short"; return false; }
        if (file.Length > WorldTransfer.MaxFileBytes) { why = "save too large"; return false; }
        if (Encoding.ASCII.GetString(file.Slice(file.Length - FooterBytes, 4)) != FooterMagic) { why = "no footer"; return false; }
        int pos = 0; long inflatedTotal = 0;
        while (pos + 8 <= file.Length - FooterBytes)
        {
            int clen = BitConverter.ToInt32(file[pos..]), rlen = BitConverter.ToInt32(file[(pos + 4)..]);
            if (rlen < 0 || rlen > 1 << 20) { why = "bad block size"; return false; }
            inflatedTotal += rlen;
            if (inflatedTotal > 256 * 1024 * 1024) { why = "inflated save too large"; return false; }
            if (clen == -1)
            {
                if (rlen > file.Length - FooterBytes - pos - 8) { why = "block runs past the end"; return false; }
                pos += 8 + rlen;
            }
            else
            {
                if (clen < 0 || clen > file.Length - FooterBytes - pos - 8) { why = "block runs past the end"; return false; }
                try
                {
                    InflateBlock(file.Slice(pos + 8, clen), rlen);
                }
                catch (InvalidDataException) { why = "a block does not inflate"; return false; }
                pos += 8 + clen;
            }
        }
        if (pos != file.Length - FooterBytes) { why = "blocks do not end at the footer"; return false; }
        return true;
    }
}
