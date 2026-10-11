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
    /// <summary>A durable loot operation id (GuestCustody.cs): 128 random bits, lower-case hex.</summary>
    public static bool ValidOp(string s) => s.Length == 32 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
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
        Func<int, string> nameOf, Func<OperationJournal?> journal, GuestCustody? custody = null)
    {
        _now = now; _toGame = toGame; _toPeers = toPeers; _toGuests = toGuests; _notify = notify; _log = log; _nameOf = nameOf; _journal = journal;
        _custody = custody ?? new GuestCustody();
    }

    private readonly GuestCustody _custody;

    /// <summary>The player's option (default on).</summary>
    public bool Enabled { get; private set; } = true;
    public bool Active { get { lock (_gate) return _on; } }
    public string Scope { get { lock (_gate) return _on ? _scope : ""; } }
    public bool SharesWorld(int peer) { lock(_gate)return _on && _peerScopes.TryGetValue(peer,out var scope) && scope==_scope; }

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
            if (!isHost && _luaNonce.Length > 0) RecoverOpenLocked(force: false);
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
                case "ask" when _on && !isHost && f.Length >= 10 && OutcomesRules.ValidNonce(f[2]) && OutcomesRules.Count(f[3], out int tok) && OutcomesRules.ValidName(f[4])
                                && OutcomesRules.ValidClass(f[5]) && OutcomesRules.Count(f[6], out int n) && OutcomesRules.Number(f[7], 0, 1, out double hp)
                                && int.TryParse(f[8], NumberStyles.None, CultureInfo.InvariantCulture, out int price) && price <= 10_000_000
                                && OutcomesRules.Number(f[9], 0, 1e12, out double wt):
                {
                    // journalled on disk BEFORE the host hears of it (GuestCustody.cs): a reload or a crash cannot lose the probation
                    var t = _custody.Record(f[2], tok, f[4], f[5], n, hp, price, wt, _scope);
                    _toPeers($"loot|ask|{t.Op}|{f[4]}|{f[5]}|{f[6]}");
                    break;
                }
                case "kept" or "returned" when !isHost && f.Length >= 4 && OutcomesRules.ValidNonce(f[2]) && OutcomesRules.Count(f[3], out int rtok):
                {
                    var t = _custody.Find(f[2], rtok);
                    string how = f[1] == "returned" && (f.Length < 5 || f[4] != "1") ? "partial" : f[1];
                    if (t != null && _custody.Settle(t.Op, how)) _toPeers($"loot|receipt|{t.Op}|{how}");
                    break;
                }
                case "recovered" when !isHost && f.Length >= 6 && OutcomesRules.ValidOp(f[2]) && f[5] is "kept" or "returned" or "reverted" or "partial":
                    if (_custody.Settle(f[2], f[5])) _toPeers($"loot|receipt|{f[2]}|{f[5]}");
                    _log($"loot recovery: {f[2]} {f[5]} (removed {f[3]}, refunded {f[4]})");
                    break;
                case "res" when _on && isHost && f.Length >= 10 && int.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out int to) && OutcomesRules.ValidOp(f[9]):
                    FinishAsk(to, f[9], f[4], f[5], f[6], f[7], f[8]);
                    break;
                case "restored" when _on && isHost:
                    FinishRestore(f);
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

    private static string OpId(string op) => "lt:" + op;

    /// <summary>A guest's ask at the host (loot|ask|op|holder|class|n): journal it, then let the game decide. The op id is the guest's durable custody id.</summary>
    public void PeerAsk(int from, string[] f)
    {
        lock (_gate)
        {
            if (!_on || f.Length != 6 || f[1] != "ask" || !OutcomesRules.ValidOp(f[2]) || !OutcomesRules.ValidName(f[3]) || !OutcomesRules.ValidClass(f[4]) || !OutcomesRules.Count(f[5], out int n)) return;
            var j = _journal();
            string id = OpId(f[2]);
            string digest = Digest($"ask|{f[3]}|{f[4]}|{n}");
            if (j is null) { _toGame($"LOOTASK|{_scope}|{from}|{f[2]}|1|{f[3]}|{f[4]}|{n}"); return; }   // no journal (tests): the game decides, nothing is remembered
            var begin = j.Begin(id, "loot.ask", digest);
            switch (begin.Kind)
            {
                case BeginKind.New:
                    j.Advance(id, OpState.Reserved); j.Advance(id, OpState.IntentRecorded, $"{f[3]}|{f[4]}|{n}"); j.Advance(id, OpState.EngineApplying, $"{f[3]}|{f[4]}|{n}");
                    _toGame($"LOOTASK|{_scope}|{from}|{f[2]}|1|{f[3]}|{f[4]}|{n}");
                    break;
                case BeginKind.Replay or BeginKind.InProgress when begin.Record.Outcome.Length > 0:   // the same ask again (a guest restarted): the same answer, nothing applied twice
                    _toGuests($"loot|res|{from}|{f[2]}|{begin.Record.Outcome}|{f[3]}|{f[4]}|{n}");
                    break;
                case BeginKind.Quarantined:
                    // interrupted on the host: a recorded decision stands; one interrupted before it was recorded may have touched the engine: never granted again
                    _toGuests($"loot|res|{from}|{f[2]}|{(begin.Record.Outcome is "ok" or "gone" or "none" ? begin.Record.Outcome : "gone")}|{f[3]}|{f[4]}|{n}");
                    break;
                case BeginKind.Conflict: _log($"loot: ask {id} repeated with other contents: ignored"); break;
                case BeginKind.InProgress: break;
            }
        }
    }

    private void FinishAsk(int to, string op, string verdict, string id, string cls, string n, string hp)
    {
        if (verdict is not ("ok" or "gone" or "none") || !OutcomesRules.ValidName(id) || !OutcomesRules.ValidClass(cls) || !OutcomesRules.Count(n, out _)) return;
        var j = _journal();
        if (j is not null && j.Get(OpId(op)) is { } open && open.State == OpState.EngineApplying)
        {
            string detail = $"{id}|{cls}|{n}|{hp}|guest {to}|scope={_scope}";
            j.Advance(OpId(op), OpState.EngineVerified, detail);
            j.Advance(OpId(op), OpState.LedgerCommitted, detail, verdict);
            j.Advance(OpId(op), OpState.Delivered, detail);                       // RecipientVerified only with the guest's receipt (PeerReceipt)
        }
        _toGuests($"loot|res|{to}|{op}|{verdict}|{id}|{cls}|{n}");
    }

    /// <summary>
    /// A guest's receipt (loot|receipt|op|kept|returned|reverted|partial). Completes the host's operation. When the host had said ok but the guest's Henry did not
    /// keep the item (it timed out and gave it back, or its world went back to before the take), the item exists nowhere: the host puts it back into its copy of
    /// the holder and tells the guests to do the same. That is the only way an "ok" is ever undone.
    /// </summary>
    public void PeerReceipt(int from, string[] f)
    {
        lock (_gate)
        {
            if (!_on || f.Length != 4 || !OutcomesRules.ValidOp(f[2]) || f[3] is not ("kept" or "returned" or "reverted" or "partial")) return;
            var j = _journal();
            if (j is null || j.Get(OpId(f[2])) is not { } rec) return;
            var d = rec.Evidence.Split('|');
            if (d.Length<6 || d[4]!=$"guest {from}" || d[5]!="scope="+_scope) return;
            if (f[3] == "kept" && rec.Outcome != "ok") return;
            if (f[3] == "partial")
            {
                if (rec.State == OpState.Delivered) j.Advance(rec.OperationId, OpState.RecoveryRequired, rec.Evidence, rec.Outcome);
                return;
            }
            bool compensate = rec.Outcome == "ok" && f[3] is "returned" or "reverted";
            bool pending = rec.State == OpState.Delivered || rec.State == OpState.RecoveryRequired && rec.Outcome.Length > 0;
            if (compensate && pending)
            {
                if (d.Length < 4 || !OutcomesRules.ValidName(d[0]) || !OutcomesRules.ValidClass(d[1]) || !OutcomesRules.Count(d[2], out _)) return;
                // Keep compensation intent before completing the source receipt. If the
                // process dies here, native recovery evidence remains quarantined.
                string restoreId = "rs:" + f[2];
                var restore = j.Begin(restoreId, "loot.restore", Digest(_scope + "|" + rec.Evidence));
                if (restore.Kind == BeginKind.New)
                {
                    string evidence=rec.Evidence+"|restore-scope="+_scope+"|restore-load="+_luaNonce;
                    j.Advance(restoreId, OpState.Reserved, evidence);
                    j.Advance(restoreId, OpState.IntentRecorded, evidence);
                    j.Advance(restoreId, OpState.EngineApplying, evidence);
                }
                else compensate = false; // An unknown native outcome is never retried.
            }
            if (rec.State == OpState.Delivered)
            {
                j.Advance(rec.OperationId, OpState.RecipientVerified, rec.Evidence + "|receipt " + f[3], rec.Outcome);
                j.Advance(rec.OperationId, OpState.Complete, rec.Evidence + "|receipt " + f[3], rec.Outcome);
            }
            else if (rec.State == OpState.RecoveryRequired && rec.Outcome.Length > 0)
                j.Resolve(rec.OperationId, Observed.Applied, "guest receipt " + f[3] + " after a host restart|" + rec.Evidence, rec.Outcome);
            else compensate = false;                                             // already completed: a repeated receipt changes nothing
            if (!compensate || !_on) return;
            if (d.Length < 4 || !OutcomesRules.ValidName(d[0]) || !OutcomesRules.ValidClass(d[1]) || !OutcomesRules.Count(d[2], out _)) return;
            string hp = OutcomesRules.Number(d[3], 0, 1, out _) ? d[3] : "1.0000";
            _toGame($"LOOTRESTORE|{_scope}|{f[2]}|{d[0]}|{d[1]}|{d[2]}|{hp}|{_luaNonce}");
            _log($"loot: {f[2]} needs native compensation in {d[0]} after guest {from}'s {f[3]} receipt");
        }
    }

    private void FinishRestore(string[] f)
    {
        // A same-load receipt identifies the created native instance. A class
        // count increase alone never completes the host's compensation.
        if(f.Length!=11 || !OutcomesRules.ValidOp(f[2]) || f[3]!=_scope || f[10]!=_luaNonce) return;
        var j=_journal(); var rec=j?.Get("rs:"+f[2]);
        if(j is null || rec is null || rec.State!=OpState.EngineApplying) return;
        var d=rec.Evidence.Split('|');
        if(d.Length!=8 || d[0]!=f[4] || d[1]!=f[5] || d[2]!=f[6] || d[3]!=f[7] ||
            d[5]!="scope="+_scope || d[6]!="restore-scope="+_scope || d[7]!="restore-load="+_luaNonce) return;
        bool identity=f[8].Length==32 && f[8].All(char.IsAsciiHexDigitLower) && f[8]!=new string('0',32);
        if(f[9]!="verified" || !identity)
        {
            j.Advance(rec.OperationId,OpState.RecoveryRequired,rec.Evidence+"|native compensation unverified");
            _log("loot: compensation "+f[2]+" needs recovery; native item custody was not verified"); return;
        }
        string evidence=rec.Evidence+"|native-instance="+f[8];
        j.Advance(rec.OperationId,OpState.EngineVerified,evidence,"restored");
        j.Advance(rec.OperationId,OpState.LedgerCommitted,evidence);
        j.Advance(rec.OperationId,OpState.Delivered,evidence);
        j.Advance(rec.OperationId,OpState.RecipientVerified,evidence);
        j.Advance(rec.OperationId,OpState.Complete,evidence);
        _toGuests($"loot|put|{d[0]}|{d[1]}|{d[2]}|{d[3]}");
        _log("loot: native compensation "+f[2]+" verified as item "+f[8]);
    }

    /// <summary>The host's event at a guest: loot|res|to|tok|verdict|id|class|n, or loot|took|id|class|n.</summary>
    public void HostLoot(string[] f, int myId)
    {
        lock (_gate)
        {
            if (!_on || f.Length < 2) return;
            if (f[1] == "res" && f.Length == 8 && int.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out int to) && to == myId
                && OutcomesRules.ValidOp(f[3]) && f[4] is "ok" or "gone" or "none" && OutcomesRules.ValidName(f[5]) && OutcomesRules.ValidClass(f[6]) && OutcomesRules.Count(f[7], out _))
            {
                var t = _custody.Find(f[3]);
                if (t == null || !_custody.Answer(t.Op, f[4])) return;
                if (t.Nonce == _luaNonce) _toGame($"LOOTRES|{_scope}|{t.Tok}|{f[4]}|{f[5]}|{f[6]}|{f[7]}");
                else Recover(t, f[4]);                                           // a take of an earlier load or process
            }
            else if (f[1] == "put" && f.Length == 6 && OutcomesRules.ValidName(f[2]) && OutcomesRules.ValidClass(f[3]) && OutcomesRules.Count(f[4], out _) && OutcomesRules.Number(f[5], 0, 1, out _))
                _toGame($"LOOTPUT|{_scope}|{f[2]}|{f[3]}|{f[4]}|{f[5]}");      // the host put a granted-but-not-kept item back
            else if (f[1] == "took" && f.Length == 5 && OutcomesRules.ValidName(f[2]) && OutcomesRules.ValidClass(f[3]) && OutcomesRules.Count(f[4], out _))
                _toGame($"LOOTTOOK|{_scope}|{f[2]}|{f[3]}|{f[4]}");
        }
    }

    // ---------------------------------------------------------------- the guest's durable custody (GuestCustody.cs)

    private string _luaNonce = "";
    private long _lastRecoveryMs;
    private int _receiptCursor;

    private void Recover(CustodyTake t, string verdict) =>
        _toGame($"LOOTRECOVER|{_scope}|{t.Op}|{t.Class}|{t.N}|{t.Price}|{t.TakeWt.ToString("0", CultureInfo.InvariantCulture)}|{verdict}");

    /// <summary>(guest) The game's loot mode readback: KCDUS|LOOTMODE|on|scope|guest|nonce|loadWt. A new nonce means a new load: settle what the old ones left open.</summary>
    public void GameLootMode(string[] f, bool isHost)
    {
        lock (_gate)
        {
            if (f.Length < 6 || f[1] != "on" || !OutcomesRules.ValidNonce(f[4])) return;
            _luaNonce = f[4];
            if(isHost)return;
            RecoverOpenLocked(force: true);
        }
    }

    private void RecoverOpenLocked(bool force)
    {
        long now = _now();
        if (!_on || (!force && now - _lastRecoveryMs < 10_000)) return;
        _lastRecoveryMs = now;
        var receipts = _custody.Receipts().Where(r => r.Take.Scope == _scope).ToArray();
        // Bound retries per tick; rotate through old receipts so a large journal cannot starve new ones.
        for (int i = 0; i < Math.Min(32, receipts.Length); i++)
        {
            var r = receipts[(_receiptCursor + i) % receipts.Length];
            _toPeers($"loot|receipt|{r.Take.Op}|{r.How}");
        }
        _receiptCursor = receipts.Length == 0 ? 0 : (_receiptCursor + Math.Min(32, receipts.Length)) % receipts.Length;
        foreach (var (t, verdict, state) in _custody.Open())
        {
            if (t.Scope != _scope || t.Nonce == _luaNonce) continue;               // this load's own asks are the game's (20 s timeout, LOOTRES)
            if (state is OpState.Delivered or OpState.RecipientVerified)
            {
                if (_custody.Settle(t.Op, verdict is "kept" or "returned" or "reverted" ? verdict : "partial"))
                    _toPeers($"loot|receipt|{t.Op}|{verdict}");
                continue;
            }
            if (verdict is "ok" or "gone" or "none") Recover(t, verdict);
            else if (verdict == "unanswered") Recover(t, "gone");                  // the game already gave it back before the answer came
            else _toPeers($"loot|ask|{t.Op}|{t.Holder}|{t.Class}|{t.N}");         // never answered: ask again (the host replays a recorded answer)
        }
    }

    /// <summary>Open custody (diagnostics, tests).</summary>
    public IReadOnlyList<(CustodyTake Take, string Verdict, OpState State)> Custody => _custody.Open();

    private static string Digest(string s) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    public void Dispose() { }
}
