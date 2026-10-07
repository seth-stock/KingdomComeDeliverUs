// SPDX-License-Identifier: GPL-3.0-only
// The shared multiplayer contract. The SAME source file is in both games' repositories (KcdUs.Protocol and KcdMp.Protocol); only the
// adapters (engine, save format, relay) differ. Shared test vectors keep the two copies from drifting apart.
using System.Globalization;
using System.Text;

namespace Coop.Contract;

/// <summary>How far a capability has been proved. Source code, a script reference or a successful call is at most <see cref="Candidate"/>.</summary>
public enum CapabilityLevel
{
    Absent = 0,
    /// <summary>Written or located, not proved.</summary>
    Candidate = 1,
    /// <summary>Proved in a private real engine (readback and gameplay effect).</summary>
    EngineVerified = 2,
    /// <summary>Proved end to end through the production agents, relay and save paths on the packaged bytes.</summary>
    IntegrationVerified = 3,
    /// <summary>Accepted by people in a two- or four-computer session.</summary>
    HumanAccepted = 4,
}

public static class CapabilityNames
{
    public const string PresenceBodies = "presence.bodies";
    public const string Locomotion = "presence.locomotion";
    public const string Outfits = "presence.outfits";
    public const string PersonalCharacter = "character.personal";
    public const string CharacterTimers = "character.timers";
    public const string ParticipantIdentity = "identity.participant";
    public const string CheckpointBarrier = "checkpoint.barrier";
    public const string AuthorityNpc = "authority.npc";
    public const string AuthorityCombat = "authority.combat";
    public const string AuthorityLoot = "authority.loot";
    public const string AuthorityQuest = "authority.quest";

    /// <summary>A room is a full shared simulation only when every one of these is proved on both sides.</summary>
    public static readonly IReadOnlyList<string> SharedSimulationRequired = new[]
    {
        AuthorityNpc, AuthorityCombat, AuthorityLoot, AuthorityQuest, CheckpointBarrier, ParticipantIdentity, PersonalCharacter,
    };

    /// <summary>The authority capabilities: any of them above Candidate makes a room <see cref="RoomMode.Partial"/> instead of presence-only.</summary>
    public static readonly IReadOnlyList<string> Authority = new[] { AuthorityNpc, AuthorityCombat, AuthorityLoot, AuthorityQuest };
}

/// <summary>What a room is, said plainly: never "shared" for a room that only shows peers.</summary>
public enum RoomMode
{
    Refused,
    /// <summary>Peers are shown; no authoritative NPC/combat/loot/quest history is proved.</summary>
    Presence,
    /// <summary>Some authority features are engine-verified, but not every required capability is integration-verified.</summary>
    Partial,
    /// <summary>Every required capability is integration-verified on both sides.</summary>
    SharedSimulation,
}

/// <summary>
/// What one side tells the other before it is admitted. One compact text field (no '|'), so it travels inside the games' existing '|'-separated Hello.
/// Hashes are lower-case hex SHA-256 (or empty when unknown).
/// </summary>
public sealed record RoomHandshake(
    string GameId, string ProductVersion, int WireVersion, int ContractVersion,
    string AgentHash, string LuaHash, string NativeHash, string EngineHash, string ContentProfileHash,
    IReadOnlyDictionary<string, CapabilityLevel> Capabilities)
{
    public const int CurrentContractVersion = 1;
    public const int MaxEncodedLength = 1500;

    public CapabilityLevel Level(string capability) => Capabilities.TryGetValue(capability, out var l) ? l : CapabilityLevel.Absent;

    private static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') sb.Append(c);
        return sb.ToString();
    }

    public string Encode()
    {
        var sb = new StringBuilder("c1");
        void F(string k, string v) => sb.Append(';').Append(k).Append('=').Append(Clean(v));
        F("g", GameId); F("p", ProductVersion);
        F("w", WireVersion.ToString(CultureInfo.InvariantCulture)); F("cv", ContractVersion.ToString(CultureInfo.InvariantCulture));
        F("a", AgentHash); F("l", LuaHash); F("n", NativeHash); F("e", EngineHash); F("x", ContentProfileHash);
        sb.Append(";k=");
        bool first = true;
        foreach (var kv in Capabilities.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (kv.Value == CapabilityLevel.Absent) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append(Clean(kv.Key)).Append(':').Append(((int)kv.Value).ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>Parses <see cref="Encode"/>; null for anything malformed, oversized or of another format.</summary>
    public static RoomHandshake? TryDecode(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxEncodedLength || !text.StartsWith("c1;", StringComparison.Ordinal)) return null;
        var kv = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in text.Split(';').Skip(1))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || !kv.TryAdd(part[..eq], part[(eq + 1)..])) return null;
        }
        string Get(string k) => kv.TryGetValue(k, out var v) ? v : "";
        if (!int.TryParse(Get("w"), NumberStyles.None, CultureInfo.InvariantCulture, out int wire)) return null;
        if (!int.TryParse(Get("cv"), NumberStyles.None, CultureInfo.InvariantCulture, out int cv)) return null;
        if (Get("g").Length == 0 || Get("p").Length == 0) return null;
        foreach (var h in new[] { "a", "l", "n", "e", "x" })
            if (Get(h).Length is not (0 or 64) || Get(h).Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f'))) return null;
        var caps = new Dictionary<string, CapabilityLevel>(StringComparer.Ordinal);
        foreach (var entry in Get("k").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = entry.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(entry[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int lv) || lv is < 0 or > 4) return null;
            if (!caps.TryAdd(entry[..colon], (CapabilityLevel)lv)) return null;
        }
        return new RoomHandshake(Get("g"), Get("p"), wire, cv, Get("a"), Get("l"), Get("n"), Get("e"), Get("x"), caps);
    }
}

