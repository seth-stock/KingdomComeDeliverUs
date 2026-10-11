// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using KcdUs.Agent;
using Xunit;
namespace KcdUs.Tests;

public class NativeQuestXpLogTests
{
    private static void U32(byte[] b, int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), value);
    private static void U64(byte[] b, int at, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(at), value);
    private static byte[] Record(ulong seq, uint phase, ulong id, byte[]? previous = null)
    {
        var b = new byte[NativeQuestXpLog.RecordSize];
        U32(b, 0, 0x3150584e); U32(b, 4, 1); U32(b, 8, (uint)b.Length); U32(b, 12, phase); U64(b, 16, seq); U64(b, 24, id);
        b[32] = 0x12; b[48] = 0x34; U64(b, 64, 500); U32(b, 80, 1); U32(b, 84, 18); U32(b, 88, 128);
        if (phase == 2) { U64(b, 72, 628); U32(b, 92, 128); U32(b, 96, 1); U32(b, 100, 1); }
        U32(b, 104, 0x21c8fd0); U32(b, 108, 24); b[119] = 5; U32(b, 120, 0x04ea8980); U32(b, 124, 9);
        Encoding.ASCII.GetBytes("world-123").CopyTo(b, 136); U32(b, 200, 3);
        previous?.AsSpan(236, 32).CopyTo(b.AsSpan(204)); Rehash(b); return b;
    }
    private static void Rehash(byte[] b) => SHA256.HashData(b.AsSpan(0, 236)).CopyTo(b, 236);
    private static MemoryStream Stream(params byte[][] records) => new(records.SelectMany(r => r).ToArray());

    [Fact]
    public void The_native_result_keeps_cause_identity_and_actual_readback()
    {
        var intent = Record(1, 1, 1); var result = Record(2, 2, 1, intent); var reader = new NativeQuestXpLog();
        using var stream = Stream(intent, result);
        var xp = Assert.Single(reader.Read(stream));
        Assert.Equal("n12000000000000000000000000000000.1", xp.Key);
        Assert.Equal("world-123", xp.World); Assert.True(xp.Host); Assert.True(xp.Applied); Assert.True(xp.Verified);
        Assert.Equal(128u, xp.Requested); Assert.Equal(128u, xp.Actual); Assert.Equal(18u, xp.Index);
        Assert.Equal("21c8fd0", xp.Cause); Assert.EndsWith("0000000000000000", xp.CauseData);
        Assert.Equal(0, reader.Pending); Assert.Empty(reader.Read(stream));
    }

    [Fact]
    public void An_intent_and_a_partial_result_never_become_an_applied_effect()
    {
        var a = Record(1, 1, 1); var b = Record(2, 2, 1, a); var reader = new NativeQuestXpLog();
        using var stream = new MemoryStream(); stream.Write(a); stream.Write(b, 0, 80);
        Assert.Empty(reader.Read(stream)); Assert.Equal(1, reader.Pending); Assert.Equal(a.Length, reader.Offset);
        stream.Position = stream.Length; stream.Write(b, 80, b.Length - 80);
        Assert.Single(reader.Read(stream)); Assert.Equal(0, reader.Pending);
    }

    [Fact]
    public void Bounded_reads_preserve_intents_across_pages_and_support_reentrancy()
    {
        var a = Record(1, 1, 1); var b = Record(2, 1, 2, a); var c = Record(3, 2, 2, b); var d = Record(4, 2, 1, c);
        using var stream = Stream(a, b, c, d); var reader = new NativeQuestXpLog();
        Assert.Empty(reader.Read(stream, 1)); Assert.Empty(reader.Read(stream, 1)); Assert.Equal(2, reader.Pending);
        Assert.EndsWith(".2", Assert.Single(reader.Read(stream, 1)).Key); Assert.Equal(1, reader.Pending);
        Assert.EndsWith(".1", Assert.Single(reader.Read(stream, 1)).Key); Assert.Equal(0, reader.Pending);
    }

    [Theory]
    [InlineData(0)] [InlineData(12)] [InlineData(16)] [InlineData(32)] [InlineData(48)]
    [InlineData(64)] [InlineData(104)] [InlineData(120)] [InlineData(204)] [InlineData(236)]
    public void Changed_complete_records_are_never_skipped_as_torn_writes(int at)
    {
        var a = Record(1, 1, 1); a[at] ^= 1;
        using var stream = Stream(a); var reader = new NativeQuestXpLog();
        Assert.Throws<InvalidDataException>(() => reader.Read(stream)); Assert.Equal(0, reader.Offset);
    }

    [Theory]
    [InlineData(48)] [InlineData(64)] [InlineData(84)] [InlineData(88)] [InlineData(120)] [InlineData(124)] [InlineData(136)]
    public void A_rehashed_result_with_different_actor_progress_amount_or_cause_is_refused(int at)
    {
        var a = Record(1, 1, 1); var b = Record(2, 2, 1, a); b[at] ^= 1; Rehash(b);
        using var stream = Stream(a, b); var reader = new NativeQuestXpLog();
        Assert.Throws<InvalidDataException>(() => reader.Read(stream)); Assert.Equal(1, reader.Pending);
    }

    [Theory]
    [InlineData(132)] [InlineData(133)] [InlineData(134)] [InlineData(135)] [InlineData(146)]
    public void Padding_is_not_part_of_a_cause_or_world_identity(int at)
    {
        var a = Record(1, 1, 1); a[at] = 1; Rehash(a);
        using var stream = Stream(a);
        Assert.Throws<InvalidDataException>(() => new NativeQuestXpLog().Read(stream));
    }

    [Fact]
    public void Truncation_of_previously_verified_data_is_a_failure()
    {
        var a = Record(1, 1, 1); using var stream = new MemoryStream(); stream.Write(a);
        var reader = new NativeQuestXpLog(); Assert.Empty(reader.Read(stream)); stream.SetLength(0);
        Assert.Throws<InvalidDataException>(() => reader.Read(stream));
    }

    [Fact]
    public void A_result_without_an_intent_or_with_a_different_boot_is_refused()
    {
        using var alone = Stream(Record(1, 2, 1));
        Assert.Throws<InvalidDataException>(() => new NativeQuestXpLog().Read(alone));
        var a = Record(1, 1, 1); var b = Record(2, 2, 1, a); b[32] = 2; Rehash(b);
        using var mixed = Stream(a, b);
        Assert.Throws<InvalidDataException>(() => new NativeQuestXpLog().Read(mixed));
    }
}
