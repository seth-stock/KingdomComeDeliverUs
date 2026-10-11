// SPDX-License-Identifier: GPL-3.0-only
// The durable operation journal (shared source; see CoopContract.cs). A journal and a running engine are NOT one atomic transaction, so this
// file never decides that an interrupted engine mutation did or did not happen: it records intent BEFORE the native call and, after a crash,
// quarantines every operation that may have reached the engine until its real state is observed and a resolution is recorded with evidence.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Coop.Contract;

public enum OpState
{
    Requested, Reserved, IntentRecorded, EngineApplying, EngineVerified, LedgerCommitted, Delivered, RecipientVerified,
    Complete, Rejected, RecoveryRequired, HostDecisionComplete,
}

public enum BeginKind
{
    /// <summary>First sight of this operation: the caller may proceed.</summary>
    New,
    /// <summary>Same id, same digest, already finished: return the stored outcome, do nothing.</summary>
    Replay,
    /// <summary>Same id but a different digest: reject without mutating anything.</summary>
    Conflict,
    /// <summary>Same id and digest, still running: wait, do not start it again.</summary>
    InProgress,
    /// <summary>Interrupted after the engine may have been touched: nothing runs until <see cref="OperationJournal.Resolve"/>.</summary>
    Quarantined,
}

public sealed record OperationRecord(string OperationId, string Kind, string PayloadDigest, OpState State, string Outcome, string Evidence, long Sequence);
public sealed record BeginResult(BeginKind Kind, OperationRecord Record);
public enum Observed { Applied, NotApplied }

public sealed class OperationJournal : IDisposable
{
    public const int MaxFieldLength = 2000;
    // A valid record's two 2000-character fields fit even with JSON escaping.
    // Bound parsing independently of the journal's total size.
    public const int MaxLineBytes = 64 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<string, OperationRecord> _ops = new(StringComparer.Ordinal);
    private readonly FileStream _file;
    private string _prevHash = new('0', 64);
    private long _seq;
    private Exception? _writeFailure;
    private bool _disposed;

    public string Path { get; }
    /// <summary>An unterminated final write ignored on opening. Corrupt newline-terminated records are never discarded.</summary>
    public bool IgnoredTornTail { get; private set; }

    private sealed record Line(long S, string Id, string K, string D, OpState St, string O, string E, string P, string H);

