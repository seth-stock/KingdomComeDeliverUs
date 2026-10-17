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
        Assert.Equal(saves, SaveStore.Existing(saves));
        Assert.True(Directory.Exists(saves));
    }

    [Fact]
    public void Nothing_is_created_where_no_game_profile_exists()
    {
        string saves = Path.Combine(_dir, "nowhere", "users", "steamuser", "Saved Games", "kingdomcome", "saves");
        Assert.Null(SaveStore.Existing(saves));
        Assert.False(Directory.Exists(Path.Combine(_dir, "nowhere")));
    }
}
