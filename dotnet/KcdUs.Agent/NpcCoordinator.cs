// SPDX-License-Identifier: GPL-3.0-only
// Shared enemies (docs/CAPABILITIES.md, "Shared enemies"): one owner per NPC. The game half is mod/kcdus/lua/npcs.lua.
//
//   * Every player's game reports the living NPCs near its player (npcnear). The HOST gives each NPC to the nearest player. A different player takes it over
//     only when they are clearly nearer (SwitchMarginM) for SwitchHoldMs, so an NPC between two players does not flicker between owners. A player whose
//     report is older than StaleMs counts as far away; an NPC nobody reports any more is forgotten.
//   * The owner's game runs the NPC (its brain walks it and picks its fight with that player) and reports its samples (npcst); every other game in the same
//     shared world makes its copy a puppet that follows the samples. The host repeats the ownership every second (an entry the game hears nothing more of
//     lapses after three seconds, and a puppet without samples gets its brain back after four).
//   * Only while shared outcomes are on (the same shared world, OutcomesCoordinator.Active): health and death are shared there, and both must be shared for
//     an owner to make sense.
using System.Globalization;

namespace KcdUs.Agent;

public sealed partial class NpcCoordinator
{
    public const long StaleMs = 4000;
    public const double SwitchMarginM = 4.0;
    public const long SwitchHoldMs = 2000;
    public const long OwnPeriodMs = 1000;
    public const long ModeRepeatMs = 10000;
    public const int MaxNpcs = 96;
    public const int MaxStatesPerSecond = 48; // 96 owned NPCs / 12 rows per packet * 5 Hz = 40; retain bounded burst headroom

