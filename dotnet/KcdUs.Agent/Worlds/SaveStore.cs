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

    /// <summary>
    /// Windows: Saved Games under the profile. Linux: the same folder inside the game's Proton prefix, in whichever Steam library holds it. The game
    /// names its folder "KingdomCome" and Windows ignores case, but a Linux file system does not: an exact-case lookup missed the folder the game made
    /// (seen with a Linux player: shared worlds off, or a second empty folder the game never read). Every step of the path is matched ignoring case.
    /// </summary>
    public static string? FindRoot(string? configured, string? steamLibraryOfGame = null, IEnumerable<string>? libraries = null)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return configured;
        if (OperatingSystem.IsWindows())
            return InProfile(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var prefixes = new List<string>();
        void Add(string steamapps)
        {
            string user = System.IO.Path.Combine(steamapps, "compatdata", GameLocator.SteamAppId, "pfx", "drive_c", "users", "steamuser");
            if (Directory.Exists(user) && !prefixes.Contains(user)) prefixes.Add(user);
        }
        // <library>/steamapps/common/KingdomComeDeliverance  ->  <library>/steamapps/compatdata/379430/pfx/drive_c/users/steamuser/Saved Games/KingdomCome/saves
        if (steamLibraryOfGame is { Length: > 0 }) Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(steamLibraryOfGame, "..", "..")));
        foreach (var lib in libraries ?? SafeLibraries()) Add(System.IO.Path.Combine(lib, "steamapps"));
        // the prefix that already has the game's own folder wins; a prefix with none gets one
        foreach (var user in prefixes)
            if (ResolveIgnoringCase(System.IO.Path.Combine(user, "Saved Games", "kingdomcome")) is not null) return InProfile(user);
        foreach (var user in prefixes)
            if (InProfile(user) is { } made) return made;
        return null;
    }

    private static IEnumerable<string> SafeLibraries()
    {
        try { return GameLocator.Libraries().ToList(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    /// <summary>
    /// <c>&lt;profile&gt;/Saved Games/kingdomcome/saves</c> as it is on disk (any case). Where two folders differ only in case (an older agent made the second),
    /// the one holding the game's files is used and an EMPTY stray one is removed. The saves folder is created when the game's user folder (or at least
    /// "Saved Games") exists but holds none yet: the game makes "saves" only at its first save, so a player who never saved could not receive a world.
    /// </summary>
    public static string? InProfile(string profile)
    {
        if (!Directory.Exists(profile)) return null;
        string? savedGames = ResolveIgnoringCase(System.IO.Path.Combine(profile, "Saved Games"));
        if (savedGames is null) return null;
        var users = Matches(savedGames, "kingdomcome");
        string? user = users.OrderByDescending(Weight).FirstOrDefault();
        foreach (var stray in users.Where(u => u != user && IsEmptyTree(u)))
            try { Directory.Delete(stray, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        user ??= System.IO.Path.Combine(savedGames, "KingdomCome");
        string saves = Matches(user, "saves").OrderByDescending(Weight).FirstOrDefault() ?? System.IO.Path.Combine(user, "saves");
        if (Directory.Exists(saves)) return saves;
        try { Directory.CreateDirectory(saves); return saves; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The kept name for the tests and older callers: the saves folder at this exact path, made when its profile exists.</summary>
    public static string? Existing(string saves)
    {
        if (Directory.Exists(saves)) return saves;
        var user = System.IO.Path.GetDirectoryName(saves);
        var profile = user is null ? null : System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(user)!);
        return profile is null ? null : InProfile(profile);
    }

    /// <summary>The path as it is on disk, each part matched ignoring case (the first match that exists); null when some part is missing.</summary>
    public static string? ResolveIgnoringCase(string path)
    {
        if (Directory.Exists(path)) return path;
        string full = System.IO.Path.GetFullPath(path);
        string? root = System.IO.Path.GetPathRoot(full);
        if (root is null) return null;
        string cur = root;
        foreach (var part in full[root.Length..].Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            string next = System.IO.Path.Combine(cur, part);
            if (!Directory.Exists(next))
            {
                var m = Matches(cur, part).OrderByDescending(Weight).FirstOrDefault();
                if (m is null) return null;
                next = m;
            }
            cur = next;
        }
        return cur;
    }

    private static List<string> Matches(string dir, string name)
    {
        try { return Directory.Exists(dir) ? Directory.EnumerateDirectories(dir).Where(d => string.Equals(System.IO.Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase)).ToList() : new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(); }
    }

    /// <summary>How much of the game is in a folder: files count, a save counts most (the folder the game itself writes to).</summary>
    private static long Weight(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => f.EndsWith(".whs", StringComparison.OrdinalIgnoreCase) ? 1000L : 1L); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static bool IsEmptyTree(string dir)
    {
        try { return !Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public static string DefaultBackupRoot() =>
        System.IO.Path.Combine(AgentPaths.Data, "world-backups");

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

    public sealed record FileVersion(long Length, DateTime ModifiedUtc);
    public Dictionary<string, FileVersion> FileVersions()
    {
        var files = new Dictionary<string, FileVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in Playlines().Where(p => p >= 0 && p < MaxPlaylines))
            foreach (var file in Directory.EnumerateFiles(PlaylineDir(slot), "*.whs"))
            {
                var info = new FileInfo(file);
                files.Add(file, new(info.Length, info.LastWriteTimeUtc));
            }
        return files;
    }
    public List<SaveFile> ChangedFiles(Dictionary<string, FileVersion> before)
    {
        var result = new List<SaveFile>();
        foreach (var (file, version) in FileVersions())
        {
            if (before.TryGetValue(file, out var old) && old == version) continue;
            string folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file))!;
            if (int.TryParse(folder[8..], out int slot) && SaveInfo.Read(file) is { } info)
                result.Add(new(file, slot, info, version.ModifiedUtc));
        }
        return result;
    }

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
        if (playline < 0 || playline >= MaxPlaylines) throw new ArgumentOutOfRangeException(nameof(playline));
        return new SaveInstallTransaction(Root, BackupRoot).Install(playline, worldId, file, replace);
    }

    public void RecoverPendingInstalls() => new SaveInstallTransaction(Root, BackupRoot).Recover();

    private static string Safe(string s) => Regex.Replace(s, @"[^A-Za-z0-9_-]", "_");
}
