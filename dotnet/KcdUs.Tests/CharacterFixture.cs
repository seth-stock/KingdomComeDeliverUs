// SPDX-License-Identifier: GPL-3.0-only
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using KcdUs.Agent.Worlds;

namespace KcdUs.Tests;

internal static class CharacterFixture
{
    private static byte[] Join(params byte[][] pieces) => pieces.SelectMany(p => p).ToArray();
    private static byte[] T(ushort tag, params byte[][] pieces)
    { var b = Join(pieces); return Join(BitConverter.GetBytes(tag), BitConverter.GetBytes(b.Length), b); }
    private static byte[] R(byte kind, Guid id) => Join(new byte[] { 1, kind }, id.ToByteArray());
    private static byte[] X(uint id, uint value) => Join(BitConverter.GetBytes(id), BitConverter.GetBytes(value), BitConverter.GetBytes(-1));
    public static byte[] Save(long unix, double hours, uint xp = 7, int amount = 1)
    {
        var hero = ExactTraitsSave.Henry;
        var identity = Join(hero.ToByteArray(), "Dude\0"u8.ToArray(), BitConverter.GetBytes(0x7777UL));
        var perk = T(0x137E, T(0x03DC, new byte[104]), T(0x03DA, new byte[8]), T(0x03DB, new byte[114]));
        var resources = Join(new float[] { 73, 115, 64, 82, 0, 0 }.SelectMany(BitConverter.GetBytes).ToArray());
        var core = T(0x092A, T(0x1385, X(0, xp)), T(0x138D, X(26, xp)), perk,
            T(0x138B, resources), T(0x137D, T(0x04B0, new byte[31])), T(0x137F, new byte[4]), T(0x138F, new byte[4]));
        var soul = T(0x115E, hero.ToByteArray(), T(0x1302, identity), T(0x12FC, core, T(0x0934, "WORLD-KNOWLEDGE"u8.ToArray())));
        var item = new byte[74]; new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee").ToByteArray().CopyTo(item, 0);
        BitConverter.GetBytes(amount).CopyTo(item, 16); BitConverter.GetBytes(0.75f).CopyTo(item, 20);
        BitConverter.GetBytes(1).CopyTo(item, 24); R(3, hero).CopyTo(item, 56);
        var instance = new Guid((int)xp, 0x1234, 0x5678, new byte[8]);
        var inv = Join(hero.ToByteArray(), hero.ToByteArray(), R(5, hero), R(5, hero), new byte[25], "Dude\0"u8.ToArray(),
            BitConverter.GetBytes(1), T(0x030A, instance.ToByteArray(), T(0x0630, item)));
        var raw = Join(BitConverter.GetBytes(20), T(0x1F5, Encoding.Latin1.GetBytes(WorldTests.Desc(unix, hours) + "\0")),
            T(0x1F4, T(0x1F6, T(0x7317, T(0x3529, BitConverter.GetBytes(1), soul)), T(0x7303, T(0x0BBA, BitConverter.GetBytes(1), T(0x1E66, inv))))),
            T(0x1FD), new byte[1]);
        return Encode(raw);
    }
    public static byte[] WithDescription(byte[] save, long unix, double hours)
    {
        using var input = new ZLibStream(new MemoryStream(save, 8, BitConverter.ToInt32(save)), CompressionMode.Decompress);
        using var expanded = new MemoryStream(); input.CopyTo(expanded); var raw = expanded.ToArray();
        int old = BitConverter.ToInt32(raw, 6);
        var result = Join(raw[..4], T(0x1F5, Encoding.Latin1.GetBytes(WorldTests.Desc(unix, hours) + "\0")), raw[(10 + old)..]);
        return Encode(result);
    }
    private static byte[] Encode(byte[] raw)
    {
        using var packed = new MemoryStream(); using (var z = new ZLibStream(packed, CompressionLevel.Optimal, true)) z.Write(raw);
        var p = packed.ToArray(); var footer = new byte[64]; "0XBP"u8.CopyTo(footer);
        var file = Join(BitConverter.GetBytes(p.Length), BitConverter.GetBytes(raw.Length), p, footer);
        MD5.HashData(file).CopyTo(file, file.Length - 60); return file;
    }
}
