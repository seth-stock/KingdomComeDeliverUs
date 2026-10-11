// SPDX-License-Identifier: GPL-3.0-only
// Durable custody of a guest's takes (docs/CAPABILITIES.md, "Loot and items", Recovery). The game's loot screen has ALREADY moved an item into the guest's Henry
// when the guest asks the host; until the host answers, that item is the guest's only on probation. The game forgets its probation list on every load and
// crash (loot.lua), so the guest's agent keeps it on disk instead, in the hash-chained OperationJournal:
//
//   ask seen       Begin -> Reserved -> IntentRecorded -> EngineApplying -> EngineVerified   (the move happened; the take's detail is the evidence of every record)
//   host answered  LedgerCommitted, outcome ok | gone | none  (or "unanswered" when the game gave it back before an answer came)
//   game settled   Delivered -> RecipientVerified, outcome kept | returned | reverted | partial -> Complete, and a receipt goes to the host
//
// Every operation id is a random 128-bit value made here, so the host can replay the same answer for the same id after either side restarts. An open operation
// of an earlier load is settled by LOOTRECOVER: the game compares the take's world time with the loaded save's (a take after the save is not in it) and keeps or
// returns the item by the durable answer. An operation the host never answered stays open until the host can answer: nothing is guessed.
// Without a file (tests, the agent's file-less guest mode) the same rules run in memory, and a restart forgets them.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coop.Contract;

namespace KcdUs.Agent;

public sealed record CustodyTake(string Op, string Nonce, int Tok, string Holder, string Class, int N, double Hp, int Price, double TakeWt, string Scope)
{
    public string Json() => JsonSerializer.Serialize(this);
    public static CustodyTake? Parse(string s) { try { return JsonSerializer.Deserialize<CustodyTake>(s); } catch (JsonException) { return null; } }
}

public sealed class GuestCustody : IDisposable
{
    public const string Kind = "guest-take";
    private readonly OperationJournal? _j;
    private readonly Dictionary<string, (CustodyTake Take, string Verdict, OpState State)> _mem = new(StringComparer.Ordinal);
    private readonly Func<string> _newId;
    private readonly object _gate = new();

