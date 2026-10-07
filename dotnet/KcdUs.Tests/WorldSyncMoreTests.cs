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
        await host.Host.HandleAsync("host", ""); await Until(() => host.Host.Session.MyId != 0);
        await guest.Host.HandleAsync("join", ""); await Until(() => guest.Host.Session.MyId != 0);
        guest.EnterWorld(); await Until(() => guest.Host.Session.InWorld);
        await guest.Host.HandleAsync("world", "join");
        await Until(() => guest.Game.Has("NOTE|World operation stopped: Henry could not be saved"), rigs: new[] { host, guest });
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
        await host.Host.HandleAsync("host", ""); await Until(() => host.Host.Session.MyId != 0);
        await guest.Host.HandleAsync("join", ""); await Until(() => guest.Host.Session.MyId != 0);
        await guest.Host.HandleAsync("world", "join");
        await Until(() => guest.Game.Has("LOAD|0|4"), rigs: new[] { host, guest });
        guest.SavePlaylineOverride = 0; guest.Game.Emit("KCDUS|UILOAD|loaded");
        await Until(() => guest.Game.Has("NOTE|The engine saved a different playline"), rigs: new[] { host, guest });
        Assert.NotNull(WorldRegistry.Load(guest.WorldsJson).PendingLoad);
        Assert.Equal("", WorldRegistry.Load(guest.WorldsJson).Active);
        lock (host.Logs) Assert.DoesNotContain(host.Logs, l => l.Contains("Hans has your world"));
        Assert.Equal(10, guest.Store.Newest(4)!.Info.Hours);
    }

    [Fact]
    public async Task Two_players_whose_copies_match_move_nothing()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => r.Seed("w1", 0, 10, 1_790_000_200));
        await using var guest = new Rig("Hans", port, seed: r => r.Seed("w1", 4, 10, 1_790_000_200, slot: true));
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Host.Session.GetStatus().RelayConnected, what: "guest up");
        await Task.Delay(2500);                                                         // long enough for the stamps to cross
        Assert.False(guest.Game.Has("LOAD|"));
        Assert.False(host.Game.Has("LOAD|"));
    }

    [Fact]
    public async Task A_player_with_no_copy_gets_the_world_when_he_asks_and_it_lands_in_an_empty_slot()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => r.Seed("w1", 0, 10, 1_790_000_200));
        await using var guest = new Rig("Hans", port, seed: r => { r.WriteSave(0, "permanent001.whs", 1_780_000_000, 5); r.WriteSave(1, "permanent001.whs", 1_780_000_100, 6); });   // his own two games
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Game.Has("NOTE|Henry has the shared world"), what: "the offer is announced", rigs: new[] { host, guest });
        Assert.False(guest.Game.Has("LOAD|"));                                          // nothing is taken until he asks (he may be in the middle of his own game)
        await guest.Host.HandleAsync("world", "join");
        await Until(() => guest.Game.Has("LOAD|0|4") && WorldRegistry.Load(guest.WorldsJson).PendingLoad is null, what: "the world is loaded from the highest empty slot", rigs: new[] { host, guest });
        Assert.Equal(10, guest.Store.Newest(4)!.Info.Hours, 3);
        Assert.Equal(5, guest.Store.Newest(0)!.Info.Hours, 3);                          // his own two games are as they were
        Assert.Equal(6, guest.Store.Newest(1)!.Info.Hours, 3);
        var rec = WorldRegistry.Load(guest.WorldsJson).Find("w1")!;
        Assert.True(rec.Slot);
        Assert.Equal(4, rec.Playline);
    }

    [Fact]
    public async Task The_host_who_is_behind_takes_the_friends_world_into_a_free_slot_and_keeps_his_own_game()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => r.Seed("w1", 0, 3, 1_790_000_100));                     // the host's own game, behind
        await using var guest = new Rig("Hans", port, seed: r => r.Seed("w1", 4, 10, 1_790_000_200, slot: true));        // the friend played on alone
        var own = File.ReadAllBytes(Path.Combine(host.Store.PlaylineDir(0), "permanent001.whs"));
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        await guest.Host.HandleAsync("join", "");
        await Until(() => host.Game.Has("LOAD|0|4") && WorldRegistry.Load(host.WorldsJson).PendingLoad is null, what: "the host loads the friend's further-along world", rigs: new[] { host, guest });
        Assert.Equal(10, host.Store.Newest(4)!.Info.Hours, 3);
        Assert.Equal(own, File.ReadAllBytes(Path.Combine(host.Store.PlaylineDir(0), "permanent001.whs")));   // his own game is untouched
        var rec = WorldRegistry.Load(host.WorldsJson).Find("w1")!;
        Assert.Equal(4, rec.Playline);
        Assert.True(rec.Slot);
        Assert.Equal(0, rec.HomePlayline);
        Assert.False(guest.Game.Has("LOAD|"));                                          // the friend, who was ahead, loads nothing
    }

    [Fact]
    public async Task With_all_five_playlines_in_use_a_world_is_refused_and_nothing_is_overwritten()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => r.Seed("w1", 0, 10, 1_790_000_200));
        await using var guest = new Rig("Hans", port, seed: r => { for (int i = 0; i < 5; i++) r.WriteSave(i, "permanent001.whs", 1_780_000_000 + i, 1 + i); });
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Host.Session.GetStatus().RelayConnected, what: "guest up");
        await guest.Host.HandleAsync("world", "join");
        await Until(() => guest.Game.Has("NOTE|All five playlines are in use"), what: "the refusal", rigs: new[] { host, guest });
        Assert.False(guest.Game.Has("LOAD|"));
        for (int i = 0; i < 5; i++) Assert.Equal(1 + i, guest.Store.Newest(i)!.Info.Hours, 3);
    }

    [Fact]
    public async Task Bring_my_henry_stages_exact_saved_state_and_acknowledges_native_readback()
    {
        int port = FreePort();
        await using var host = new Rig("Henry", port, seed: r => { r.NativeTemplate = CharacterFixture.Save(1_790_000_200, 10, 9000, 22); r.Seed("w1", 0, 10, 1_790_000_200); });
        await using var guest = new Rig("Hans", port, tweak: c => { c.HenryMode = "mine"; c.AutoSync = false; },
            seed: r => { r.NativeTemplate = CharacterFixture.Save(1_790_000_100, 3, 7, 1); r.WriteSave(0, "home.whs", 1_790_000_100, 3); });
        guest.AutoLoadAck = false;
        var expected = ExactTraitsSave.CaptureCharacter(guest.NativeTemplate!);
        await host.Host.HandleAsync("host", "");
        await Until(() => host.Host.Session.GetStatus().RelayConnected, what: "host up");
        await guest.Host.HandleAsync("join", "");
        await Until(() => guest.Host.Session.GetStatus().RelayConnected, what: "guest up");
        guest.EnterWorld();                                                              // he is in his own game now
        await Until(() => guest.Host.Session.InWorld, what: "in world");
        await guest.Host.HandleAsync("world", "join");
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
}
