// SPDX-License-Identifier: GPL-3.0-only
// Arbitrary character restoration, the live soul state (CharacterSave.cs): every buff instance (injuries, poison, drink, hunger, sleep, perk effects) and the
// personal accumulators move with Henry; the world's live state stays. Record framing as observed in a real private-engine save (2026-10-11).
using KcdUs.Agent.Worlds;

namespace KcdUs.Tests;

public class CharacterLiveStateTests
{
    private static readonly Guid Hangover = new("7e252c71-5e41-472c-ad32-f223a664faab");
    private static readonly Guid Injury = new("34f0885b-7287-4881-907f-f19751a5e831");
    private static readonly Guid Poison = new("64dde7b9-fbef-40f9-bf7d-ee8e07449ee7");

    private static byte[] Source() => CharacterFixture.Save(1_790_000_100, 2, 7, 1,
        CharacterFixture.Buffs(CharacterFixture.Buff(Guid.NewGuid(), Hangover, 10.25), CharacterFixture.Buff(Guid.NewGuid(), Injury, 153.4), CharacterFixture.Buff(Guid.NewGuid(), Poison, 0.5)),
        CharacterFixture.Accumulators(3227.25, 54302.06, 38514.6, 175458.49, 14.6, 43.92, 458.49, 43.92));

    private static byte[] World() => CharacterFixture.Save(1_790_000_200, 10, 9000, 22,
        CharacterFixture.Buffs(CharacterFixture.Buff(Guid.NewGuid(), Poison, 99)), CharacterFixture.Accumulators(5, 6));

    [Fact]
    public void Every_buff_and_the_personal_accumulators_move_with_henry_and_the_worlds_live_state_stays()
    {
        var source = ExactTraitsSave.CaptureCharacter(Source());
        Assert.NotNull(source.Live);
        Assert.Equal(new[] { Hangover, Injury, Poison }, ExactTraitsSave.BuffDefinitions(source.Live![ExactTraitsSave.LiveBuffs]));
        var world = World();
        var prepared = ExactTraitsSave.PrepareCharacter(world, source);
        var after = ExactTraitsSave.CaptureCharacter(prepared);
        Assert.Equal(source.Live[ExactTraitsSave.LiveBuffs], after.Live![ExactTraitsSave.LiveBuffs]);           // exact records: timers as elapsed time
        Assert.Equal(source.Live[ExactTraitsSave.LiveAccumulators], after.Live[ExactTraitsSave.LiveAccumulators]);
        Assert.Contains("WORLD-KNOWLEDGE"u8.ToArray(), new[] { Inflated(prepared) }, new SubsequenceComparer());  // 0x0934 is the world's
        Assert.Contains("WORLD-SLOTS"u8.ToArray(), new[] { Inflated(prepared) }, new SubsequenceComparer());      // 0x0936 is the world's
        Assert.True(ExactTraitsSave.SameProgressionAndInventory(source, after));
    }

    [Fact]
    public void A_character_without_live_fields_leaves_the_destinations_live_state_alone()
    {
        var plain = ExactTraitsSave.CaptureCharacter(CharacterFixture.Save(1_790_000_100, 2));
        Assert.Null(plain.Live);
        var world = World();
        var before = ExactTraitsSave.CaptureCharacter(world).Live!;
        var after = ExactTraitsSave.CaptureCharacter(ExactTraitsSave.PrepareCharacter(world, plain)).Live!;
        Assert.Equal(before[ExactTraitsSave.LiveBuffs], after[ExactTraitsSave.LiveBuffs]);
    }

    public static IEnumerable<object[]> Broken()
    {
        var id = Guid.NewGuid();
        yield return new object[] { CharacterFixture.Buffs(CharacterFixture.Buff(id, Hangover, 1), CharacterFixture.Buff(id, Injury, 1)), "duplicated" };
        yield return new object[] { CharacterFixture.Buffs(CharacterFixture.Buff(Guid.NewGuid(), Hangover, double.NaN)), "elapsed" };
        yield return new object[] { CharacterFixture.Buffs(CharacterFixture.Buff(Guid.NewGuid(), Hangover, -5)), "elapsed" };
        yield return new object[] { CharacterFixture.Buffs(CharacterFixture.Buff(Guid.NewGuid(), Guid.Empty, 1)), "definition" };
        var miscounted = CharacterFixture.Buffs(CharacterFixture.Buff(Guid.NewGuid(), Hangover, 1)); BitConverter.GetBytes(2).CopyTo(miscounted, 0);
        yield return new object[] { miscounted, "count" };
    }

    [Theory]
    [MemberData(nameof(Broken))]
    public void A_damaged_or_unknown_buff_list_is_refused_before_it_can_enter_a_world(byte[] buffs, string why)
    {
        var e = Assert.Throws<InvalidDataException>(() => ExactTraitsSave.CaptureCharacter(CharacterFixture.Save(1_790_000_100, 2, 7, 1, buffs)));
        Assert.Contains(why, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accumulators_must_be_the_eight_known_finite_counters()
    {
        Assert.Throws<InvalidDataException>(() => ExactTraitsSave.CaptureCharacter(CharacterFixture.Save(1_790_000_100, 2, 7, 1,
            CharacterFixture.Buffs(), CharacterFixture.Accumulators(1, double.PositiveInfinity))));
    }

    private static byte[] Inflated(byte[] file)
    {
        using var input = new System.IO.Compression.ZLibStream(new MemoryStream(file, 8, BitConverter.ToInt32(file)), System.IO.Compression.CompressionMode.Decompress);
        using var o = new MemoryStream(); input.CopyTo(o); return o.ToArray();
    }

    private sealed class SubsequenceComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y) => x != null && y != null && (x.AsSpan().IndexOf(y) >= 0 || y.AsSpan().IndexOf(x) >= 0);
        public int GetHashCode(byte[] b) => 0;
    }
}
