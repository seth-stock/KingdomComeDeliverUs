// SPDX-License-Identifier: GPL-3.0-only
// Durable loot custody, the game's half (mod/kcdus/lua/loot.lua "Recovery"): asks carry the take's world time and price, receipts carry the load's nonce and
// token, and LOOTRECOVER settles a take of an earlier load by the world time the save was loaded at and the host's durable answer. Never by a guess.
namespace KcdUs.Tests;

public class LootRecoveryLuaTests
{
    private const string Scope = "w1";
    private const string Coin = "5ef63059-322e-4e1b-abe8-926e100c770e";
    private const string Coat = "a856e87a-8065-4338-919d-0aff7a63341d";
    private const string Op = "0123456789abcdef0123456789abcdef";

    private static LuaMod Guest(double loadWt = 5000)
    {
        var m = new LuaMod();
        m.InWorld();
        m.Do("__world.worldTime = " + loadWt.ToString(System.Globalization.CultureInfo.InvariantCulture));
        m.Do("KCDUS.Loot.onWorldReset()");                                                 // a loaded save: its world time is what it can contain
        m.Do("__mkNpc('bandit_7', 0, 12, 20, 30); __world_ents[1].dead = true; __world_ents[1].items = { __item('" + Coat + "', 1, 0.7) }");
        m.Send("1~LOOTMODE|1|" + Scope + "|0");
        m.Advance(0.6);
        m.ClearLog();
        return m;
    }

    private static double HenryHas(LuaMod m, string cls) =>
        m.Num("(function() local n = 0 for _, it in ipairs(__henry.items) do if it.class == '" + cls + "' then n = n + it.amount end end return n end)()");

    [Fact]
    public void The_loot_mode_readback_names_this_loads_nonce_and_world_time()
    {
        var m = Guest(5000);
        m.Send("2~LOOTMODE|1|" + Scope + "|0");
        var line = Assert.Single(m.Lines("KCDUS|LOOTMODE|on|"));
        var f = line.Split('|');
        Assert.Equal(7, f.Length);
        Assert.Matches("^[0-9a-f]{8}$", f[5]);
        Assert.Equal("5000", f[6]);
    }

    [Fact]
    public void An_ask_carries_the_takes_world_time_and_its_receipt_the_nonce_and_token()
    {
        var m = Guest(5000);
        m.Do("__world.worldTime = 6200");
        m.Do("__world_ents[1].items = {}; __henry.items[#__henry.items + 1] = __item('" + Coat + "', 1, 0.7)");
        m.Advance(0.6);
        var ask = Assert.Single(m.Lines("KCDUS|LOOT|ask|")).Split('|');
        Assert.Equal("0", ask[9]);                                                          // no price: not a shop
        Assert.Equal("6200", ask[10]);
        m.Send($"2~LOOTRES|{Scope}|{ask[4]}|ok|bandit_7|{Coat}|1");
        Assert.Single(m.Lines($"KCDUS|LOOT|kept|{ask[3]}|{ask[4]}"));
    }

    [Fact]
    public void A_take_made_after_the_loaded_save_is_not_in_it_and_nothing_is_removed()
    {
        var m = Guest(5000);
        m.Do("__henry.items[#__henry.items + 1] = __item('" + Coat + "', 1, 0.7)");          // a coat Henry legitimately has
        m.Send($"2~LOOTRECOVER|{Scope}|{Op}|{Coat}|1|0|6000|gone");
        Assert.Single(m.Lines($"KCDUS|LOOT|recovered|{Op}|0|0|reverted"));
        Assert.Equal(1, HenryHas(m, Coat));
    }

    [Fact]
    public void A_take_the_save_contains_is_returned_on_gone_with_a_shops_price_refunded_and_kept_on_ok()
    {
        var m = Guest(7000);
        m.Do("__henry.items[#__henry.items + 1] = __item('" + Coat + "', 1, 0.7)");
        m.Send($"2~LOOTRECOVER|{Scope}|{Op}|{Coat}|1|120|6000|gone");
        Assert.Single(m.Lines($"KCDUS|LOOT|recovered|{Op}|1|120|returned"));
        Assert.Equal(0, HenryHas(m, Coat));
        Assert.Equal(120, HenryHas(m, Coin));
        m.Send($"3~LOOTRECOVER|{Scope}|{Op}|{Coat}|1|120|6000|gone");                          // a repeat changes nothing
        Assert.Equal(120, HenryHas(m, Coin));
        Assert.Single(m.Lines($"KCDUS|LOOT|recovered|{Op}|0|0|repeat"));

        var k = Guest(7000);
        k.Do("__henry.items[#__henry.items + 1] = __item('" + Coat + "', 1, 0.7)");
        k.Send($"2~LOOTRECOVER|{Scope}|{Op}|{Coat}|1|0|6000|ok");
        Assert.Single(k.Lines($"KCDUS|LOOT|recovered|{Op}|0|0|kept"));
        Assert.Equal(1, HenryHas(k, Coat));
    }

    [Fact]
    public void An_unanswered_take_is_never_guessed_and_bad_lines_are_ignored()
    {
        var m = Guest(7000);
        m.Do("__henry.items[#__henry.items + 1] = __item('" + Coat + "', 1, 0.7)");
        m.Send($"2~LOOTRECOVER|{Scope}|{Op}|{Coat}|1|0|6000|");
        m.Send($"3~LOOTRECOVER|other|{Op}|{Coat}|1|0|6000|gone");
        m.Send($"4~LOOTRECOVER|{Scope}|not-an-op|{Coat}|1|0|6000|gone");
        Assert.Empty(m.Lines("KCDUS|LOOT|recovered|"));
        Assert.Equal(1, HenryHas(m, Coat));
    }

    [Fact]
    public void A_new_load_has_a_new_nonce()
    {
        var m = Guest(5000);
        string before = m.Str("KCDUS.Loot.nonce");
        m.Do("KCDUS.Loot.onWorldReset()");
        Assert.NotEqual(before, m.Str("KCDUS.Loot.nonce"));
    }
}
