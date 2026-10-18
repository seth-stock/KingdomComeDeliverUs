// SPDX-License-Identifier: GPL-3.0-only
// A player who never saved (a fresh Proton prefix) has no "saves" folder: shared worlds must still work, so the agent makes it inside the game's own folder.
using KcdUs.Agent.Worlds;

namespace KcdUs.Tests;

public class SaveRootTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kcdus-saveroot-" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void A_missing_saves_folder_inside_an_existing_game_profile_is_created()
    {
        string profile = Path.Combine(_dir, "pfx", "drive_c", "users", "steamuser");
        Directory.CreateDirectory(Path.Combine(profile, "Saved Games"));
        string saves = Path.Combine(profile, "Saved Games", "kingdomcome", "saves");
        Assert.Equal(saves, SaveStore.Existing(saves), StringComparer.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(saves));
    }

    [Fact]
    public void The_game_folder_is_found_whatever_its_case_and_its_saves_are_used()
    {
        // the game makes "KingdomCome"; the agent used to look for "kingdomcome" (a Linux file system tells them apart)
        string profile = Path.Combine(_dir, "pfx2", "drive_c", "users", "steamuser");
        string real = Path.Combine(profile, "Saved Games", "KingdomCome", "saves", "playline0");
        Directory.CreateDirectory(real);
        File.WriteAllBytes(Path.Combine(real, "autosave1.whs"), new byte[] { 1 });
        var found = SaveStore.InProfile(profile)!;
        Assert.True(File.Exists(Path.Combine(found, "playline0", "autosave1.whs")));
    }

    [Fact]
    public void The_proton_prefix_is_found_in_another_steam_library_than_the_games()
    {
        string libA = Path.Combine(_dir, "libA"), libB = Path.Combine(_dir, "libB");
        string game = Path.Combine(libA, "steamapps", "common", "KingdomComeDeliverance");
        Directory.CreateDirectory(game);
        string user = Path.Combine(libB, "steamapps", "compatdata", "379430", "pfx", "drive_c", "users", "steamuser");
        Directory.CreateDirectory(Path.Combine(user, "Saved Games", "KingdomCome", "saves", "playline2"));
        if (OperatingSystem.IsWindows()) return;   // FindRoot on Windows is the profile's own Saved Games
        var root = SaveStore.FindRoot(null, game, new[] { libA, libB });
        Assert.Equal(Path.Combine(user, "Saved Games", "KingdomCome", "saves"), root);
    }

    [Fact]
    public void Nothing_is_created_where_no_game_profile_exists()
    {
        string saves = Path.Combine(_dir, "nowhere", "users", "steamuser", "Saved Games", "kingdomcome", "saves");
        Assert.Null(SaveStore.Existing(saves));
        Assert.False(Directory.Exists(Path.Combine(_dir, "nowhere")));
    }
}
