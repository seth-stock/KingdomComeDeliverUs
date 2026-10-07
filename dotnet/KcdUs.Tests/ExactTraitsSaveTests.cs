using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using KcdUs.Agent.Worlds;

namespace KcdUs.Tests;

public class ExactTraitsSaveTests
{
    private static byte[] Node(ushort tag, params byte[][] parts)
    {
        var body = parts.SelectMany(b => b).ToArray(); var result = new byte[body.Length + 6];
        BinaryPrimitives.WriteUInt16LittleEndian(result, tag); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(2), body.Length);
        body.CopyTo(result, 6); return result;
    }
    private static byte[] Xp(params (uint Id, uint Value)[] entries)
    {
        var bytes = new byte[entries.Length * 8 + 4]; int at = 0;
        foreach (var (id, value) in entries)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), id); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at + 4), value); at += 8;
        }
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), -1); return bytes;
    }
    private static byte[] Save(uint xp, uint story, int version = 20)
    {
        byte[] identity = new byte[29]; ExactTraitsSave.Henry.ToByteArray().CopyTo(identity, 0); "Dude\0"u8.CopyTo(identity.AsSpan(16));
        BinaryPrimitives.WriteUInt64LittleEndian(identity.AsSpan(21), 0x7777);
        var core = Node(0x092A, Node(0x1385, Xp((0, xp), (1, xp), (2, xp), (3, xp), (8, story))),
            Node(0x138D, Xp((26, xp))), Node(0x137E, Node(0x03DC, new byte[104]), Node(0x03DA, new byte[8]), Node(0x03DB, new byte[114]), Node(0x03D8, Node(0x1378, BitConverter.GetBytes(99UL)), Node(0x137E, Node(0x1379, new Guid((int)xp, 0, 0, new byte[8]).ToByteArray())))), Node(0x138B, "STATE-PRESERVED"u8.ToArray()));
        var soul = Node(0x115E, ExactTraitsSave.Henry.ToByteArray(), Node(0x1302, identity), Node(0x12FC, core));
        var raw = BitConverter.GetBytes(version).Concat(Node(0x1F5, "HEADER-PRESERVED"u8.ToArray()))
            .Concat(Node(0x1F4, Node(0x1F6, Node(0x7303, "QUEST-PRESERVED"u8.ToArray()), Node(0x7317, Node(0x3529, BitConverter.GetBytes(1), soul))), Node(0xCAFE, "WORLD-PRESERVED"u8.ToArray())))
            .Concat(Node(0x1FD)).Append((byte)0).ToArray();
        using var file = new MemoryStream();
        for (int at = 0; at < raw.Length; at += 32768)
        {
            int count = Math.Min(32768, raw.Length - at); file.Write(BitConverter.GetBytes(-1)); file.Write(BitConverter.GetBytes(count)); file.Write(raw, at, count);
        }
        byte[] footer = new byte[64]; "0XBP"u8.CopyTo(footer); file.Write(footer); var bytes = file.ToArray();
        MD5.HashData(bytes).CopyTo(bytes.AsSpan(bytes.Length - 60)); return bytes;
    }
    private static byte[] Raw(byte[] save)
    {
        using var result = new MemoryStream(); int at = 0;
        while (at < save.Length - 64)
        {
            int packed = BitConverter.ToInt32(save, at), raw = BitConverter.ToInt32(save, at + 4); int size = packed == -1 ? raw : packed;
            if (packed == -1) result.Write(save, at + 8, size);
            else { using var z = new ZLibStream(new MemoryStream(save, at + 8, size), CompressionMode.Decompress); z.CopyTo(result); }
            at += 8 + size;
        }
        return result.ToArray();
    }
    [Fact]
    public void LowersXpExactlyAndReplacesPerksWithoutCopyingWorldStory()
    {
        var source = ExactTraitsSave.Capture(Save(7, 999)); var target = Save(9000, 70);
        var targetHash = SHA256.HashData(target);
        var output = ExactTraitsSave.Prepare(target, source); var actual = ExactTraitsSave.Capture(output);
        Assert.Equal(Xp((0, 7), (1, 7), (2, 7), (3, 7), (8, 70)), actual.Stats);
        Assert.Equal(source.Skills, actual.Skills); Assert.Equal(source.Perks, actual.Perks);
        var raw = System.Text.Encoding.ASCII.GetString(Raw(output));
        foreach (string marker in new[] { "HEADER-PRESERVED", "QUEST-PRESERVED", "WORLD-PRESERVED", "STATE-PRESERVED" }) Assert.Contains(marker, raw);
        Assert.Equal(targetHash, SHA256.HashData(target));
    }
    [Fact]
    public void RepeatPreparationIsByteIdentical()
    {
        var t = ExactTraitsSave.Capture(Save(7, 999)); var first = ExactTraitsSave.Prepare(Save(90, 70), t);
        Assert.Equal(first, ExactTraitsSave.Prepare(first, t));
    }
    [Fact]
    public void RejectsChecksumDamageAndUnknownStreamVersion()
    {
        var damaged = Save(7, 2); damaged[15] ^= 1;
        Assert.Throws<InvalidDataException>(() => ExactTraitsSave.Capture(damaged));
        Assert.Throws<InvalidDataException>(() => ExactTraitsSave.Capture(Save(7, 2, 21)));
    }
    [Fact]
    public void RejectsMalformedAndDuplicateXpAndMalformedPerksBeforeWriting()
    {
        var t = ExactTraitsSave.Capture(Save(7, 2)); var world = Save(9, 3);
        Assert.Throws<InvalidDataException>(() => ExactTraitsSave.Prepare(world, t with { Skills = Xp((26, 1), (26, 2)) }));
        Assert.Throws<InvalidDataException>(() => ExactTraitsSave.Prepare(world, t with { Skills = [0, 1] }));
        Assert.Throws<InvalidDataException>(() => ExactTraitsSave.Prepare(world, t with { Skills = Xp((33, 2)) }));
        Assert.Throws<InvalidDataException>(() => ExactTraitsSave.Prepare(world, t with { Stats = Xp((10, 2)) }));
        Assert.Throws<InvalidDataException>(() => ExactTraitsSave.Prepare(world, t with { Perks = [1, 2, 3] }));
        Assert.Throws<InvalidDataException>(() => ExactTraitsSave.Prepare(world, t with { Perks = Node(0x03D8, new byte[4]) }));
    }
    [Fact]
    public void SparseEarlyCharacterResetsDestinationXpInsteadOfInheritingIt()
    {
        var t = ExactTraitsSave.Capture(Save(7, 2)) with { Stats = Xp(), Skills = Xp() };
        var actual = ExactTraitsSave.Capture(ExactTraitsSave.Prepare(Save(9000, 70), t));
        Assert.Equal(Xp((0, 0), (1, 0), (2, 0), (3, 0), (8, 70)), actual.Stats);
        Assert.Equal(Xp(), actual.Skills);
    }
}
