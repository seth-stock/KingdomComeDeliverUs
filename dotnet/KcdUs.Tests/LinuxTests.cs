// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using KcdUs.Agent;
using Xunit;

namespace KcdUs.Tests;

/// <summary>Linux support (docs/LINUX.md): the key reader's decoding, the held-key state, and where Steam is looked for. (synthetic: no keyboard, no game)</summary>
public class LinuxTests
{
    private static byte[] Event(ushort type, ushort code, int value)
    {
        var b = new byte[LinuxKeys.EventSize];
        BitConverter.TryWriteBytes(b.AsSpan(16), type);
        BitConverter.TryWriteBytes(b.AsSpan(18), code);
        BitConverter.TryWriteBytes(b.AsSpan(20), value);
        return b;
    }

    [Fact]
    public void A_key_event_decodes()
    {
        Assert.True(LinuxKeys.TryParse(Event(1, 87, 1), out var code, out var value));
        Assert.Equal(87, code);
        Assert.Equal(1, value);
    }

    [Fact]
    public void Events_that_are_not_keys_or_are_short_are_ignored()
    {
        Assert.False(LinuxKeys.TryParse(Event(0, 0, 0), out _, out _));       // EV_SYN
        Assert.False(LinuxKeys.TryParse(Event(2, 1, 5), out _, out _));       // EV_REL (mouse)
        Assert.False(LinuxKeys.TryParse(new byte[10], out _, out _));
    }

    [Fact]
    public void Press_hold_and_release_track_the_down_state()
    {
        using var k = new LinuxKeys();
        Assert.False(k.IsDownVk(Hotkeys.VkF11));
        k.Apply(LinuxKeys.KeyF11, 1);
        Assert.True(k.IsDownVk(Hotkeys.VkF11));
        Assert.False(k.IsDownVk(Hotkeys.VkF12));
        k.Apply(LinuxKeys.KeyF11, 2);                    // autorepeat: still down
        Assert.True(k.IsDownVk(Hotkeys.VkF11));
        k.Apply(LinuxKeys.KeyF11, 0);
        Assert.False(k.IsDownVk(Hotkeys.VkF11));
    }

    [Fact]
    public void The_shared_hotkey_edge_logic_fires_once_per_press_from_the_linux_state()
    {
        using var k = new LinuxKeys();
        int joins = 0, stays = 0;
        var hk = new Hotkeys(() => true) { IsDown = k.IsDownVk };
        hk.Join += () => joins++;
        hk.Stay += () => stays++;
        hk.Poll();
        k.Apply(LinuxKeys.KeyF11, 1); hk.Poll(); hk.Poll(); hk.Poll();   // held: one edge
        k.Apply(LinuxKeys.KeyF11, 0); hk.Poll();
        k.Apply(LinuxKeys.KeyF12, 1); hk.Poll();
        k.Apply(LinuxKeys.KeyF12, 0); hk.Poll();
        k.Apply(LinuxKeys.KeyF11, 1); hk.Poll();
        Assert.Equal(2, joins);
        Assert.Equal(1, stays);
    }

    [Fact]
    public void No_input_directory_means_no_devices_not_an_error()
    {
        using var k = new LinuxKeys();
        var lines = new List<string>();
        Assert.Equal(0, k.Start(lines.Add, Path.Combine(Path.GetTempPath(), "kcdus-no-such-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void Linux_steam_candidates_name_the_usual_places()
    {
        var c = GameLocator.LinuxSteamCandidates("/home/me").Select(p => p.Replace('\\', '/')).ToArray();
        Assert.Contains("/home/me/.steam/steam", c);
        Assert.Contains("/home/me/.local/share/Steam", c);
        Assert.Contains("/home/me/.var/app/com.valvesoftware.Steam/.local/share/Steam", c);
    }

    [Fact]
    public void The_game_is_found_in_a_library_listed_in_libraryfolders_vdf_under_config_too()
    {
        string root = Path.Combine(Path.GetTempPath(), "kcdus-steam-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            string lib2 = Path.Combine(root, "lib2");
            string game = Path.Combine(lib2, "steamapps", "common", GameLocator.FolderName);
            Directory.CreateDirectory(Path.Combine(game, "Bin", "Win64")); Directory.CreateDirectory(Path.Combine(game, "Data"));
            File.WriteAllText(Path.Combine(game, "Bin", "Win64", "KingdomCome.exe"), "");
            Directory.CreateDirectory(Path.Combine(root, "steam", "config"));
            File.WriteAllText(Path.Combine(root, "steam", "config", "libraryfolders.vdf"), $"\"libraryfolders\"\n{{\n \"1\"\n {{\n  \"path\"  \"{lib2.Replace("\\", "\\\\")}\"\n }}\n}}\n");
            Assert.Equal(game, GameLocator.Find(null, Path.Combine(root, "steam")));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
