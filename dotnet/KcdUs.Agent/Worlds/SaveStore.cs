// Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
// belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;

namespace KcdUs.Agent.Worlds;

public sealed record SaveFile(string Path, int Playline, SaveInfo Info, DateTime ModifiedUtc);

/// <summary>
/// The game's save folder: <c>Saved Games\kingdomcome\saves\playline&lt;N&gt;\*.whs</c>. The mod reads saves to compare two copies of a world and
/// writes exactly one kind of file: a world a friend sent, into an EMPTY playline slot (the game has five, folders playline0..4; a folder outside them
/// stops the game from starting: seen), or into the slot a shared world already took. Another playline of the player's is never touched. A copy that
/// is replaced is moved to a backup folder, not deleted.
/// </summary>
public sealed class SaveStore
{
    /// <summary>How many playlines the game has (the New Game screen lists five; the folders are playline0..playline4).</summary>
    public const int MaxPlaylines = 5;

    public string Root { get; }
    public string BackupRoot { get; }

    public SaveStore(string root, string backupRoot) { Root = root; BackupRoot = backupRoot; }

    /// <summary>Windows: Saved Games under the profile. Linux: the same folder inside the game's Proton prefix.</summary>
    public static string? FindRoot(string? configured, string? steamLibraryOfGame = null)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return configured;
        if (OperatingSystem.IsWindows())
        {
            string p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "kingdomcome", "saves");
            return Directory.Exists(p) ? p : null;
        }
        // <library>/steamapps/common/KingdomComeDeliverance  ->  <library>/steamapps/compatdata/379430/pfx/drive_c/users/steamuser/Saved Games/kingdomcome/saves
        if (steamLibraryOfGame is { Length: > 0 })
        {
            var steamapps = System.IO.Path.GetFullPath(System.IO.Path.Combine(steamLibraryOfGame, "..", ".."));
            string p = System.IO.Path.Combine(steamapps, "compatdata", GameLocator.SteamAppId, "pfx", "drive_c", "users", "steamuser", "Saved Games", "kingdomcome", "saves");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    public static string DefaultBackupRoot() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCDUS", "world-backups");

    public string PlaylineDir(int playline) => System.IO.Path.Combine(Root, "playline" + playline);

    public IEnumerable<int> Playlines()
    {
        if (!Directory.Exists(Root)) yield break;
        foreach (var d in Directory.EnumerateDirectories(Root))
        {
            var m = Regex.Match(System.IO.Path.GetFileName(d), @"^playline(\d+)$");
            if (m.Success) yield return int.Parse(m.Groups[1].Value);
        }
    }

    /// <summary>The playline's saves, newest first (by the time written inside the save, which is what the game lists by).</summary>
    public List<SaveFile> Saves(int playline)
    {
        var dir = PlaylineDir(playline);
        var list = new List<SaveFile>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.EnumerateFiles(dir, "*.whs"))
            if (SaveInfo.Read(f) is { } info) list.Add(new SaveFile(f, playline, info, File.GetLastWriteTimeUtc(f)));
        list.Sort((a, b) => b.Info.SavedUnix != a.Info.SavedUnix ? b.Info.SavedUnix.CompareTo(a.Info.SavedUnix) : b.ModifiedUtc.CompareTo(a.ModifiedUtc));
        return list;
    }

    public SaveFile? Newest(int playline) => Saves(playline).FirstOrDefault();

    /// <summary>The highest slot that holds no save and is not in <paramref name="taken"/> (a slot a shared world already uses), or null when all five are in use.</summary>
    public int? FreePlayline(IEnumerable<int> taken)
    {
        var t = taken.ToHashSet();
        for (int i = MaxPlaylines - 1; i >= 0; i--)
            if (!t.Contains(i) && (!Directory.Exists(PlaylineDir(i)) || !Directory.EnumerateFiles(PlaylineDir(i), "*.whs").Any())) return i;
        return null;
    }

    /// <summary>The most recently saved game anywhere (what the game's Continue would load).</summary>
    public SaveFile? NewestAny() => Playlines().Select(Newest).Where(s => s is not null).OrderByDescending(s => s!.Info.SavedUnix).FirstOrDefault();

    /// <summary>The newest save written after <paramref name="sinceUtc"/> in any playline (what a "save now" produced), or null.</summary>
    public SaveFile? NewestSince(DateTime sinceUtc)
    {
        SaveFile? best = null;
        foreach (var p in Playlines())
        {
            var dir = PlaylineDir(p);
            foreach (var f in Directory.EnumerateFiles(dir, "*.whs"))
            {
                var mt = File.GetLastWriteTimeUtc(f);
                if (mt < sinceUtc || (best is not null && mt <= best.ModifiedUtc)) continue;
                if (SaveInfo.Read(f) is { } info) best = new SaveFile(f, p, info, mt);
            }
        }
        return best;
    }

    /// <summary>
    /// Puts a world a friend sent into <paramref name="playline"/> as its only save. A slot that already holds saves is refused unless
    /// <paramref name="replace"/> says it is the shared world's own slot; what was there is moved to the backup folder first, so a world that lost the
    /// comparison is never lost. The write is a temporary file and a rename, so the game never sees half a save.
    /// </summary>
    public string InstallWorld(int playline, string worldId, byte[] file, string stamp, bool replace)
    {
        if (playline < 0 || playline >= MaxPlaylines) throw new ArgumentOutOfRangeException(nameof(playline), "the game has playlines 0 to " + (MaxPlaylines - 1));
        if (!SaveInfo.Validate(file, out var why)) throw new InvalidDataException("not a whole save: " + why);
        var dir = PlaylineDir(playline);
        if (!replace && Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.whs").Any()) throw new InvalidOperationException("playline " + playline + " holds a game of the player's");
        Directory.CreateDirectory(dir);
        var backup = System.IO.Path.Combine(BackupRoot, Safe(worldId), stamp);
        foreach (var old in Directory.EnumerateFiles(dir, "*.whs").ToList())
        {
            Directory.CreateDirectory(backup);
            File.Move(old, System.IO.Path.Combine(backup, System.IO.Path.GetFileName(old)), overwrite: true);
        }
        string target = System.IO.Path.Combine(dir, "world.whs");
        string tmp = target + ".part";
        File.WriteAllBytes(tmp, file);
        File.Move(tmp, target, overwrite: true);
        return target;
    }

    private static string Safe(string s) => Regex.Replace(s, @"[^A-Za-z0-9_-]", "_");
}
