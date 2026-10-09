// SPDX-License-Identifier: GPL-3.0-only
// Shared fights and loot (docs/CAPABILITIES.md, "Combat" and "Loot"): the agent's half of mod/kcdus/lua/combat.lua and loot.lua.
//
// It is ON only while it is honest: the option is on, this player is in a world, the relay is up, and a friend is in the SAME shared world (each machine tells the
// others which world it plays; two copies of one save have the same named NPCs, stashes and bodies, two different saves do not). Then
//   * damage and deaths the local player causes to NPCs go to the friends, and theirs are applied to this copy (health, additive and order-free: nobody is "the" authority over health);
//   * loot is decided by the HOST: a guest's take is an ask, the host answers ok or gone, and the host's answer is written to a durable, hash-chained journal first,
//     so a repeated ask gets the same answer and an interrupted one is never replayed as a second grant (OperationJournal, shared with the KCD2 mod).
using System.Globalization;
using System.Text.RegularExpressions;
using Coop.Contract;
using KcdUs.Wire;

namespace KcdUs.Agent;

public static class OutcomesRules
{
    private static readonly Regex Name = new(@"^[A-Za-z0-9_.\-:@]{3,80}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Class = new(@"^[0-9a-fA-F\-]{8,40}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Nonce = new(@"^[0-9a-f]{8}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool ValidName(string s) => Name.IsMatch(s) && !s.Contains("kcdus_", StringComparison.Ordinal);
    public static bool ValidClass(string s) => Class.IsMatch(s);
    public static bool ValidNonce(string s) => Nonce.IsMatch(s);
    public static bool ValidScope(string s) => s.Length is >= 1 and <= 64 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    public static bool Number(string s, double min, double max, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= min && v <= max;
    public static bool Count(string s, out int n) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n is >= 1 and <= 100000;
}

public sealed class OutcomesCoordinator : IDisposable
{
    public const int MaxCombatPerSecond = 40;

    private readonly Func<long> _now;
    private readonly Action<string> _toGame, _toPeers, _toGuests, _notify, _log;
    private readonly Func<int, string> _nameOf;
    private readonly Func<OperationJournal?> _journal;
    private readonly object _gate = new();
    private readonly Dictionary<int, string> _peerScopes = new();
    private bool _on;
    private string _scope = "", _announced = "";
    private long _lastAnnounceMs, _windowStart;
    private int _windowCount;

    public OutcomesCoordinator(Func<long> now, Action<string> toGame, Action<string> toPeers, Action<string> toGuests, Action<string> notify, Action<string> log,
        Func<int, string> nameOf, Func<OperationJournal?> journal)
    {
        _now = now; _toGame = toGame; _toPeers = toPeers; _toGuests = toGuests; _notify = notify; _log = log; _nameOf = nameOf; _journal = journal;
    }

    /// <summary>The player's option (default on).</summary>
    public bool Enabled { get; private set; } = true;
    public bool Active { get { lock (_gate) return _on; } }
    public string Scope { get { lock (_gate) return _on ? _scope : ""; } }

    public void SetEnabled(bool on) { lock (_gate) { Enabled = on; } }

    public void PeerScope(int id, string scope) { lock (_gate) { if (OutcomesRules.ValidScope(scope)) _peerScopes[id] = scope; else _peerScopes.Remove(id); } }
    public void PeerLeft(int id) { lock (_gate) _peerScopes.Remove(id); }

    public void Reset(string why) { lock (_gate) { _peerScopes.Clear(); _announced = ""; Off(why); } }

    private void Off(string why)
    {
        if (!_on) return;
        _on = false;
        _toGame("CMBMODE|0");
        _toGame("LOOTMODE|0");
        _log("shared fights and loot: off (" + why + ")");
    }

    /// <summary>About ten times a second. <paramref name="myScope"/> is the id of the shared world this player has loaded ("" when none).</summary>
    public void Tick(bool inWorld, bool connected, bool isHost, string myScope)
    {
        lock (_gate)
        {
            long now = _now();
            string scope = inWorld && connected && OutcomesRules.ValidScope(myScope) ? myScope : "";
            if (connected && (scope != _announced || (scope.Length > 0 && now - _lastAnnounceMs >= 5000)))
            {
                _announced = scope; _lastAnnounceMs = now;
                _toPeers("scope|" + (scope.Length > 0 ? scope : "-"));
            }
            bool want = Enabled && scope.Length > 0 && _peerScopes.Values.Any(s => s == scope);
            if (want && (!_on || scope != _scope))
            {
                _on = true; _scope = scope;
                _toGame($"CMBMODE|1|{scope}");
                _toGame($"LOOTMODE|1|{scope}|{(isHost ? 1 : 0)}");
                var with = _peerScopes.Where(kv => kv.Value == scope).Select(kv => _nameOf(kv.Key));
                _notify($"Shared fights and loot are on with {string.Join(", ", with)}.");
                _log($"shared fights and loot: on in world {scope}");
            }
            else if (!want && _on)
            {
                Off(!Enabled ? "the option is off" : scope.Length == 0 ? "this player is not in a shared world" : "no friend is in the same world");
                _notify("Shared fights and loot are off.");
            }
        }
    }

    private bool Allow()
    {
        long now = _now();
        if (now - _windowStart >= 1000) { _windowStart = now; _windowCount = 0; }
        return ++_windowCount <= MaxCombatPerSecond;
    }

    // ---------------------------------------------------------------- combat

    /// <summary>The game: KCDUS|CMB|name|damage|health|dead.</summary>
    public void GameCombat(string[] f)
    {
        lock (_gate)
        {
            if (!_on || f.Length < 5 || !OutcomesRules.ValidName(f[1]) || !OutcomesRules.Number(f[2], 0, 5000, out _) || f[4] is not ("0" or "1") || !Allow()) return;
            _toPeers($"cmb|{f[1]}|{f[2]}|{f[4]}");
        }
    }

    /// <summary>A friend's event: cmb|name|damage|dead.</summary>
    public void PeerCombat(int from, string[] f)
    {
        lock (_gate)
        {
            if (!_on || f.Length != 4 || !OutcomesRules.ValidName(f[1]) || !OutcomesRules.Number(f[2], 0, 5000, out _) || f[3] is not ("0" or "1")) return;
            _toGame($"CMBAPPLY|{_scope}|{f[1]}|{f[2]}|{f[3]}");
        }
    }

    // ---------------------------------------------------------------- loot

    /// <summary>The game: KCDUS|LOOT|... (took, ask, res, confirmed, unconfirmed).</summary>
    public void GameLoot(string[] f, bool isHost)
    {
        lock (_gate)
        {
            if (f.Length < 2) return;
            switch (f[1])
            {
                case "took" when _on && isHost && f.Length >= 6 && OutcomesRules.ValidName(f[2]) && OutcomesRules.ValidClass(f[3]) && OutcomesRules.Count(f[4], out _):
                    _toGuests($"loot|took|{f[2]}|{f[3]}|{f[4]}");
                    break;
                case "ask" when _on && !isHost && f.Length >= 8 && OutcomesRules.ValidNonce(f[2]) && OutcomesRules.Count(f[3], out _) && OutcomesRules.ValidName(f[4])
                                && OutcomesRules.ValidClass(f[5]) && OutcomesRules.Count(f[6], out _):
                    _toPeers($"loot|ask|{f[2]}|{f[3]}|{f[4]}|{f[5]}|{f[6]}");
                    break;
                case "res" when _on && isHost && f.Length >= 9 && int.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out int to):
                    FinishAsk(to, f[3], f[4], f[5], f[6], f[7], f[8]);
                    break;
                case "put" or "drop" when _on && f.Length >= 6 && OutcomesRules.ValidName(f[2]) && OutcomesRules.ValidClass(f[3]) && OutcomesRules.Count(f[4], out _)
                                           && OutcomesRules.Number(f[5], 0, 1, out _) && (f[1] == "put" || f[2].StartsWith("g@", StringComparison.Ordinal)):
                    _toPeers($"loot|{f[1]}|{f[2]}|{f[3]}|{f[4]}|{f[5]}");      // a put or a drop cannot duplicate anything: every friend's copy just gains it
                    break;
                case "unconfirmed":
                    _notify("Someone already took that.");
                    break;
            }
        }
    }

    /// <summary>A friend's put or drop: loot|put|id|class|n|health (or drop, on the ground at a g@ position).</summary>
    public void PeerPutDrop(int from, string[] f)
    {
        lock (_gate)
        {
            if (!_on || f.Length != 6 || f[1] is not ("put" or "drop") || !OutcomesRules.ValidName(f[2]) || !OutcomesRules.ValidClass(f[3]) || !OutcomesRules.Count(f[4], out _)
                || !OutcomesRules.Number(f[5], 0, 1, out _) || (f[1] == "drop" && !f[2].StartsWith("g@", StringComparison.Ordinal)) || !Allow()) return;
            _toGame($"{(f[1] == "put" ? "LOOTPUT" : "LOOTDROP")}|{_scope}|{f[2]}|{f[3]}|{f[4]}|{f[5]}");
        }
    }

    private static string OpId(int from, string nonce, string tok) => $"lt:{from}:{nonce}:{tok}";

    /// <summary>A guest's ask at the host: journal it, then let the game decide.</summary>
    public void PeerAsk(int from, string[] f)
    {
        lock (_gate)
        {
            // f = loot|ask|nonce|tok|id|class|n
            if (!_on || f.Length != 7 || f[1] != "ask" || !OutcomesRules.ValidNonce(f[2]) || !OutcomesRules.Count(f[3], out _) || !OutcomesRules.ValidName(f[4]) || !OutcomesRules.ValidClass(f[5]) || !OutcomesRules.Count(f[6], out int n)) return;
            var j = _journal();
            string id = OpId(from, f[2], f[3]);
            string digest = Digest($"ask|{f[4]}|{f[5]}|{n}");
            if (j is null) { _toGame($"LOOTASK|{_scope}|{from}|{f[2]}|{f[3]}|{f[4]}|{f[5]}|{n}"); return; }   // no journal (tests): the game decides, nothing is remembered
            var begin = j.Begin(id, "loot.ask", digest);
            switch (begin.Kind)
            {
                case BeginKind.New:
                    j.Advance(id, OpState.Reserved); j.Advance(id, OpState.IntentRecorded); j.Advance(id, OpState.EngineApplying);
                    _toGame($"LOOTASK|{_scope}|{from}|{f[2]}|{f[3]}|{f[4]}|{f[5]}|{n}");
                    break;
                case BeginKind.Replay:                                // the same ask again: the same answer, nothing applied twice
                    _toGuests($"loot|res|{from}|{f[3]}|{begin.Record.Outcome}|{f[4]}|{f[5]}|{n}");
                    break;
                case BeginKind.Quarantined:                           // interrupted after the game may have applied it: never granted a second time
                    _toGuests($"loot|res|{from}|{f[3]}|gone|{f[4]}|{f[5]}|{n}");
                    break;
                case BeginKind.Conflict: _log($"loot: ask {id} repeated with other contents: ignored"); break;
                case BeginKind.InProgress: break;
            }
        }
    }

    private void FinishAsk(int to, string tok, string verdict, string id, string cls, string n, string hp)
    {
        if (verdict is not ("ok" or "gone" or "none") || !OutcomesRules.ValidName(id) || !OutcomesRules.ValidClass(cls) || !OutcomesRules.Count(n, out _)) return;
        // the nonce is not in the game's answer: find the open operation for this guest and token
        var j = _journal();
        if (j is not null)
        {
            var open = j.All().LastOrDefault(r => !OperationJournal.IsTerminal(r.State) && r.State != OpState.RecoveryRequired && r.OperationId.StartsWith($"lt:{to}:", StringComparison.Ordinal) && r.OperationId.EndsWith(":" + tok, StringComparison.Ordinal));
            if (open is not null)
            {
                j.Advance(open.OperationId, OpState.EngineVerified, "game said " + verdict);
                j.Advance(open.OperationId, OpState.LedgerCommitted, "answer recorded", verdict);
                j.Advance(open.OperationId, OpState.Delivered, "sent to the guest");
                j.Advance(open.OperationId, OpState.RecipientVerified, "the guest reconciles by itself: an unconfirmed take is taken back (loot.lua)");
                j.Advance(open.OperationId, OpState.Complete);
            }
        }
        _toGuests($"loot|res|{to}|{tok}|{verdict}|{id}|{cls}|{n}");
    }

    /// <summary>The host's event at a guest: loot|res|to|tok|verdict|id|class|n, or loot|took|id|class|n.</summary>
    public void HostLoot(string[] f, int myId)
    {
        lock (_gate)
        {
            if (!_on || f.Length < 2) return;
            if (f[1] == "res" && f.Length == 8 && int.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out int to) && to == myId
                && OutcomesRules.Count(f[3], out _) && f[4] is "ok" or "gone" or "none" && OutcomesRules.ValidName(f[5]) && OutcomesRules.ValidClass(f[6]) && OutcomesRules.Count(f[7], out _))
                _toGame($"LOOTRES|{_scope}|{f[3]}|{f[4]}|{f[5]}|{f[6]}|{f[7]}");
            else if (f[1] == "took" && f.Length == 5 && OutcomesRules.ValidName(f[2]) && OutcomesRules.ValidClass(f[3]) && OutcomesRules.Count(f[4], out _))
                _toGame($"LOOTTOOK|{_scope}|{f[2]}|{f[3]}|{f[4]}");
        }
    }

    private static string Digest(string s) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    public void Dispose() { }
}