    private readonly Func<long> _now;
    private readonly Action<string> _toGame, _toPeers, _toGuests, _log;
    private readonly object _gate = new();
    // host: name -> player -> (distance, when)
    private readonly Dictionary<string, Dictionary<int, (double D, long At)>> _near = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _owner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Who, long Since)> _challenger = new(StringComparer.Ordinal);
    private readonly Dictionary<int, (long Window, int N)> _stateRate = new();
    private bool _on;
    private string _scope = "";
    private int _myId;
    private long _lastOwnMs, _lastModeMs;

    public NpcCoordinator(Func<long> now, Action<string> toGame, Action<string> toPeers, Action<string> toGuests, Action<string> log)
    {
        _now = now; _toGame = toGame; _toPeers = toPeers; _toGuests = toGuests; _log = log;
    }

    public bool Active { get { lock (_gate) return _on; } }
    public IReadOnlyDictionary<string, int> Owners { get { lock (_gate) return new Dictionary<string, int>(_owner); } }
    public int OwnedBy(int id) { lock (_gate) return _owner.Values.Count(v => v == id); }

    /// <summary>About ten times a second. <paramref name="scope"/> is the shared world while shared outcomes are active, else empty.</summary>
    public void Tick(string scope, bool isHost, int myId)
    {
        lock (_gate)
        {
            long now = _now();
            bool want = scope.Length > 0 && myId != 0;
            if (want != _on || (want && (scope != _scope || myId != _myId)))
            {
                _on = want; _scope = want ? scope : ""; _myId = myId;
                _near.Clear(); _owner.Clear(); _challenger.Clear();
                ResetAuthority(isHost);
                _toGame(want ? $"NPCMODE|1|{_scope}|{_myId}" : "NPCMODE|0");
                _lastModeMs = now;
                _log(want ? "shared enemies: on (one owner per NPC near the players)" : "shared enemies: off (every NPC is this game's own again)");
            }
            if (!_on) return;
            if (now - _lastModeMs >= ModeRepeatMs) { _lastModeMs = now; _toGame($"NPCMODE|1|{_scope}|{_myId}"); }   // a loaded world forgets
            if (isHost && now - _lastOwnMs >= OwnPeriodMs) { _lastOwnMs = now; DecideLocked(now); }
        }
    }

    public void Reset(string why) { lock (_gate) { if (_on) { _on = false; _scope = ""; _near.Clear(); _owner.Clear(); _challenger.Clear(); ResetAuthority(false); _toGame("NPCMODE|0"); _log("shared enemies: off (" + why + ")"); } } }

    public void PeerLeft(int id)
    {
        lock (_gate)
        {
            foreach (var m in _near.Values) m.Remove(id);
            foreach (var k in _owner.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList()) _owner.Remove(k);
            _stateRate.Remove(id);
        }
    }

    // ------------------------------------------------------------------ what this game says

    /// <summary>KCDUS|NPCNEAR|name:dist;... (the host keeps its own; a guest tells the host).</summary>
    public void GameNear(string[] f, bool isHost)
    {
        lock (_gate)
        {
            if (!_on || f.Length < 2) return;
            var list = ParseNear(f[1]);
            if (list == null) return;
            if (isHost) NoteNearLocked(_myId, list, _now());
            else _toPeers($"npcnear|{_scope}|{f[1]}");
        }
    }

    /// <summary>KCDUS|NPCST|entries: this game's own NPCs, for the others' puppets.</summary>
    public void GameStates(string[] f)
    {
        lock (_gate)
        {
            if (!_on || f.Length < 2 || !ValidStates(f[1])) return;
            SendStatesV2(f[1]);
        }
    }

    // ------------------------------------------------------------------ what the friends say

    public void PeerNear(int from, string[] f, bool isHost)
    {
        lock (_gate)
        {
            if (!_on || !isHost || f.Length != 3 || f[1] != _scope) return;
            var list = ParseNear(f[2]);
            if (list != null) NoteNearLocked(from, list, _now());
        }
    }

    public void PeerStates(int from, string[] f)
    {
        lock (_gate)
        {
            if (!_on || f.Length != 3 || f[1] != _scope || !ValidStates(f[2])) return;
            long now = _now();
            var r = _stateRate.TryGetValue(from, out var x) && now - x.Window < 1000 ? x : (now, 0);
            if (r.Item2 >= MaxStatesPerSecond) return;
            _stateRate[from] = (r.Item1, r.Item2 + 1);
            _toGame($"NPCSET|{_scope}|{from}|{f[2]}");
        }
    }

    /// <summary>(guest) the host's ownership.</summary>
    public void HostOwners(string[] f)
    {
        lock (_gate)
        {
            if (!_on || f.Length != 3 || f[1] != _scope || !ValidOwners(f[2])) return;
            _toGame($"NPCOWN|{_scope}|{f[2]}");
        }
    }

    // ------------------------------------------------------------------ the host's decision

    private void NoteNearLocked(int who, List<(string Name, double D)> list, long now)
    {
        foreach (var (name, d) in list)
        {
            if (!_near.TryGetValue(name, out var m))
            {
                if (_near.Count >= MaxNpcs * 2) continue;
                _near[name] = m = new Dictionary<int, (double, long)>();
            }
            m[who] = (d, now);
        }
    }

    private void DecideLocked(long now)
    {
        foreach (var name in _near.Keys.ToList())
        {
            var m = _near[name];
            foreach (var who in m.Where(kv => now - kv.Value.At > StaleMs).Select(kv => kv.Key).ToList()) m.Remove(who);
            if (m.Count == 0) { _near.Remove(name); _owner.Remove(name); _challenger.Remove(name); }
        }
        foreach (var (name, m) in _near.OrderBy(kv => kv.Value.Values.Min(v => v.D)).Take(MaxNpcs))
        {
            var best = m.OrderBy(kv => kv.Value.D).ThenBy(kv => kv.Key).First();
            if (!_owner.TryGetValue(name, out int cur) || !m.ContainsKey(cur)) { SetOwner(name,best.Key); _challenger.Remove(name); continue; }
            if (best.Key == cur || best.Value.D + SwitchMarginM >= m[cur].D) { _challenger.Remove(name); continue; }
            if (!_challenger.TryGetValue(name, out var c) || c.Who != best.Key) { _challenger[name] = (best.Key, now); continue; }
            if (now - c.Since >= SwitchHoldMs) { SetOwner(name,best.Key); _challenger.Remove(name); }
        }
        foreach (var name in _owner.Keys.Where(n => !_near.ContainsKey(n)).ToList()) _owner.Remove(name);
        // The union can grow while players travel: keep the supported interest set bounded.
        var supported = _near.OrderBy(kv => kv.Value.Values.Min(v => v.D)).ThenBy(kv=>kv.Key,StringComparer.Ordinal).Take(MaxNpcs).Select(kv=>kv.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var name in _owner.Keys.Where(n=>!supported.Contains(n)).ToArray()) _owner.Remove(name);
        foreach (var name in _terms.Keys.Where(n=>!_owner.ContainsKey(n)).ToArray()) _terms.Remove(name);
        if (_owner.Count == 0) return;
        long revision = ++_ownerRevision;
        foreach (var chunk in _owner.Chunk(24))
        {
            string body = string.Join(";", chunk.Select(kv => FormattableString.Invariant($"{kv.Key}:{kv.Value}:{_terms[kv.Key]}")));
            _toGame(FormattableString.Invariant($"NPCOWN2|{_scope}|{_authority}|{revision}|{body}"));
            _toGuests(FormattableString.Invariant($"npcown2|{_scope}|{_authority}|{revision}|{body}"));
        }
    }

    // ------------------------------------------------------------------ checks (a friend's line is never trusted)

    internal static List<(string, double)>? ParseNear(string s)
    {
        if (s.Length == 0) return new();
        var parts = s.Split(';');
        if (parts.Length > 32) return null;
        var list = new List<(string, double)>();
        foreach (var p in parts)
        {
            int i = p.LastIndexOf(':');
            if (i <= 0 || !OutcomesRules.ValidName(p[..i]) || !OutcomesRules.Number(p[(i + 1)..], 0, 200, out double d)) return null;
            list.Add((p[..i], d));
        }
        return list;
    }

    internal static bool ValidStates(string s)
    {
        if (s.Length is 0 or > 1600) return false;
        var parts = s.Split(';');
        if (parts.Length > 12) return false;
        foreach (var p in parts)
        {
            var v = p.Split(',');
            if (v.Length != 8 || !OutcomesRules.ValidName(v[0]) || v[7] is not ("0" or "1")) return false;
            for (int i = 1; i <= 6; i++) if (!OutcomesRules.Number(v[i], -100000, 100000, out _)) return false;
        }
        return true;
    }

    internal static bool ValidOwners(string s)
    {
        if (s.Length is 0 or > 2400) return false;
        var parts = s.Split(';');
        if (parts.Length > 24) return false;
        foreach (var p in parts)
        {
            int i = p.LastIndexOf(':');
            if (i <= 0 || !OutcomesRules.ValidName(p[..i]) || !int.TryParse(p[(i + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id is < 1 or > 255) return false;
        }
        return true;
    }
}
