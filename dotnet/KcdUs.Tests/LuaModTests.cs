// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using KcdUs.Wire;

namespace KcdUs.Tests;

public class LuaModTests
{
    [Fact]
    public void The_mod_boots_without_a_single_error_and_announces_itself()
    {
        var m = new LuaMod();
        Assert.Empty(m.Lines("KCDUS|ERR"));
        Assert.Equal(new[] { "KCDUS|HELLO|0.0.0-dev|1|dev" }, m.Lines("KCDUS|HELLO"));
        foreach (var c in new[] { "kcdus", "kcdus_say", "kcdus_status", "kcdus_off", "kcdus_on" })
            Assert.True(m.Bool($"__cmds[\"{c}\"] ~= nil"), c);
        // the agent's command must take the whole line as one quoted argument
        Assert.Equal("KCDUS_In(%line)", m.Str("__cmds[\"kcdus\"].code"));
    }

    [Fact]
    public void The_generated_quest_list_loads_and_covers_the_gated_quests()
    {
        var m = new LuaMod();
        Assert.True(m.Num("#KCDUS_QUESTS") > 80);
        Assert.True(m.Bool("(function() for _, q in ipairs(KCDUS_QUESTS) do if q.code == 'q_skalitz' and q.tier == 'mixed' and q.kind == 'main' then return true end end return false end)()"));
    }

    [Fact]
    public void The_menu_is_not_a_world_so_nothing_is_sampled_but_the_heartbeat_runs()
    {
        var m = new LuaMod();
        m.Advance(3.2);
        Assert.Equal(0, m.Lines("KCDUS|ST|").Count);
        Assert.InRange(m.Lines("KCDUS|HB|").Count, 2, 4);
    }

    [Fact]
    public void In_a_world_the_player_is_sampled_ten_times_a_second()
    {
        var m = new LuaMod();
        m.InWorld();
        m.ClearLog();
        m.Advance(2.0);
        var st = m.Lines("KCDUS|ST|");
        Assert.InRange(st.Count, 17, 22);
        Assert.Equal(1, m.Lines("KCDUS|READY|1").Count);
        // KCDUS|ST|x,y,z|yaw|vx,vy,vz|flags|hp|stam|anim|worldTime
        var f = st[0].Split('|');
        Assert.Equal("10.00,20.00,30.00", f[2]);
        Assert.Equal("1.500", f[3]);
        Assert.Equal("1.00,2.00,0.00", f[4]);
        Assert.Equal("0", f[5]);
        Assert.Equal("100", f[6]);
        Assert.Equal("105", f[7]);
        Assert.Equal("MotionIdle", f[8]);
        Assert.Equal("36000", f[9]);
        // the C# side decodes exactly what the Lua side wrote
        var s = PlayerState.TryDecode(f, 2)!;
        Assert.Equal(20.0, s.Y);
        Assert.Equal(105, s.Stamina);
    }

