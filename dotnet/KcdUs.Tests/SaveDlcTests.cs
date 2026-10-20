// SPDX-License-Identifier: GPL-3.0-only
// A world made with a DLC can be loaded without it once the save's header no longer names the DLC (docs/DLC-DOWNGRADE.md).
using KcdUs.Agent.Worlds;

namespace KcdUs.Tests;

public class SaveDlcTests
{
    private static byte[] Save(params (int, string)[] dlc) => CharacterFixture.WithDlc(CharacterFixture.Save(1_790_000_000, 3), dlc);

    [Fact]
    public void The_dlc_a_save_used_is_read_from_its_header()
    {
        var list = ExactTraitsSave.ReadDlcUse(Save((9, "Band of Bastards"), (4, "From the Ashes")))!;
        Assert.Equal(new[] { new DlcUse(9, "Band of Bastards"), new DlcUse(4, "From the Ashes") }, list);
        Assert.Empty(ExactTraitsSave.ReadDlcUse(Save())!);
    }

    [Fact]
    public void A_header_that_is_not_laid_out_as_the_game_writes_it_is_not_claimed_or_touched()
    {
        var plain = CharacterFixture.Save(1_790_000_000, 3);      // the fixture's own minimal header: description only
        Assert.Null(ExactTraitsSave.ReadDlcUse(plain));
        Assert.Same(plain, ExactTraitsSave.StripDlc(plain, _ => true, out var removed));
        Assert.Empty(removed);
        Assert.Null(ExactTraitsSave.ReadDlcUse(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void Stripping_takes_the_chosen_dlc_out_and_leaves_the_rest_of_the_world_byte_for_byte()
    {
        var file = Save((9, "Band of Bastards"), (4, "From the Ashes"));
        var result = ExactTraitsSave.StripDlc(file, d => d.Id == 9, out var removed);
        Assert.Equal(new[] { new DlcUse(9, "Band of Bastards") }, removed);
        Assert.Equal(new[] { new DlcUse(4, "From the Ashes") }, ExactTraitsSave.ReadDlcUse(result)!);
        Assert.True(SaveInfo.Validate(result, out var why), why);            // still a whole, checksummed save
        Assert.Equal(SaveInfo.Read(file)!.Hours, SaveInfo.Read(result)!.Hours);   // the description is intact
        // everything but the header block is the same stream: the character is still the character
        Assert.True(ExactTraitsSave.SameProgressionAndInventory(ExactTraitsSave.CaptureCharacter(file), ExactTraitsSave.CaptureCharacter(result)));
        var none = ExactTraitsSave.StripDlc(result, d => true, out var all);
        Assert.Single(all);
        Assert.Empty(ExactTraitsSave.ReadDlcUse(none)!);
        Assert.True(SaveInfo.Validate(none, out why), why);
    }

    [Fact]
    public void Nothing_to_strip_returns_the_same_file()
    {
        var file = Save((9, "Band of Bastards"));
        Assert.Same(file, ExactTraitsSave.StripDlc(file, d => d.Id == 77, out var removed));
        Assert.Empty(removed);
    }

    [Fact]
    public void The_dlc_log_gives_the_games_index_of_each_dlc()
    {
        var lines = new[]
        {
            "DLC list:",
            "[1 TreasuresOfThePast] 768530, Kingdom Come: Deliverance - Treasures of the Past, active: N, save: Y",
            "ERROR: operation 'Do connect' takes too much time.",
            "[9 ExpeditionaryRides] 977420, Kingdom Come: Deliverance – Band of Bastards, active: Y, save: Y",
            "Running machine spec auto detect (64 bit)...",
        };
        var idx = Coop.Contract.DlcLog.Indexes(lines);
        Assert.Equal(9, idx["ExpeditionaryRides"]);
        Assert.Equal(1, idx["TreasuresOfThePast"]);
    }
}
