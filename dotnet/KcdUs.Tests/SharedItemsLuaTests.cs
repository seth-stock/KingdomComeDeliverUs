// SPDX-License-Identifier: GPL-3.0-only
// Shared items beyond bodies and chests (mod/kcdus/lua/loot.lua): things on the ground, saddlebags, shops' stock, puts and drops. The engine facts (a
// PickableItem's item record, spawning one by class, RemoveEntity, shop stock in linked stashes, CreateItem/AddItem) were read in the private engine.
namespace KcdUs.Tests;

public class SharedItemsLuaTests
{
    private const string Scope = "w1";
    private const string Apple = "11111111-2222-3333-4444-555555555555";
    private const string Coin = "5ef63059-322e-4e1b-abe8-926e100c770e";
    private const string Coat = "a856e87a-8065-4338-919d-0aff7a63341d";

    private static LuaMod World(bool host)
    {
        var m = new LuaMod();
        m.InWorld();
        m.Do("__mkGround('" + Apple + "', 3, 12, 20, 30)");                                       // 2 m from the player (10,20,30)
        m.Do("__mkNpc('horse_pebbles', 100, 13, 20, 30, 'Horse'); __world_ents[2].items = { __item('" + Coat + "', 1, 0.8) }");
        m.Do("local s = __mkStash(30, 20, 30, { __item('" + Coat + "', 4, 1) }); s.shopLink = 'wuid-shop-1'");   // a shop's stock, 20 m away
        m.Do("__henry.items[#__henry.items + 1] = __item('" + Coin + "', 500)");
        m.Send("1~LOOTMODE|1|" + Scope + "|" + (host ? 1 : 0));
        m.Advance(0.6);
        m.ClearLog();
        return m;
    }

    private static double HenryHas(LuaMod m, string cls) =>
        m.Num("(function() local n = 0 for _, it in ipairs(__henry.items) do if it.class == '" + cls + "' then n = n + it.amount end end return n end)()");
    private static void Give(LuaMod m, string cls, int n) => m.Do("__henry.items[#__henry.items + 1] = __item('" + cls + "', " + n + ")");
    private static void Spend(LuaMod m, string cls, int n) => m.Do("player.inventory:DeleteItemOfClass('" + cls + "', " + n + ")");

    [Fact]
    public void A_guest_picking_something_up_asks_the_host_and_a_gone_answer_takes_it_back()
    {
        var m = World(host: false);
        m.Do("table.remove(__world_ents, 1)");                                                    // the thing left the ground ...
        Give(m, Apple, 3);                                                                         // ... and came into Henry's pack
        m.Advance(0.6);
        var ask = Assert.Single(m.Lines("KCDUS|LOOT|ask|"));
        var f = ask.Split('|');
        Assert.Equal("g@120_200_300", f[5]);
        Assert.Equal(Apple, f[6]);
        Assert.Equal("3", f[7]);
        m.Send($"2~LOOTRES|{Scope}|{f[4]}|gone|g@120_200_300|{Apple}|3");
        Assert.Equal(0, HenryHas(m, Apple));
    }

    [Fact]
    public void Something_that_rolls_out_of_sight_without_reaching_the_pack_is_not_a_pick_up()
    {
        var m = World(host: false);
        m.Do("table.remove(__world_ents, 1)");                                                    // an NPC took it, or a friend's take applied here
        m.Advance(0.6);
        Assert.Empty(m.Lines("KCDUS|LOOT|ask|"));
    }

    [Fact]
    public void The_host_decides_a_guests_ground_ask_by_removing_its_own_copy_and_a_second_ask_is_gone()
    {
        var m = World(host: true);
        m.Send($"2~LOOTASK|{Scope}|2|abcdef12|1|g@120_200_300|{Apple}|3");
        Assert.Single(m.Lines($"KCDUS|LOOT|res|2|1|ok|g@120_200_300|{Apple}|3"));
        Assert.Single(m.Lines($"KCDUS|LOOT|took|g@120_200_300|{Apple}|3"));
        Assert.Equal(0, m.Num("(function() local n=0 for _,e in ipairs(__world_ents) do if e.class=='PickableItem' then n=n+1 end end return n end)()"));
        m.Advance(0.6);
        Assert.Empty(m.Lines("KCDUS|LOOT|took|g@120_200_300|" + Apple + "|3|1.0000|"));       // its own watcher does not take it a second time
        m.Send($"3~LOOTASK|{Scope}|3|abcdef12|1|g@120_200_300|{Apple}|3");
        Assert.Single(m.Lines($"KCDUS|LOOT|res|3|1|none|g@120_200_300|{Apple}|3"));
    }

    [Fact]
    public void A_host_pick_up_is_told_to_the_guests_and_removes_their_copy()
    {
        var host = World(host: true);
        host.Do("table.remove(__world_ents, 1)");
        Give(host, Apple, 3);
        host.Advance(0.6);
        Assert.Single(host.Lines($"KCDUS|LOOT|took|g@120_200_300|{Apple}|3"));
        var guest = World(host: false);
        guest.Send($"2~LOOTTOOK|{Scope}|g@120_200_300|{Apple}|3");
        Assert.Single(guest.Lines($"KCDUS|LOOTAPPLIED|g@120_200_300|{Apple}|3|applied"));
    }