    [Fact]
    public void Flags_report_weapon_horse_dialog_death_and_danger()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Do("__world.weapon = true; __world.mounted = true; __world.danger = true");
        m.ClearLog();
        m.Advance(0.5);
        Assert.Equal("19", m.Lines("KCDUS|ST|")[0].Split('|')[5]);
        m.Do("__world.weapon = false; __world.mounted = false; __world.danger = false; __world.dialog = true; __world.dead = true");
        m.ClearLog();
        m.Advance(0.5);
        Assert.Equal("12", m.Lines("KCDUS|ST|")[0].Split('|')[5]);
    }

    [Fact]
    public void The_main_menu_scene_is_not_a_world_even_though_a_game_is_started_and_a_player_exists()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Do("__world.camera = { x = 991.0, y = 3508.0, z = 54.0 }; __world.pos = { x = 728.9, y = 3411.6, z = 63.8 }");   // observed: 280 m away
        m.ClearLog();
        m.Advance(3.0);
        Assert.Equal(0, m.Lines("KCDUS|ST|").Count);
        Assert.Equal(0, m.Lines("KCDUS|READY|1").Count);
        Assert.InRange(m.Lines("KCDUS|HB|").Count, 2, 4);      // the mod is alive and answering, just not reporting a player
        m.Do("__world.camera = nil");                           // the save finished loading: the camera is behind the player
        m.ClearLog();
        m.Advance(1.0);
        Assert.Equal(1, m.Lines("KCDUS|READY|1").Count);
        Assert.True(m.Lines("KCDUS|ST|").Count >= 8);
    }

    [Fact]
    public void A_cutscene_camera_far_away_for_a_moment_does_not_make_the_world_flap()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Advance(1.0);
        m.Do("__world.camera = { x = 400.0, y = 20.0, z = 30.0 }");   // a long establishing shot
        m.ClearLog();
        m.Advance(1.0);                                                 // inside the grace
        Assert.Equal(0, m.Lines("KCDUS|READY|0").Count);
        m.Advance(1.5);                                                 // it stayed: that is a menu
        Assert.Equal(1, m.Lines("KCDUS|READY|0").Count);
        m.Do("__world.camera = nil");
        m.ClearLog();
        m.Advance(0.5);
        Assert.Equal(1, m.Lines("KCDUS|READY|1").Count);
    }

    [Fact]
    public void A_loading_screen_is_not_a_world()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Advance(0.5);
        m.Do("__world.loading = true");
        m.ClearLog();
        m.Advance(1.0);
        Assert.Equal(0, m.Lines("KCDUS|ST|").Count);
        Assert.Equal(1, m.Lines("KCDUS|READY|0").Count);
    }

    [Fact]
    public void Dropped_timers_after_a_load_are_revived_by_the_players_load_hook()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Advance(1.0);
        m.Do("__dropTimers()");           // what a save load does to every script timer
        m.ClearLog();
        m.Advance(2.0);
        Assert.Equal(0, m.Lines("KCDUS|ST|").Count);   // dead
        m.Do("Player.OnLoad({})");        // the engine rebuilds the player entity
        m.ClearLog();
        m.Advance(1.0);
        Assert.InRange(m.Lines("KCDUS|ST|").Count, 8, 12);
    }

    [Fact]
    public void Dropped_timers_are_also_revived_by_any_line_from_the_agent_and_never_double_up()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Advance(1.0);
        m.Do("__dropTimers()");
        m.Advance(1.0);                   // stale for a second
        m.Send("1~PING|1");
        m.ClearLog();
        m.Advance(1.0);
        int n = m.Lines("KCDUS|ST|").Count;
        Assert.InRange(n, 8, 12);
        // a second kick while the loop is alive must not start a second loop
        m.Send("2~PING|2"); m.Send("3~PING|3");
        m.ClearLog();
        m.Advance(1.0);
        Assert.InRange(m.Lines("KCDUS|ST|").Count, 8, 12);
    }

    [Fact]
    public void Ping_is_answered_with_the_last_sequence_number()
    {
        var m = new LuaMod();
        m.Send("41~PING|7");
        Assert.Contains("KCDUS|PONG|7|41", m.Log());
    }

    [Fact]
    public void A_remote_player_becomes_a_body_that_follows_the_updates()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Send("1~P|2|Hans|100.0,200.0,30.0|0.5|0.00,0.00,0.00|0|100|100|MotionIdle");
        m.Advance(0.2);
        Assert.Equal(1, m.Num("#__spawned"));
        Assert.Equal("NPC", m.Str("__spawned[1].class"));
        Assert.Equal("kcdus_ghost_2", m.Str("__spawned[1].name"));
        Assert.True(m.Bool("__spawned[1].flags.invulnerable and __spawned[1].flags.btOff and __spawned[1].flags.noDialog"));
        Assert.Equal("40f91af6-8aa6-fa06-c171-785c048b0893", m.Str("__spawned[1].flags.clothes"));
        Assert.Contains("KCDUS|GHOST|spawned|2|Hans", m.Log());
        Assert.InRange(m.Num("__spawned[1].pos.x"), 99.5, 100.5);

        // he walks: velocity 3 m/s east, updates 10 times a second
        for (int i = 1; i <= 20; i++)
        {
            m.Send($"{i + 1}~P|2|Hans|{(100.0 + i * 0.3).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},200.00,30.00|0.5|3.00,0.00,0.00|0|100|100|MotionMovement");
            m.Advance(0.1);
        }
        double x = m.Num("__spawned[1].pos.x");
        Assert.InRange(x, 105.0, 106.8);   // glided to about where he is, not left behind, not past him
        Assert.True(m.Num("__spawned[1].moves") > 40);   // moved every tick, not only on updates
    }

    [Fact]
    public void A_teleport_snaps_the_body_instead_of_gliding_across_the_map()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Send("1~P|2|Hans|100.0,200.0,30.0|0|0,0,0|0|100|100|MotionIdle");
        m.Advance(0.2);
        m.Send("2~P|2|Hans|900.0,800.0,30.0|0|0,0,0|0|100|100|MotionIdle");
        m.Advance(0.12);
        Assert.InRange(m.Num("__spawned[1].pos.x"), 899.0, 901.0);
    }

    [Fact]
    public void A_player_who_leaves_is_removed_and_all_can_be_cleared()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Send("1~P|2|Hans|1,2,3|0|0,0,0|0|100|100|x~P|3|Anna|4,5,6|0|0,0,0|0|100|100|x");
        m.Advance(0.2);
        Assert.Equal(2, m.Num("#__spawned"));
        m.Send("2~PD|2");
        Assert.Single(m.L.DoString("return __removed").Table.Values);
        Assert.Contains("KCDUS|GHOST|removed|2|Hans", m.Log());
        m.Send("3~PA");
        Assert.Equal(2, m.Num("#__removed"));
        m.Advance(0.5);
        Assert.Equal(2, m.Num("#__spawned"));   // nothing respawned: they are gone
    }

    [Fact]
    public void A_body_the_engine_removed_is_spawned_again()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Send("1~P|2|Hans|1,2,3|0|0,0,0|0|100|100|x");
        m.Advance(0.2);
        m.Do("__entities[__spawned[1].id] = nil");   // the area unloaded under it
        m.Advance(1.5);
        Assert.Equal(2, m.Num("#__spawned"));
    }

    [Fact]
    public void A_failing_spawn_is_reported_a_few_times_and_retried_without_flooding()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Do("__world.failSpawn = true");
        m.Send("1~P|2|Hans|1,2,3|0|0,0,0|0|100|100|x");
        m.Advance(30.0);
        int errs = m.Lines("KCDUS|ERR|ghost:spawn").Count;
        Assert.InRange(errs, 1, 5);
        m.Do("__world.failSpawn = false");
        m.Advance(2.0);
        Assert.Equal(1, m.Num("#__spawned"));
    }

    [Fact]
    public void A_malformed_record_is_ignored_and_counted_not_fatal()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Send("1~P||||~NOPE|x~P|9|Bob");
        m.Send("2~TIME|abc");
        m.Advance(0.5);
        // still alive and sampling
        Assert.InRange(m.Lines("KCDUS|ST|").Count, 3, 6);
    }

    [Fact]
    public void Shared_time_is_applied_when_the_agent_says_so()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Send("1~TIME|45000");
        Assert.Contains("SetWorldTime:45000", m.L.DoString("return table.concat(__calls, ',')").String.Split(','));
    }

    [Fact]
    public void Notifications_chat_and_prompts_reach_the_player()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Send("1~NOTE|Hans joined");
        m.Send("2~CHAT|Hans|hello there");
        Assert.Equal("Hans joined", m.Str("__notes[1]"));
        Assert.Equal("Hans: hello there", m.Str("__notes[2]"));
        m.Send("3~PROMPT|Your host is on rails. F11 join, F12 stay.|30");
        Assert.Equal(1, m.Num("#__infos"));
        m.Advance(7.0);
        Assert.True(m.Num("#__infos") >= 3);       // the held prompt is re-sent
        m.Send("4~PROMPTCLEAR");
        int n = (int)m.Num("#__infos");
        m.Advance(7.0);
        Assert.Equal(n, (int)m.Num("#__infos"));    // and stops
    }

    [Fact]
    public void Chat_the_player_types_leaves_clean_and_is_echoed_locally()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Do("KCDUS_Say('hi \"you\" | ~ \\\\ there')");
        var chat = m.Lines("KCDUS|CHAT|");
        Assert.Single(chat);
        Assert.DoesNotContain("\"", chat[0]);
        Assert.Equal(2, chat[0].Split('|').Length - 1);   // KCDUS|CHAT|text : no stray separators
        Assert.StartsWith("You: hi", m.Str("__notes[1]"));
    }

    [Fact]
    public void Quest_changes_are_reported_once_and_resent_on_a_snapshot_request()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Advance(2.0);
        Assert.Empty(m.Lines("KCDUS|Q|"));   // nothing is going on: nothing to say
        m.Do("__world.quests.q_escapeToTalmberk = { started = true, completed = false, objectives = { 12, 5 } }");
        m.ClearLog();
        m.Advance(7.0);   // the whole list is visited about every 5 s
        Assert.Equal(new[] { "KCDUS|Q|q_escapeToTalmberk|1|0|5,12" }, m.Lines("KCDUS|Q|"));
        m.ClearLog();
        m.Advance(7.0);   // the whole list is visited about every 5 s
        Assert.Empty(m.Lines("KCDUS|Q|"));   // unchanged: silent
        m.Do("__world.quests.q_escapeToTalmberk.objectives = { 12, 5, 99 }");
        m.ClearLog();
        m.Advance(7.0);   // the whole list is visited about every 5 s
        Assert.Equal(new[] { "KCDUS|Q|q_escapeToTalmberk|1|0|5,12,99" }, m.Lines("KCDUS|Q|"));
        m.Do("__world.quests.q_escapeToTalmberk = { started = true, completed = true, objectives = {} }");
        m.ClearLog();
        m.Advance(7.0);   // the whole list is visited about every 5 s
        Assert.Equal(new[] { "KCDUS|Q|q_escapeToTalmberk|1|1|" }, m.Lines("KCDUS|Q|"));
        // the agent just connected and wants everything again
        m.Send("9~QSNAP");
        m.ClearLog();
        m.Advance(7.0);   // the whole list is visited about every 5 s
        Assert.Equal(new[] { "KCDUS|Q|q_escapeToTalmberk|1|1|" }, m.Lines("KCDUS|Q|"));
    }

    [Fact]
    public void Every_watched_quest_is_visited_within_a_few_seconds()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Do("for _, q in ipairs(KCDUS_QUESTS) do __world.quests[q.code] = { started = true, completed = false, objectives = { 1 } } end");
        m.ClearLog();
        m.Advance(8.0);   // two quests every 0.1 s: the whole list in about 5 s
        Assert.Equal((int)m.Num("#KCDUS_QUESTS"), m.Lines("KCDUS|Q|").Count);
    }

    [Fact]
    public void The_off_command_stops_the_loop_and_on_restarts_it()
    {
        var m = new LuaMod();
        m.InWorld();
        m.Advance(0.5);
        m.Do("KCDUS.stop()");
        m.ClearLog();
        m.Advance(1.0);
        Assert.Equal(0, m.Lines("KCDUS|ST|").Count);
        m.Do("KCDUS.start()");
        m.Advance(1.0);
        Assert.InRange(m.Lines("KCDUS|ST|").Count, 8, 12);
    }

    [Fact]
    public void Console_text_never_carries_a_quote_or_a_backslash_out_of_the_mod()
    {
        var m = new LuaMod();
        Assert.Equal("a  b  c d", m.Str("KCDUS.clean('a\"|b\\\\~c\\nd')"));
        Assert.Equal("", m.Str("KCDUS.clean(nil)"));
    }

    [Fact]
    public void The_mod_costs_few_script_calls_per_second()
    {
        // A budget, not a benchmark: the agent paces its commands and the mod samples at 10 Hz, so a quiet second is cheap.
        var m = new LuaMod();
        m.InWorld();
        m.Advance(2.0);
        m.ClearLog();
        m.Advance(10.0);
        // 10 ST + 1 HB per second, and nothing else
        Assert.InRange(m.Log().Count, 100, 130);
    }

    [Fact]
    public void A_menu_button_reaches_the_agent_as_one_line_and_cannot_break_the_line()
    {
        var m = new LuaMod();
        m.ClearLog();
        m.Do("KCDUS_Menu('host', '')");
        m.Do("KCDUS_Menu('pref', 'a|b' .. string.char(10) .. 'c')");
        Assert.Contains("KCDUS|MENU|host|", m.Lines("KCDUS|MENU"));
        foreach (var l in m.Lines("KCDUS|MENU")) Assert.Equal(4, l.Split('|').Length);   // a '|' in an argument cannot add a field
        Assert.Empty(m.Lines("KCDUS|ERR"));
    }

    [Fact]
    public void The_agent_can_ask_the_game_to_load_a_save_by_its_list_position()
    {
        var m = new LuaMod();
        m.ClearLog();
        m.Send("1~LOAD|3|1");
        Assert.Equal(3, m.Num("__globals.KCDUS_LoadSaveId"));
        Assert.Equal(1, m.Num("__globals.KCDUS_LoadPlayLine"));
        Assert.Equal("MP_Load", m.Str("__actions[1]"));
        Assert.Contains("KCDUS|LOADING|3|1|1|", m.Lines("KCDUS|LOADING"));
    }

    [Fact]
    public void A_load_the_game_refuses_is_reported_not_thrown()
    {
        var m = new LuaMod();
        m.Do("__world.actionFails = true");
        m.ClearLog();
        m.Send("1~LOAD|0|1");
        Assert.Single(m.Lines("KCDUS|LOADING|0|1|0|"));
        Assert.Empty(m.Lines("KCDUS|ERR"));
    }

    [Fact]
    public void A_world_save_is_one_engine_call_and_its_failure_is_reported()
    {
        var m = new LuaMod();
        m.ClearLog();
        m.Send("1~SAVEWORLD|x");
        Assert.True(m.Bool("__calls[#__calls] == 'SaveGameViaResting'"));
        Assert.Single(m.Lines("KCDUS|SAVEWORLD|x|1"));
        m.Do("__world.saveFails = true");
        m.Send("2~SAVEWORLD|y");
        Assert.Single(m.Lines("KCDUS|SAVEWORLD|y|0"));
    }

    [Fact]
    public void The_agents_title_code_starts_the_title_graph_of_that_code()
    {
        var m = new LuaMod();
        m.Send("1~MENUTEXT|1");
        Assert.Equal("MP_Title1", m.Str("__actions[1]"));
        m.Do("__world.actionFails = true");
        m.Send("2~MENUTEXT|2");   // a game without the page (not installed) is no error
        Assert.Empty(m.Lines("KCDUS|ERR"));
    }

    [Fact]
    public void A_Henrys_card_is_read_in_pieces_and_every_piece_is_one_safe_log_line()
    {
        var m = new LuaMod();
        m.Do("__henry.stats.str = 12; __henry.skills.fencing = 7.5; for i = 1, 60 do __henry.items[#__henry.items + 1] = { class = string.format('aaaaaaaa-0000-0000-0000-%012d', i), health = 0.5, amount = 2 } end");
        m.ClearLog();
        m.Send("1~CARDGET|c1");
        var pieces = m.Lines("KCDUS|CARD|c1|");
        Assert.True(pieces.Count > 3);                                    // a big pack is several pieces
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < pieces.Count; i++)
        {
            var f = pieces[i].Split('|');
            Assert.Equal(6, f.Length);                                    // KCDUS|CARD|id|i|n|text: nothing in the text can add a field
            Assert.Equal(i.ToString(), f[3]);
            Assert.Equal(pieces.Count.ToString(), f[4]);
            Assert.True(f[5].Length <= 380);
            text.Append(f[5]);
        }
        var card = text.ToString();
        Assert.Contains("t:str=12", card);
        Assert.Contains("s:fencing=7.5", card);
        Assert.Contains("i:aaaaaaaa-0000-0000-0000-000000000060,0.50,2", card);
        Assert.DoesNotContain("~", card);
    }

    [Fact]
    public void A_card_raises_levels_and_adds_things_but_never_lowers_or_removes()
    {
        var m = new LuaMod();
        m.Do("__henry.stats.str = 9; __henry.skills.fencing = 4");   // the world's Henry is already stronger than the card in these two
        m.ClearLog();
        // the card: str 5 (lower: ignored), agi 6 (higher: raised), fencing 2 (lower), sword skill 3 (raised), 1500 groschen, two swords, the thing he already has
        var card = "t:str=5;t:agi=6;s:fencing=2;s:weapon_sword=3;i:5ef63059-322e-4e1b-abe8-926e100c770e,1.00,1500;i:bbbbbbbb-0000-0000-0000-000000000001,0.80,2;i:aaaaaaaa-0000-0000-0000-000000000001,1.00,1";
        m.Send("1~CARDSET|c2|0|2|" + card[..40]);
        Assert.Empty(m.Lines("KCDUS|CARDSET"));                             // not applied until every piece is here
        m.Send("2~CARDSET|c2|1|2|" + card[40..]);
        Assert.Equal(9, m.Num("__henry.stats.str"));                     // never lowered
        Assert.Equal(6, m.Num("__henry.stats.agi"));
        Assert.Equal(4, m.Num("__henry.skills.fencing"));
        Assert.Equal(3, m.Num("__henry.skills.weapon_sword"));
        Assert.Equal(1500, m.Num("(function() local n = 0; for _, it in ipairs(__henry.items) do if it.class == '5ef63059-322e-4e1b-abe8-926e100c770e' then n = n + it.amount end end return n end)()"));
        Assert.Equal(2, m.Num("(function() local n = 0; for _, it in ipairs(__henry.items) do if string.sub(it.class, 1, 8) == 'bbbbbbbb' then n = n + 1 end end return n end)()"));   // two single swords
        Assert.Equal(1, m.Num("(function() local n = 0; for _, it in ipairs(__henry.items) do if it.class == 'aaaaaaaa-0000-0000-0000-000000000001' then n = n + it.amount end end return n end)()"));   // not doubled
        Assert.Single(m.Lines("KCDUS|CARDSET|c2|2|2|0"));                  // two levels raised, two things added, none failed
        Assert.Empty(m.Lines("KCDUS|ERR"));
    }

    [Fact]
    public void A_damaged_card_is_ignored_piece_by_piece()
    {
        var m = new LuaMod();
        m.ClearLog();
        m.Send("1~CARDSET|c3|0|1|t:str=notanumber;s:nosuchskill=3;i:zz,x,y;garbage;i:5ef63059-322e-4e1b-abe8-926e100c770e,1.00,10");
        Assert.Empty(m.Lines("KCDUS|ERR"));
        Assert.Single(m.Lines("KCDUS|CARDSET|c3|"));
        m.Send("2~CARDSET|c4|7|3|x");                                        // a piece that cannot be right
        Assert.Empty(m.Lines("KCDUS|CARDSET|c4"));
    }
}