    public GuestCustody(string path = "", Func<string>? newId = null)
    {
        _newId = newId ?? (() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
        if (path.Length > 0) _j = new OperationJournal(path);
    }

    public bool Durable => _j != null;

    private static string Digest(CustodyTake t) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{t.Holder}|{t.Class}|{t.N}|{t.Price}|{t.TakeWt:0}|{t.Scope}"))).ToLowerInvariant();

    private static string OpKey(string op) => "gt:" + op;

    /// <summary>The game reported a take and asked the host. Journals it before anything is sent; returns the new take with its operation id.</summary>
    public CustodyTake Record(string nonce, int tok, string holder, string cls, int n, double hp, int price, double takeWt, string scope)
    {
        lock (_gate)
        {
            var existing = _j == null
                ? _mem.Values.Select(v => v.Take).FirstOrDefault(t => t.Nonce == nonce && t.Tok == tok && t.Scope == scope)
                : _j.All().Where(r => r.Kind == Kind).Select(r => CustodyTake.Parse(r.Evidence))
                    .FirstOrDefault(t => t != null && t.Nonce == nonce && t.Tok == tok && t.Scope == scope);
            var t = new CustodyTake(existing?.Op ?? _newId(), nonce, tok, holder, cls, n, hp, price, takeWt, scope);
            if (existing != null)
            {
                if (existing != t) throw new InvalidDataException("Conflicting custody take.");
                return existing;
            }
            if (_j == null) { _mem[t.Op] = (t, "", OpState.EngineVerified); return t; }
            string id = OpKey(t.Op), detail = t.Json();
            _j.Begin(id, Kind, Digest(t));
            foreach (var s in new[] { OpState.Reserved, OpState.IntentRecorded, OpState.EngineApplying, OpState.EngineVerified }) _j.Advance(id, s, detail);
            return t;
        }
    }

    /// <summary>The open takes: everything not yet Complete, with the host's answer when it has one.</summary>
    public IReadOnlyList<(CustodyTake Take, string Verdict, OpState State)> Open()
    {
        lock (_gate)
        {
            if (_j == null) return _mem.Values.Where(v => v.State != OpState.Complete && v.State != OpState.RecoveryRequired).ToList();
            var list = new List<(CustodyTake, string, OpState)>();
            foreach (var r in _j.All())
            {
                if (r.Kind != Kind || OperationJournal.IsTerminal(r.State) || r.State == OpState.RecoveryRequired) continue;
                var t = CustodyTake.Parse(r.Evidence);
                if (t != null) list.Add((t, r.State >= OpState.LedgerCommitted ? r.Outcome : "", r.State));
            }
            return list;
        }
    }

    public CustodyTake? Find(string op) => Open().Select(o => o.Take).FirstOrDefault(t => t.Op == op);
    public CustodyTake? Find(string nonce, int tok) => Open().Select(o => o.Take).FirstOrDefault(t => t.Nonce == nonce && t.Tok == tok);

    // Completion is durable before the network send. Repeat receipts after disconnect/restart;
    // the host's journal makes duplicates harmless. No network delivery is inferred from Send.
    public IReadOnlyList<(CustodyTake Take, string How)> Receipts()
    {
        lock (_gate)
        {
            if (_j == null) return _mem.Values.Where(v => v.State == OpState.Complete).Select(v => (v.Take, v.Verdict)).ToList();
            return _j.All().Where(r => r.Kind == Kind && r.State == OpState.Complete && r.Outcome is "kept" or "returned" or "reverted")
                .Select(r => (Take: CustodyTake.Parse(r.Evidence), How: r.Outcome)).Where(r => r.Take != null)
                .Select(r => (r.Take!, r.How)).ToList();
        }
    }

    public int QuarantinedCount { get { lock (_gate) return _j == null ? _mem.Values.Count(v => v.State == OpState.RecoveryRequired) : _j.Quarantined().Count(r => r.Kind == Kind); } }

    private (string Verdict, OpState State) StateOf(string op)
    {
        if (_j == null) return _mem.TryGetValue(op, out var m) ? (m.Verdict, m.State) : ("", OpState.Requested);
        var r = _j.Get(OpKey(op));
        return r == null ? ("", OpState.Requested) : (r.State >= OpState.LedgerCommitted ? r.Outcome : "", r.State);
    }

    private void Move(CustodyTake t, OpState next, string outcome)
    {
        if (_j == null) { var m = _mem[t.Op]; _mem[t.Op] = (t, outcome.Length > 0 ? outcome : m.Verdict, next); return; }
        _j.Advance(OpKey(t.Op), next, t.Json(), outcome);
    }

    /// <summary>The host's answer. Recorded once; a repeated identical answer is accepted, a different one refused.</summary>
    public bool Answer(string op, string verdict)
    {
        if (verdict is not ("ok" or "gone" or "none")) return false;
        lock (_gate)
        {
            var t = Find(op);
            if (t == null) return false;
            var (v, st) = StateOf(op);
            if (st >= OpState.LedgerCommitted) return v == verdict;
            Move(t, OpState.LedgerCommitted, verdict);
            return true;
        }
    }

    /// <summary>The game settled it: kept, returned, reverted (the save does not contain it) or partial. Completes the operation.</summary>
    public bool Settle(string op, string how)
    {
        if (how is not ("kept" or "returned" or "reverted" or "partial")) return false;
        lock (_gate)
        {
            var t = Find(op);
            if (t == null) return false;
            var (v, st) = StateOf(op);
            if (how == "partial") { Move(t, OpState.RecoveryRequired, how); return false; }
            if (st is OpState.Delivered or OpState.RecipientVerified)
            {
                // A restart after a settlement record must continue forward, never replay engine work.
                if (v != how) { Move(t, OpState.RecoveryRequired, "conflicting settlement"); return false; }
                if (st == OpState.Delivered) Move(t, OpState.RecipientVerified, how);
                Move(t, OpState.Complete, how);
                return true;
            }
            if (st < OpState.LedgerCommitted)
            {
                // returned before any answer (the game's own 20 s timeout, or a save): nothing is ever kept without an "ok"
                if (how == "kept") return false;
                Move(t, OpState.LedgerCommitted, "unanswered");
            }
            else if (how == "kept" && v != "ok") return false;
            Move(t, OpState.Delivered, how);
            Move(t, OpState.RecipientVerified, how);
            Move(t, OpState.Complete, how);
            return true;
        }
    }

    public void Dispose() => _j?.Dispose();
}