    public OperationJournal(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _file = new FileStream(Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        try { Load(); } catch { _file.Dispose(); throw; }
    }

    private static string HashOf(string prev, Line l) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', prev, l.S, l.Id, l.K, l.D, (int)l.St, l.O, l.E)))).ToLowerInvariant();

    private void Load()
    {
        _file.Position = 0;
        var block = new byte[8192];
        var line = new byte[MaxLineBytes];
        long good = 0, offset = 0;
        int used = 0, lineNo = 0, count;
        while ((count = _file.Read(block, 0, block.Length)) > 0)
        {
            for (int i = 0; i < count; i++, offset++)
            {
                if (block[i] != 10)
                {
                    if (used == line.Length) throw new InvalidDataException($"The operation journal {Path} has an oversized record at line {lineNo + 1}; it is not repaired automatically.");
                    line[used++] = block[i];
                    continue;
                }
                lineNo++;
                Line? l = null;
                try
                {
                    l = JsonSerializer.Deserialize<Line>(line.AsSpan(0, used));
                    if (l is not null)
                    {
                        if (l.Id is null || l.K is null || l.D is null || l.O is null || l.E is null
                            || !Enum.IsDefined(l.St)) l = null;
                        else
                        {
                            Validate(l.Id, l.K, l.D); Field("outcome", l.O); Field("evidence", l.E);
                            if (_seq == long.MaxValue || l.S != _seq + 1 || l.P != _prevHash || l.H != HashOf(_prevHash, l)) l = null;
                        }
                    }
                }
                catch (JsonException) { l = null; }
                catch (ArgumentException) { l = null; }
                // A terminating newline means this is a complete record. Never
                // turn corrupted EngineApplying evidence into a safe rejection.
                if (l is null) throw new InvalidDataException($"The operation journal {Path} is damaged at line {lineNo}; it is not repaired automatically.");
                _seq = l.S; _prevHash = l.H;
                _ops[l.Id] = new OperationRecord(l.Id, l.K, l.D, l.St, l.O, l.E, l.S);
                good = offset + 1;
                used = 0;
            }
        }
        IgnoredTornTail = used != 0;
        if (IgnoredTornTail)
        {
            _file.SetLength(good); // only an unterminated tail may be cut
            _file.Flush(true);
        }
        _file.Seek(0, SeekOrigin.End);
    }

    private void RequireWritable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_writeFailure is not null) throw new InvalidOperationException("The operation journal had an ambiguous write failure; close it and recover before any further mutation.", _writeFailure);
    }

    private static void Field(string name, string v)
    {
        if (v.Length > MaxFieldLength) throw new ArgumentException(name + " is too long");
    }

    private OperationRecord Append(string id, string kind, string digest, OpState st, string outcome, string evidence)
    {
        RequireWritable();
        Field("outcome", outcome); Field("evidence", evidence);
        var l = new Line(checked(_seq + 1), id, kind, digest, st, outcome, evidence, _prevHash, "");
        l = l with { H = HashOf(_prevHash, l) };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(l) + "\n");
        if (bytes.Length - 1 > MaxLineBytes) throw new ArgumentException("The operation record is too long");
        try
        {
            _file.Write(bytes, 0, bytes.Length);
            _file.Flush(true); // durable BEFORE the caller acts on the new state
        }
        catch (Exception ex)
        {
            // The file may have received none, some or all of the record. Its
            // hash/sequence cannot safely be reused within this instance.
            _writeFailure = ex;
            throw;
        }
        _seq = l.S; _prevHash = l.H;
        var rec = new OperationRecord(id, kind, digest, st, outcome, evidence, l.S);
        _ops[id] = rec;
        return rec;
    }

    public static bool IsTerminal(OpState s) => s is OpState.Complete or OpState.Rejected or OpState.HostDecisionComplete;

    private static void Validate(string id, string kind, string digest)
    {
        if (id.Length is 0 or > 80 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':'))) throw new ArgumentException("bad operation id");
        if (kind.Length is 0 or > 40) throw new ArgumentException("bad operation kind");
        if (digest.Length != 64 || digest.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f'))) throw new ArgumentException("the payload digest must be a lower-case SHA-256");
    }

    /// <summary>Registers an operation or says what to do about one already known. Same id and digest returns the stored result; a different digest is a conflict.</summary>
    public BeginResult Begin(string operationId, string kind, string payloadDigest)
    {
        Validate(operationId, kind, payloadDigest);
        lock (_gate)
        {
            RequireWritable();
            if (_ops.TryGetValue(operationId, out var rec))
            {
                if (rec.PayloadDigest != payloadDigest || rec.Kind != kind) return new BeginResult(BeginKind.Conflict, rec);
                if (rec.State == OpState.RecoveryRequired) return new BeginResult(BeginKind.Quarantined, rec);
                return new BeginResult(IsTerminal(rec.State) ? BeginKind.Replay : BeginKind.InProgress, rec);
            }
            return new BeginResult(BeginKind.New, Append(operationId, kind, payloadDigest, OpState.Requested, "", ""));
        }
    }

    /// <summary>Moves an operation forward along Requested to Complete, in order. Skipping or going back throws. The record is on disk when this returns.</summary>
    public OperationRecord Advance(string operationId, OpState next, string evidence = "", string outcome = "")
    {
        lock (_gate)
        {
            RequireWritable();
            var rec = Get(operationId) ?? throw new KeyNotFoundException(operationId);
            if (IsTerminal(rec.State) || rec.State == OpState.RecoveryRequired) throw new InvalidOperationException($"{operationId} is {rec.State}");
            bool ok = next switch
            {
                OpState.Rejected => rec.State <= OpState.IntentRecorded,               // before the engine was touched
                OpState.RecoveryRequired => true,
                OpState.Complete => rec.State == OpState.RecipientVerified,
                OpState.HostDecisionComplete => rec.State == OpState.LedgerCommitted && rec.Kind.StartsWith("loot-", StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(rec.Outcome),
                _ => next <= OpState.RecipientVerified && (int)next == (int)rec.State + 1,
            };
            if (!ok) throw new InvalidOperationException($"{operationId}: {rec.State} cannot go to {next}");
            return Append(operationId, rec.Kind, rec.PayloadDigest, next, outcome.Length > 0 ? outcome : rec.Outcome, evidence);
        }
    }

    public OperationRecord? Get(string operationId) { lock (_gate) return _ops.TryGetValue(operationId, out var r) ? r : null; }
    public IReadOnlyList<OperationRecord> All() { lock (_gate) return _ops.Values.OrderBy(r => r.Sequence).ToList(); }
    public IReadOnlyList<OperationRecord> Quarantined() { lock (_gate) return _ops.Values.Where(r => r.State == OpState.RecoveryRequired).OrderBy(r => r.Sequence).ToList(); }

    /// <summary>
    /// After a restart: an operation that never reached the engine call is rejected; one that may have is quarantined (never replayed). Returns what was quarantined.
    /// </summary>
    public IReadOnlyList<OperationRecord> Recover()
    {
        lock (_gate)
        {
            RequireWritable();
            var q = new List<OperationRecord>();
            foreach (var rec in _ops.Values.Where(r => !IsTerminal(r.State) && r.State != OpState.RecoveryRequired).ToList())
            {
                if (rec.State <= OpState.IntentRecorded) Append(rec.OperationId, rec.Kind, rec.PayloadDigest, OpState.Rejected, "", "abandoned before the engine was called");
                // Native/item lineage evidence is needed to resolve the quarantine; retain it exactly.
                else q.Add(Append(rec.OperationId, rec.Kind, rec.PayloadDigest, OpState.RecoveryRequired, rec.Outcome, rec.Evidence));
            }
            return q;
        }
    }

    /// <summary>Records what the engine was actually observed to have done for a quarantined operation. Evidence is required: this is never a guess.</summary>
    public OperationRecord Resolve(string operationId, Observed observed, string evidence, string outcome = "")
    {
        if (string.IsNullOrWhiteSpace(evidence)) throw new ArgumentException("resolving a quarantined operation needs evidence of the observed native state");
        lock (_gate)
        {
            RequireWritable();
            var rec = Get(operationId) ?? throw new KeyNotFoundException(operationId);
            if (rec.State != OpState.RecoveryRequired) throw new InvalidOperationException($"{operationId} is {rec.State}, not quarantined");
            return Append(operationId, rec.Kind, rec.PayloadDigest, observed == Observed.Applied ? OpState.Complete : OpState.Rejected, outcome, evidence);
        }
    }

    public void Dispose() { lock (_gate) { _disposed = true; _file.Dispose(); } }
}
