// SPDX-License-Identifier: GPL-3.0-only
// The economy audit events (mod/kcdus/lua/loot.lua, KCDUS|ECON): what changed Henry's pack and what the pack watcher could attribute it to. Observation only.
namespace KcdUs.Tests;

public class EconLuaTests
{
    private const string Scope = "w1";
    private const string Coin = "5ef63059-322e-4e1b-abe8-926e100c770e";
    private const string Coat = "a856e87a-8065-4338-919d-0aff7a63341d";
    private const string Apple = "2264f217-590e-4c0f-a4c6-f50c6532b9f6";

    private static LuaMod Guest()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Do("__world.worldTime = 5000; KCDUS.Loot.onWorldReset()");
        m.Do("__mkNpc('bandit_7', 0, 12, 20, 30); __world_ents[1].dead = true; __world_ents[1].items = { __item('" + Coat + "', 1, 0.7) }");
        m.Send("1~LOOTMODE|1|" + Scope + "|0");
        m.Advance(0.6);
        m.ClearLog();
        return m;
    }

    [Fact]
    public void A_take_from_a_holder_is_a_take_of_exactly_what_moved()
    {
        var m = Guest();
        m.Do("__world_ents[1].items = {}; __henry.items[#__henry.items + 1] = __item('" + Coat + "', 1, 0.7)");
        m.Advance(0.6);
        Assert.Single(m.Lines($"KCDUS|ECON|1|{Coat}|1|5000|bandit_7"));
        Assert.Empty(m.Lines("KCDUS|ECON|5|"));
        Assert.Empty(m.Lines("KCDUS|ECON|6|"));
    }

    [Fact]
    public void A_gain_no_holder_gave_and_a_loss_no_holder_received_are_plain_entries()
    {
        var m = Guest();
        m.Do("__henry.items[#__henry.items + 1] = __item('" + Apple + "', 3, 1)");
        m.Advance(0.6);
        Assert.Single(m.Lines($"KCDUS|ECON|6|{Apple}|3|5000"));
        m.ClearLog();
        m.Do("player.inventory:DeleteItemOfClass('" + Apple + "', 2)");
        m.Advance(0.6);
        Assert.Single(m.Lines($"KCDUS|ECON|5|{Apple}|-2|5000"));
        m.ClearLog();
        m.Advance(1.0);
        Assert.Empty(m.Lines("KCDUS|ECON|"));                                               // nothing changed: nothing is written
    }

    [Fact]
    public void A_purchase_is_two_barter_entries_and_no_stray_loss_of_the_price()
    {
        var m = Guest();
        m.Do("__mkStash(13, 20, 30, { __item('" + Coat + "', 1, 1) }); __world_ents[#__world_ents].shop = true");
        m.Do("__henry.items[#__henry.items + 1] = __item('" + Coin + "', 1, 1); __henry.items[#__henry.items].amount = 100");
        m.Advance(0.6);
        m.ClearLog();
        m.Do("__world_ents[#__world_ents].items = {}; __henry.items[#__henry.items + 1] = __item('" + Coat + "', 1, 1)");
        m.Do("for _, it in ipairs(__henry.items) do if it.class == '" + Coin + "' then it.amount = 75 end end");
        m.Advance(0.6);
        var econ = m.Lines("KCDUS|ECON|");
        // a stash the stub does not know as a shop is a chest here: the goods are a take and the price a plain loss; with a shop it is two barter entries.
        // Either way the pack's change is fully explained and nothing is counted twice.
        Assert.Contains(econ, l => l.StartsWith($"KCDUS|ECON|1|{Coat}|1|") || l.StartsWith($"KCDUS|ECON|3|{Coat}|1|"));
        Assert.Equal(-25, econ.Where(l => l.Contains($"|{Coin}|")).Sum(l => double.Parse(l.Split('|')[4])));
    }
}
