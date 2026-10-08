// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text;
using Coop.Contract;
using KcdUs.Wire;

namespace KcdUs.Agent;

/// <summary>
/// What this installation tells a room about itself: the exact mod payload (the Lua pak), the agent, the game's engine, the DLC/mod content, and an HONEST capability table.
/// A capability is claimed only at the level its evidence reaches (docs/CAPABILITIES.md); anything not proved is absent or a candidate.
/// </summary>
public static class HandshakeFactory
{
    public const string GameId = "kcd1";

    public sealed record Fingerprint(string Agent, string Lua, string Native, string Engine, string Content, IReadOnlyList<string>? Dlc = null);

    private static string FileHash(string? path)
    {
        try
        {
            if (path is null || !File.Exists(path)) return "";
            using var s = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>
    /// The DLC that is active and affects saves, as the game itself logged it at startup (kcd.log, "DLC list:"). null while the game has not logged it yet.
    /// A save names the DLC it needs and the engine refuses to load it without them, so this decides who can load whose world.
    /// </summary>
    public static IReadOnlyList<string>? ReadDlc(string? gameDir)
    {
        if (gameDir is null) return null;
        try
        {
            var log = Path.Combine(gameDir, "kcd.log");
            if (!File.Exists(log)) return null;
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var rd = new StreamReader(fs, Encoding.UTF8);
            var lines = new List<string>();
            string? line;
            while ((line = rd.ReadLine()) is not null) lines.Add(line);
            return DlcLog.ActiveSaveAffecting(lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The OTHER MODS installed with the game, as one hash: two computers with different mods must not share authority over a world. (DLC is compared by name, not by this hash.)</summary>
    public static string ContentProfile(string gameDir)
    {
        var sb = new StringBuilder();
        try
        {
            var mods = Path.Combine(gameDir, "Mods");
            if (Directory.Exists(mods))
                foreach (var d in Directory.EnumerateDirectories(mods).Select(Path.GetFileName).Where(n => !string.Equals(n, "kcdus", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    sb.Append("M:").Append(d!.ToLowerInvariant()).Append('\n');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
        // an install with no other mods still has a profile (of nothing): "" would mean "unknown" and hide a real difference
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public static Fingerprint Measure(string? gameDir, string? nativeDll = null)
    {
        string agent = FileHash(Environment.ProcessPath);
        if (gameDir is null) return new Fingerprint(agent, "", FileHash(nativeDll), "", "");
        return new Fingerprint(agent,
            FileHash(Path.Combine(gameDir, "Mods", "kcdus", "Data", "kcdus.pak")),
            FileHash(nativeDll),
            FileHash(Path.Combine(gameDir, "Bin", "Win64", "WHGame.dll")),
            ContentProfile(gameDir), ReadDlc(gameDir));
    }

    /// <summary>
    /// The capability table of THIS build, by its evidence (docs/CAPABILITIES.md). <paramref name="adapterLoaded"/>: the game itself said its startup adapter is in the process.
    /// Authority over NPCs, combat, loot and quests is NOT claimed: the engine entry paths have not been proved (docs/ENGINE-INTEGRATION-20261007.md).
    /// </summary>
    public static IReadOnlyDictionary<string, CapabilityLevel> Capabilities(bool adapterLoaded, bool supportedEngine) => new Dictionary<string, CapabilityLevel>
    {
        [CapabilityNames.PresenceBodies] = CapabilityLevel.EngineVerified,
        [CapabilityNames.Locomotion] = CapabilityLevel.EngineVerified,
        [CapabilityNames.Outfits] = adapterLoaded && supportedEngine ? CapabilityLevel.EngineVerified : CapabilityLevel.Absent,
        [CapabilityNames.PersonalCharacter] = supportedEngine ? CapabilityLevel.EngineVerified : CapabilityLevel.Candidate,
        [CapabilityNames.ParticipantIdentity] = CapabilityLevel.IntegrationVerified,
        [CapabilityNames.CheckpointBarrier] = CapabilityLevel.Candidate,
        [CapabilityNames.AuthorityNpc] = adapterLoaded && supportedEngine ? CapabilityLevel.Candidate : CapabilityLevel.Absent,
        [CapabilityNames.AuthorityQuest] = CapabilityLevel.Candidate, // the host's quest progress follows one way, on by default in a shared world; one objective binding proved, rewards/spawns unproved
        // Shared OUTCOMES (not shared AI): proved in a private real engine with a synthetic second player (tools/engine/shared_outcomes_live*.py):
        // health damage and death through the ordinary damage path with readback, corpse and stash inventories read, a guest's take decided by the host and taken back on "gone".
        // Two real computers have not played it: it is not integration-verified, and where the NPCs walk and whom they target is NOT shared.
        [CapabilityNames.AuthorityCombat] = CapabilityLevel.EngineVerified,
        [CapabilityNames.AuthorityLoot] = CapabilityLevel.EngineVerified,
    };

    public static RoomHandshake Build(string release, Fingerprint f, bool adapterLoaded)
    {
        bool supported = f.Engine.Equals(EngineGameStart.SupportedHash, StringComparison.OrdinalIgnoreCase);
        return new RoomHandshake(GameId, release, Proto.ProtocolVersion, RoomHandshake.CurrentContractVersion, f.Agent, f.Lua, f.Native, f.Engine, f.Content, Capabilities(adapterLoaded, supported), f.Dlc);
    }
}