public sealed record RoomPolicy(bool AllowUnverifiedPayload = false);

public sealed record NegotiationResult(RoomMode Mode, IReadOnlyList<string> Refusals, IReadOnlyList<string> Missing, IReadOnlyDictionary<string, CapabilityLevel> Effective, IReadOnlyList<string>? Warnings = null)
{
    public IReadOnlyList<string> Notes => Warnings ?? Array.Empty<string>();
    public bool Admitted => Mode != RoomMode.Refused;
    public string Describe() => Mode switch
    {
        RoomMode.Refused => "refused: " + string.Join("; ", Refusals),
        RoomMode.Presence => "presence only (peers are shown; shared NPC, combat, loot and quest authority is NOT active: " + string.Join(", ", Missing) + ")",
        RoomMode.Partial => "partly shared (not every authority capability is verified: " + string.Join(", ", Missing) + ")",
        _ => "shared simulation",
    };
}

public static class Negotiation
{
    /// <summary>
    /// Decides whether two sides may share a room and what the room honestly is. Refusals: another game, another contract/wire version, a different Lua payload
    /// or an unverifiable payload. Native/engine differences are not refusals: a capability counts only at the LOWER of the two sides' levels. A different content profile
    /// (DLC or other mods) is not a refusal for a presence room, but it keeps the room from being <see cref="RoomMode.Partial"/> or shared: authority over a world that the two
    /// computers load differently cannot be trusted.
    /// </summary>
    public static NegotiationResult Negotiate(RoomHandshake local, RoomHandshake remote, RoomPolicy? policy = null)
    {
        policy ??= new RoomPolicy();
        var refusals = new List<string>();
        if (!string.Equals(local.GameId, remote.GameId, StringComparison.Ordinal)) refusals.Add($"different games ({local.GameId} and {remote.GameId})");
        if (local.ContractVersion != remote.ContractVersion) refusals.Add($"contract version {local.ContractVersion} and {remote.ContractVersion}");
        if (local.WireVersion != remote.WireVersion) refusals.Add($"wire version {local.WireVersion} and {remote.WireVersion}");
        if (local.LuaHash.Length == 0 || remote.LuaHash.Length == 0)
        {
            if (!policy.AllowUnverifiedPayload) refusals.Add("the game's mod payload could not be verified on " + (local.LuaHash.Length == 0 ? "this" : "the other") + " computer");
        }
        else if (local.LuaHash != remote.LuaHash) refusals.Add("different mod payloads (the Lua packages differ): both must install the same build");
        var warnings = new List<string>();
        bool contentDiffers = local.ContentProfileHash.Length > 0 && remote.ContentProfileHash.Length > 0 && local.ContentProfileHash != remote.ContentProfileHash;
        if (contentDiffers) warnings.Add("different game content (DLC or other mods differ): shared authority is off, peers are still shown");

        var effective = new Dictionary<string, CapabilityLevel>(StringComparer.Ordinal);
        foreach (var name in local.Capabilities.Keys.Union(remote.Capabilities.Keys, StringComparer.Ordinal))
            effective[name] = (CapabilityLevel)Math.Min((int)local.Level(name), (int)remote.Level(name));

        var missing = CapabilityNames.SharedSimulationRequired
            .Where(n => !effective.TryGetValue(n, out var l) || l < CapabilityLevel.IntegrationVerified).ToList();

        RoomMode mode;
        if (refusals.Count > 0) mode = RoomMode.Refused;
        else if (contentDiffers) mode = RoomMode.Presence;
        else if (missing.Count == 0) mode = RoomMode.SharedSimulation;
        else if (CapabilityNames.Authority.Any(n => effective.TryGetValue(n, out var l) && l >= CapabilityLevel.EngineVerified)) mode = RoomMode.Partial;
        else mode = RoomMode.Presence;
        return new NegotiationResult(mode, refusals, missing, effective, warnings);
    }
}
