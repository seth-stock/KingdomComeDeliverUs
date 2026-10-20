// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using KcdUs.Agent;
using KcdUs.Agent.Worlds;
using Xunit;

namespace KcdUs.Tests;

/// <summary>More shared-world meetings: the cases around the first one (see WorldSyncTests). (synthetic game, real relay, real files)</summary>
public partial class WorldSyncTests
{
    [Fact]
    public async Task BusyCharacterCaptureRefusesWorldReplacementWithoutUsingAnOldCard()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => r.Seed("w1", 0, 10, 1_790_000_200));
        await using var guest = new Rig("Hans", port, tweak: c => { c.HenryMode = "mine"; c.AutoSync = false; },
            seed: r => { r.NativeTemplate = CharacterFixture.Save(1_790_000_100, 3); r.WriteSave(0, "home.whs", 1_790_000_100, 3); });
        guest.RefuseSaves = true;
        var original = File.ReadAllBytes(guest.Store.Newest(0)!.Path);
        host.EnterWorld();
        await host.Host.HandleAsync("host", ""); await Until(() => host.Host.Session.MyId != 0);
        guest.EnterWorld(); await Until(() => guest.Host.Session.InWorld);
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Game.Has("MSG|World operation stopped: Henry could not be saved"), rigs: new[] { host, guest });
        Assert.False(guest.Game.Has("LOAD|")); Assert.False(guest.Game.Has("CARDSET|"));
        Assert.Null(guest.Store.Newest(4)); Assert.Equal(original, File.ReadAllBytes(guest.Store.Newest(0)!.Path));
    }

    [Fact]
    public async Task WrongPlaylineReadbackRemainsPendingAndIsNotAnnouncedAsAccepted()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => r.Seed("w1", 0, 10, 1_790_000_200));
        await using var guest = new Rig("Hans", port, tweak: c => c.AutoSync = false);
        guest.AutoLoadAck = false;
        host.CurrentHours = 10; host.EnterWorld();
        await host.Host.HandleAsync("host", ""); await Until(() => host.Host.Session.MyId != 0);
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Game.Has("LOAD|0|4"), rigs: new[] { host, guest });
        guest.SavePlaylineOverride = 0; guest.Game.Emit("KCDUS|UILOAD|loaded");
        await Until(() => guest.Game.Has("MSG|The engine saved a different playline"), rigs: new[] { host, guest });
        Assert.NotNull(WorldRegistry.Load(guest.WorldsJson).PendingLoad);
        Assert.Equal("", WorldRegistry.Load(guest.WorldsJson).Active);
        lock (host.Logs) Assert.DoesNotContain(host.Logs, l => l.Contains("Hans is in your game"));
        Assert.Equal(10.5, guest.Store.Newest(4)!.Info.Hours);
    }

    [Fact]
    public async Task Join_waits_while_the_host_is_in_the_main_menu_and_gets_the_new_game_the_host_starts()
    {
        // a new game together: the host hosts from the main menu, the friend presses Join, then the host starts a New Game
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => r.WriteSave(0, "permanent001.whs", 1_780_000_000, 40));   // an old game of the host's
        await using var guest = new Rig("Hans", port);
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Game.Has("MENUTEXT|-1|Waiting for Henry's game"), what: "the friend is told to wait", rigs: new[] { host, guest });
        Assert.False(guest.Game.Has("LOAD|"));
        await Until(() => host.Game.Has("NOTE|Hans is waiting to join"), what: "the host is told who waits", rigs: new[] { host, guest });
        // New Game: the game writes into a new playline, and the host is in the world
        host.CurrentPlayline = 2; host.CurrentHours = 0.1;
        host.WriteSave(2, "autosave1.whs", 1_799_100_000, 0.1);
        host.EnterWorld();
        await Until(() => guest.Game.Has("LOAD|0|4") && WorldRegistry.Load(guest.WorldsJson).PendingLoad is null, ms: 20000, what: "the friend loads the host's new game", rigs: new[] { host, guest });
        Assert.True(guest.Store.Newest(4)!.Info.Hours < 1);                                // the new game, not the host's old 40-hour one
        var hostReg = WorldRegistry.Load(host.WorldsJson);
        Assert.Equal(2, hostReg.ActiveWorld!.Playline);
        Assert.Equal(hostReg.Active, WorldRegistry.Load(guest.WorldsJson).Active);         // the same world on both: fights and loot are shared in it
        await Until(() => host.Game.Has("NOTE|Hans is in your game now"), rigs: new[] { host, guest });
    }

    [Fact]
    public async Task A_player_with_no_copy_gets_the_world_when_he_asks_and_it_lands_in_an_empty_slot()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => r.Seed("w1", 0, 10, 1_790_000_200));
        await using var guest = new Rig("Hans", port, seed: r => { r.WriteSave(0, "permanent001.whs", 1_780_000_000, 5); r.WriteSave(1, "permanent001.whs", 1_780_000_100, 6); });   // his own two games
        host.CurrentHours = 10; host.EnterWorld();
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        await guest.Host.HandleAsync("join", "");                                       // one press: connect, get the game, load it
        await Until(() => guest.Game.Has("LOAD|0|4") && WorldRegistry.Load(guest.WorldsJson).PendingLoad is null, what: "the world is loaded from the highest empty slot", rigs: new[] { host, guest });
        Assert.Equal(10.5, guest.Store.Newest(4)!.Info.Hours, 3);                        // the host's game as it was when he joined (saved for him)
        Assert.Equal(5, guest.Store.Newest(0)!.Info.Hours, 3);                          // his own two games are as they were
        Assert.Equal(6, guest.Store.Newest(1)!.Info.Hours, 3);
        var rec = WorldRegistry.Load(guest.WorldsJson).Find("w1")!;
        Assert.True(rec.Slot);
        Assert.Equal(4, rec.Playline);
        Assert.True(guest.Game.Snapshot().Any(l => l.StartsWith("MENUTEXT|-1|Receiving: ")));   // progress is shown
    }

    [Fact]
    public async Task The_hosts_game_is_played_even_when_the_friends_copy_got_further()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => r.Seed("w1", 0, 3, 1_790_000_100));                     // the host's game
        await using var guest = new Rig("Hans", port, seed: r => r.Seed("w1", 4, 10, 1_790_000_200, slot: true));        // the friend played on alone
        host.CurrentHours = 3; host.EnterWorld();
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Game.Has("LOAD|0|4") && WorldRegistry.Load(guest.WorldsJson).PendingLoad is null, what: "the friend loads the host's game", rigs: new[] { host, guest });
        Assert.Equal(3.5, guest.Store.Newest(4)!.Info.Hours, 3);
        Assert.True(Directory.GetFiles(guest.Store.BackupRoot, "*.whs", SearchOption.AllDirectories).Length >= 1);   // his further copy is backed up, not lost
        Assert.False(host.Game.Has("LOAD|"));                                           // the host never loads anything
    }

    [Fact]
    public async Task Pressing_host_again_keeps_the_friends_who_joined()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => r.Seed("w1", 0, 10, 1_790_000_200));
        await using var guest = new Rig("Hans", port);
        host.CurrentHours = 10; host.EnterWorld();
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Host.Session.MyId != 0, what: "guest up");
        int id = guest.Host.Session.MyId;
        await host.Host.HandleAsync("host", "");
        await Task.Delay(1500);
        Assert.Equal(id, guest.Host.Session.MyId);                                       // the same connection: nobody was dropped
        lock (host.Logs) Assert.DoesNotContain(host.Logs, l => l.Contains("Hans left"));
    }

    [Fact]
    public async Task With_all_five_playlines_in_use_join_asks_first_and_a_second_press_uses_the_oldest_after_a_backup()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => r.Seed("w1", 0, 10, 1_790_000_200));
        await using var guest = new Rig("Hans", port, seed: r => { for (int i = 0; i < 5; i++) r.WriteSave(i, "permanent001.whs", 1_780_000_000 + (i == 2 ? -50 : i), 1 + i); });
        host.CurrentHours = 10; host.EnterWorld();
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Game.Has("MSG|All five playlines are full. Put the host's game into Playline 3"), what: "the question", rigs: new[] { host, guest });
        await Task.Delay(500);
        Assert.False(guest.Game.Has("LOAD|"));
        for (int i = 0; i < 5; i++) Assert.Equal(1 + i, guest.Store.Newest(i)!.Info.Hours, 3);   // nothing was touched before he said yes
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Game.Has("LOAD|0|2") && WorldRegistry.Load(guest.WorldsJson).PendingLoad is null, what: "the world goes into the oldest playline", rigs: new[] { host, guest });
        Assert.Equal(10.5, guest.Store.Newest(2)!.Info.Hours, 3);
        Assert.True(Directory.GetFiles(guest.Store.BackupRoot, "*.whs", SearchOption.AllDirectories).Length >= 1);   // what was there is kept
        for (int i = 0; i < 5; i++) if (i != 2) Assert.Equal(1 + i, guest.Store.Newest(i)!.Info.Hours, 3);
    }

    [Fact]
    public async Task Bring_my_henry_stages_exact_saved_state_and_acknowledges_native_readback()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => { r.NativeTemplate = CharacterFixture.Save(1_790_000_200, 10, 9000, 22); r.Seed("w1", 0, 10, 1_790_000_200); });
        await using var guest = new Rig("Hans", port, tweak: c => { c.AutoSync = false; },
            seed: r => { r.NativeTemplate = CharacterFixture.Save(1_790_000_100, 3, 7, 1); r.WriteSave(0, "home.whs", 1_790_000_100, 3); });
        guest.AutoLoadAck = false;
        var expected = ExactTraitsSave.CaptureCharacter(guest.NativeTemplate!);
        host.CurrentHours = 10; host.EnterWorld();
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        guest.EnterWorld();                                                              // he is in his own game
        await Until(() => guest.Host.Session.InWorld, what: "in world");
        await guest.Host.HandleAsync("joinmine", "");
        await Until(() => guest.Game.Has("LOAD|0|4"), what: "the world loads", rigs: new[] { host, guest });
        Assert.True(guest.Game.Has("SAVEWORLD|"));
        Assert.False(guest.Game.Has("CARDSET|"));
        Assert.NotNull(WorldRegistry.Load(guest.WorldsJson).PendingLoad);
        Assert.True(ExactTraitsSave.SameProgressionAndInventory(expected, ExactTraitsSave.CaptureCharacter(guest.Store.Newest(4) is { } staged ? File.ReadAllBytes(staged.Path) : [])));
        guest.Game.Emit("KCDUS|UILOAD|loaded");
        await Until(() => WorldRegistry.Load(guest.WorldsJson).PendingLoad is null, what: "native character readback acknowledgement", rigs: new[] { host, guest });
        Assert.Equal("w1", WorldRegistry.Load(guest.WorldsJson).Active);
        Assert.False(guest.Game.Has("CARDSET|"));
    }

    [Fact]
    public async Task Coming_back_to_a_world_keeps_the_friends_own_henry_from_his_last_save_of_it()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => { r.NativeTemplate = CharacterFixture.Save(1_790_000_200, 10, 9000, 22); r.Seed("w1", 0, 10, 1_790_000_200); });
        // he played the shared world before: his copy (slot 4) holds HIS Henry, and it is not just the save that verified the last load
        await using var guest = new Rig("Hans", port, seed: r => { r.NativeTemplate = CharacterFixture.Save(1_790_000_100, 6, 444, 5); r.Seed("w1", 4, 6, 1_790_000_100, slot: true); });
        var his = ExactTraitsSave.CaptureCharacter(File.ReadAllBytes(guest.Store.Newest(4)!.Path));
        host.CurrentHours = 10; host.EnterWorld();
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Game.Has("LOAD|0|4") && WorldRegistry.Load(guest.WorldsJson).PendingLoad is null, what: "the world loads", rigs: new[] { host, guest });
        var now = File.ReadAllBytes(guest.Store.Newest(4)!.Path);
        Assert.True(ExactTraitsSave.SameProgressionAndInventory(his, ExactTraitsSave.CaptureCharacter(now)));   // his Henry, in the host's newer world
        Assert.True(guest.Game.Has("NOTE|Got Henry's game. Loading it with your own Henry"));
    }

    [Fact]
    public async Task A_friend_without_the_hosts_dlc_gets_the_world_adjusted_instead_of_refused()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => { r.NativeTemplate = CharacterFixture.Save(1_790_000_200, 10, 9000, 22); r.SaveDlc = new[] { (9, "Band of Bastards") }; r.Seed("w1", 0, 10, 1_790_000_200); });
        await using var guest = new Rig("Hans", port);
        // the guest's own game: Band of Bastards is not switched on (its index is 9 in his kcd.log)
        guest.Host.World!.OwnDlc = () => (new string[0], new Dictionary<string, int> { ["ExpeditionaryRides"] = 9, ["NewHomes"] = 4 });
        host.CurrentHours = 10; host.EnterWorld();
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        Assert.Single(ExactTraitsSave.ReadDlcUse(File.ReadAllBytes(host.Store.Newest(0)!.Path))!);   // the host's world does name the DLC
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Game.Has("LOAD|0|4") && WorldRegistry.Load(guest.WorldsJson).PendingLoad is null, what: "the guest loads the adjusted world", rigs: new[] { host, guest });
        Assert.Empty(ExactTraitsSave.ReadDlcUse(File.ReadAllBytes(Path.Combine(guest.Store.PlaylineDir(4), "world.whs")))!);   // no DLC in the copy he was given (the game then wrote its own save beside it)
        Assert.Single(ExactTraitsSave.ReadDlcUse(File.ReadAllBytes(host.Store.Newest(0)!.Path))!);        // the host's own world is untouched
        Assert.True(guest.Game.Has("NOTE|Band of Bastards is not on this computer"), string.Join(", ", guest.Game.Snapshot().Where(l => l.StartsWith("NOTE|"))));
    }
}