    [Fact]
    public void A_drop_at_henrys_feet_is_told_and_a_friends_drop_appears_on_this_ground_without_being_reported_back()
    {
        var m = World(host: false);
        Spend(m, Coin, 50);
        m.Do("__mkGround('" + Coin + "', 50, 11, 20, 30)");
        m.Advance(0.6);
        Assert.Single(m.Lines($"KCDUS|LOOT|drop|g@110_200_300|{Coin}|50|"));

        var friend = World(host: true);
        friend.Send($"2~LOOTDROP|{Scope}|g@110_200_300|{Coin}|50|1.0000");
        Assert.Single(friend.Lines($"KCDUS|LOOTAPPLIED|g@110_200_300|{Coin}|50|dropped"));
        friend.Advance(1.2);
        Assert.Empty(friend.Lines("KCDUS|LOOT|drop|"));
        Assert.Empty(friend.Lines("KCDUS|LOOT|took|"));
    }

    [Fact]
    public void Saddlebags_share_takes_and_puts()
    {
        var m = World(host: false);
        m.Do("__world_ents[2].items = {}");                                                       // the coat out of the saddlebag ...
        Give(m, Coat, 1);                                                                          // ... into Henry's pack
        m.Advance(0.6);
        Assert.Single(m.Lines($"KCDUS|LOOT|ask|"));
        Assert.Contains("|horse_pebbles|" + Coat + "|1|", m.Lines("KCDUS|LOOT|ask|")[0]);

        m.ClearLog();
        Spend(m, Coin, 100);                                                                       // a put: out of the pack, into the saddlebag
        m.Do("local h = __world_ents[2]; h.items[#h.items + 1] = __item('" + Coin + "', 100)");
        m.Advance(0.6);
        Assert.Single(m.Lines($"KCDUS|LOOT|put|horse_pebbles|{Coin}|100|"));

        var friend = World(host: true);
        friend.Send($"2~LOOTPUT|{Scope}|horse_pebbles|{Coin}|100|1.0000");
        Assert.Equal(100, friend.Num("(function() local n=0 for _,it in ipairs(__world_ents[2].items) do if it.class=='" + Coin + "' then n=n+it.amount end end return n end)()"));
        friend.Advance(0.6);
        Assert.Empty(friend.Lines("KCDUS|LOOT|put|"));                                           // the friend's put applied here is not this player's
    }

    [Fact]
    public void Buying_from_a_shop_is_an_ask_from_twenty_metres_and_gone_gives_the_money_back()
    {
        var m = World(host: false);
        m.Do("__world_ents[3].items[1].amount = 3");                                             // one coat sold off the shop's stock ...
        Give(m, Coat, 1);                                                                          // ... into Henry's pack ...
        Spend(m, Coin, 120);                                                                       // ... for 120
        m.Advance(0.6);
        var ask = Assert.Single(m.Lines("KCDUS|LOOT|ask|"));
        Assert.Contains("|s@300_200_300|" + Coat + "|1|", ask);
        var tok = ask.Split('|')[4];
        Assert.Equal(380, HenryHas(m, Coin));
        m.Send($"2~LOOTRES|{Scope}|{tok}|gone|s@300_200_300|{Coat}|1");
        Assert.Equal(0, HenryHas(m, Coat));
        Assert.Equal(500, HenryHas(m, Coin));                                                     // the price is given back
        m.ClearLog(); m.Advance(1.2);
        Assert.Empty(m.Lines("KCDUS|LOOT|"));                                                     // the refund is not mistaken for anything
    }

    [Fact]
    public void Selling_to_a_shop_is_a_put_into_its_stock_and_a_restock_is_nobodys()
    {
        var m = World(host: true);
        Give(m, Apple, 2);
        m.Advance(0.6);
        m.ClearLog();
        Spend(m, Apple, 2);
        m.Do("local s = __world_ents[3]; s.items[#s.items + 1] = __item('" + Apple + "', 2)");
        m.Advance(0.6);
        Assert.Single(m.Lines($"KCDUS|LOOT|put|s@300_200_300|{Apple}|2|"));
        m.ClearLog();
        m.Do("local s = __world_ents[3]; s.items[#s.items + 1] = __item('" + Coat + "', 5)");   // the shop restocks: Henry's pack did not change
        m.Do("__world_ents[3].items[1].amount = 1");
        m.Advance(0.6);
        Assert.Empty(m.Lines("KCDUS|LOOT|"));
    }

    [Fact]
    public void Things_only_npcs_use_and_shop_display_pieces_are_never_watched()
    {
        var m = World(host: false);
        m.Do("__world_ents[1].npcOnly = true");
        m.Do("local g = __mkGround('" + Coat + "', 1, 11, 21, 30); g.shopItem = true");
        m.Advance(0.6);
        m.Do("table.remove(__world_ents, 1)");
        Give(m, Apple, 3);
        m.Advance(0.6);
        Assert.Empty(m.Lines("KCDUS|LOOT|ask|"));
    }

    [Fact]
    public void Two_things_dropped_on_one_spot_are_two_drops_and_two_pick_ups()
    {
        var m = World(host: false);
        Spend(m, Coin, 10);
        m.Do("__mkGround('" + Coin + "', 10, 11, 20, 30)");
        m.Advance(0.6);
        Spend(m, Coin, 20);
        m.Do("__mkGround('" + Coin + "', 20, 11, 20, 30)");
        m.Advance(0.6);
        Assert.Equal(2, m.Lines("KCDUS|LOOT|drop|g@110_200_300|" + Coin + "|").Count);
        Assert.Empty(m.Lines("KCDUS|LOOT|put|"));
        m.Do("for i = #__world_ents, 1, -1 do if __world_ents[i].class == 'PickableItem' and __world_ents[i].rec.class == '" + Coin + "' then table.remove(__world_ents, i) end end");
        Give(m, Coin, 30);
        m.Advance(0.6);
        Assert.Equal(2, m.Lines("KCDUS|LOOT|ask|").Count(l => l.Contains("|g@110_200_300|" + Coin + "|")));
    }
}
