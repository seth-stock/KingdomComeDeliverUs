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

    public sealed record Fingerprint(string Agent, string Lua, string Native, string Engine, string Content);

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

    /// <summary>The DLC and other mods installed with the game, as one hash: another computer with other DLC or other mods must not share authority over a world.</summary>
    public static string ContentProfile(string gameDir)
    {
        var sb = new StringBuilder();
        try
        {
            var data = Path.Combine(gameDir, "Data");
            if (Directory.Exists(data))
                foreach (var f in Directory.EnumerateFiles(data, "*.pak").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    sb.Append("D:").Append(Path.GetFileName(f).ToLowerInvariant()).Append(':').Append(new FileInfo(f).Length).Append('\n');
            var mods = Path.Combine(gameDir, "Mods");
            if (Directory.Exists(mods))
                foreach (var d in Directory.EnumerateDirectories(mods).Select(Path.GetFileName).Where(n => !string.Equals(n, "kcdus", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    sb.Append("M:").Append(d!.ToLowerInvariant()).Append('\n');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
        // an install with no extra DLC or mods still has a profile (of nothing): "" would mean "unknown" and hide a real difference
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
            ContentProfile(gameDir));
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
    };

    public static RoomHandshake Build(string release, Fingerprint f, bool adapterLoaded)
    {
        bool supported = f.Engine.Equals(EngineGameStart.SupportedHash, StringComparison.OrdinalIgnoreCase);
        return new RoomHandshake(GameId, release, Proto.ProtocolVersion, RoomHandshake.CurrentContractVersion, f.Agent, f.Lua, f.Native, f.Engine, f.Content, Capabilities(adapterLoaded, supported));
    }
}
