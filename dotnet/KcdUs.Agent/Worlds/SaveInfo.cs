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

    public static SaveInfo? Parse(string text)
    {
        var m = Desc.Match(text);
        if (!m.Success) return null;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return new SaveInfo(int.Parse(m.Groups[1].Value, c), int.Parse(m.Groups[2].Value, c), m.Groups[3].Value, m.Groups[4].Value, m.Groups[5].Value,
            long.Parse(m.Groups[6].Value, c), m.Groups[7].Value, double.Parse(m.Groups[8].Value, c));
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
                if (pos + 8 + rlen > file.Length) return null;
                o.Write(file.Slice(pos + 8, rlen)); pos += 8 + rlen;
            }
            else
            {
                if (clen < 0 || pos + 8 + clen > file.Length) return null;
                try
                {
                    using var z = new ZLibStream(new MemoryStream(file.Slice(pos + 8, clen).ToArray()), CompressionMode.Decompress);
                    z.CopyTo(o);
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
        try { return Read(File.ReadAllBytes(path)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Is this a whole save file: every block inflates, the blocks end exactly where the footer starts, and the footer is the game's. Used on a file that came over the wire.</summary>
    public static bool Validate(ReadOnlySpan<byte> file, out string why)
    {
        why = "";
        if (file.Length < FooterBytes + 8) { why = "too short"; return false; }
        if (Encoding.ASCII.GetString(file.Slice(file.Length - FooterBytes, 4)) != FooterMagic) { why = "no footer"; return false; }
        int pos = 0;
        while (pos + 8 <= file.Length - FooterBytes)
        {
            int clen = BitConverter.ToInt32(file[pos..]), rlen = BitConverter.ToInt32(file[(pos + 4)..]);
            if (rlen < 0 || rlen > 1 << 20) { why = "bad block size"; return false; }
            if (clen == -1) pos += 8 + rlen;
            else
            {
                if (clen < 0 || pos + 8 + clen > file.Length - FooterBytes) { why = "block runs past the end"; return false; }
                try
                {
                    using var z = new ZLibStream(new MemoryStream(file.Slice(pos + 8, clen).ToArray()), CompressionMode.Decompress);
                    var sink = new MemoryStream();
                    z.CopyTo(sink);
                    if (sink.Length != rlen) { why = "block size differs"; return false; }
                }
                catch (InvalidDataException) { why = "a block does not inflate"; return false; }
                pos += 8 + clen;
            }
        }
        if (pos != file.Length - FooterBytes) { why = "blocks do not end at the footer"; return false; }
        return true;
    }
}
