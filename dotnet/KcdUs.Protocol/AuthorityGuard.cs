// SPDX-License-Identifier: GPL-3.0-only
// Authority incarnation / epoch / revision checks and participant binding (shared source; see CoopContract.cs).
// Every world-changing request is checked against the CURRENT authority state immediately before the native mutation, not only when it arrives.
using System.Security.Cryptography;
using System.Text;

namespace Coop.Contract;

public sealed record AuthorityState(string WorldId, string Incarnation, long Epoch, string CheckpointId, long GlobalRevision);

/// <summary>One world-changing request. The sender identity comes from the authenticated connection, never from the payload alone.</summary>
public sealed record MutationEnvelope(
    string WorldId, string Incarnation, long Epoch, string CheckpointId,
    string ParticipantId, string ConnectionNonce, string ActorLoadNonce,
    string OperationId, string TargetId, long ExpectedRevision, string Kind, string Payload, string PayloadHash)
{
    public const int MaxPayload = 60_000;
    public static string HashPayload(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
}

public enum EnvelopeVerdict
{
    Ok, Malformed, WrongWorld, StaleIncarnation, StaleEpoch, WrongCheckpoint, UnknownParticipant, StaleConnection, StaleActorLoad, StaleRevision, PayloadMismatch,
}

public sealed record AuthorityCommit(string OperationId, long GlobalRevision, IReadOnlyDictionary<string, long> EntityRevisions);

public sealed class AuthorityGuard
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _connection = new(StringComparer.Ordinal);   // participant -> current connection nonce
    private readonly Dictionary<string, string> _actorLoad = new(StringComparer.Ordinal);    // participant -> current actor load nonce
    private readonly Dictionary<string, long> _entity = new(StringComparer.Ordinal);         // target -> revision
    private AuthorityState _state;

    public AuthorityGuard(AuthorityState state) { _state = state; }

    /// <summary>A new authority process: a fresh random incarnation, so a stale incarnation can never authorise a mutation even when its numeric epoch repeats.</summary>
    public static AuthorityState Start(string worldId, string checkpointId, long epoch = 1, long globalRevision = 0) =>
        new(worldId, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(), epoch, checkpointId, globalRevision);

    public AuthorityState State { get { lock (_gate) return _state; } }

    /// <summary>A new world or authority was selected or loaded: the epoch moves, every queued old-world request becomes invalid and the per-load maps are cleared.</summary>
    public AuthorityState NewEpoch(string worldId, string checkpointId, long globalRevision)
    {
        lock (_gate)
        {
            _state = _state with { WorldId = worldId, CheckpointId = checkpointId, Epoch = _state.Epoch + 1, GlobalRevision = globalRevision };
            _connection.Clear(); _actorLoad.Clear(); _entity.Clear();
            return _state;
        }
    }

    public void Connect(string participantId, string connectionNonce) { lock (_gate) { _connection[participantId] = connectionNonce; _actorLoad.Remove(participantId); } }
    public void Disconnect(string participantId) { lock (_gate) { _connection.Remove(participantId); _actorLoad.Remove(participantId); } }
    public void ActorLoaded(string participantId, string actorLoadNonce) { lock (_gate) _actorLoad[participantId] = actorLoadNonce; }
    public long Revision(string targetId) { lock (_gate) return _entity.TryGetValue(targetId, out var r) ? r : 0; }

    public EnvelopeVerdict Validate(MutationEnvelope e)
    {
        lock (_gate) return ValidateLocked(e);
    }

    private EnvelopeVerdict ValidateLocked(MutationEnvelope e)
    {
        if (e.OperationId.Length is 0 or > 80 || e.TargetId.Length is 0 or > 80 || e.Kind.Length is 0 or > 40 || e.Payload.Length > MutationEnvelope.MaxPayload || e.ExpectedRevision < 0)
            return EnvelopeVerdict.Malformed;
        if (e.WorldId != _state.WorldId) return EnvelopeVerdict.WrongWorld;
        if (e.Incarnation != _state.Incarnation) return EnvelopeVerdict.StaleIncarnation;
        if (e.Epoch != _state.Epoch) return EnvelopeVerdict.StaleEpoch;
        if (e.CheckpointId != _state.CheckpointId) return EnvelopeVerdict.WrongCheckpoint;
        if (!_connection.TryGetValue(e.ParticipantId, out var conn)) return EnvelopeVerdict.UnknownParticipant;
        if (conn != e.ConnectionNonce) return EnvelopeVerdict.StaleConnection;
        if (_actorLoad.TryGetValue(e.ParticipantId, out var load) && load != e.ActorLoadNonce) return EnvelopeVerdict.StaleActorLoad;
        if (e.PayloadHash != MutationEnvelope.HashPayload(e.Payload)) return EnvelopeVerdict.PayloadMismatch;
        long cur = _entity.TryGetValue(e.TargetId, out var r) ? r : 0;
        if (e.ExpectedRevision != cur) return EnvelopeVerdict.StaleRevision;
        return EnvelopeVerdict.Ok;
    }

    /// <summary>
    /// Validates AGAIN and, if still good, applies <paramref name="apply"/> (the native mutation) under the same lock, so nothing can change the state between the check and the
    /// mutation. A false return from <paramref name="apply"/> commits nothing. The commit bumps the global and the target's revision.
    /// </summary>
    public (EnvelopeVerdict Verdict, AuthorityCommit? Commit) Commit(MutationEnvelope e, Func<bool> apply)
    {
        lock (_gate)
        {
            var v = ValidateLocked(e);
            if (v != EnvelopeVerdict.Ok) return (v, null);
            if (!apply()) return (EnvelopeVerdict.Ok, null);
            long rev = (_entity.TryGetValue(e.TargetId, out var r) ? r : 0) + 1;
            _entity[e.TargetId] = rev;
            _state = _state with { GlobalRevision = _state.GlobalRevision + 1 };
            return (EnvelopeVerdict.Ok, new AuthorityCommit(e.OperationId, _state.GlobalRevision, new Dictionary<string, long> { [e.TargetId] = rev }));
        }
    }
}

/// <summary>
/// Room-authenticated participant identity. A participant keeps a P-256 key pair; the room (relay side) remembers the PUBLIC key at first claim and afterwards requires a
/// signature over a fresh server nonce, so another connection cannot take over a participant's id merely by claiming its UUID. Display names, slots and PIDs are not identity.
/// </summary>
public sealed class ParticipantBindings
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _keys = new(StringComparer.Ordinal);   // participant id -> public key (base64 SubjectPublicKeyInfo)
    private readonly HashSet<string> _issued = new(StringComparer.Ordinal);

    public string Challenge()
    {
        var n = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        lock (_gate) { if (_issued.Count > 1000) _issued.Clear(); _issued.Add(n); }
        return n;
    }

    public enum Result { Bound, Accepted, WrongKey, BadSignature, UnknownChallenge, Malformed }

    public Result Claim(string participantId, string publicKeyBase64, string challenge, string signatureBase64)
    {
        if (!Guid.TryParseExact(participantId, "N", out _)) return Result.Malformed;
        lock (_gate)
        {
            if (!_issued.Remove(challenge)) return Result.UnknownChallenge;     // one use only
            bool known = _keys.TryGetValue(participantId, out var bound);
            if (known && bound != publicKeyBase64) return Result.WrongKey;
            try
            {
                using var ec = ECDsa.Create();
                ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
                if (ec.KeySize != 256) return Result.Malformed;
                if (!ec.VerifyData(Encoding.UTF8.GetBytes(Context(participantId, challenge)), Convert.FromBase64String(signatureBase64), HashAlgorithmName.SHA256)) return Result.BadSignature;
            }
            catch (Exception e) when (e is CryptographicException or FormatException) { return Result.Malformed; }
            if (known) return Result.Accepted;
            _keys[participantId] = publicKeyBase64;
            return Result.Bound;
        }
    }

    public static string Context(string participantId, string challenge) => "kcd-coop-participant\n" + participantId + "\n" + challenge;

    /// <summary>The participant's side: a key pair kept in <paramref name="keyFile"/> (created on first use), and the proof for a challenge.</summary>
    public sealed class Identity
    {
        public string ParticipantId { get; }
        public string PublicKey { get; }
        private readonly byte[] _private;
        private Identity(string id, string pub, byte[] priv) { ParticipantId = id; PublicKey = pub; _private = priv; }

        public static Identity LoadOrCreate(string keyFile)
        {
            if (File.Exists(keyFile))
            {
                var parts = File.ReadAllText(keyFile).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 3 && Guid.TryParseExact(parts[0], "N", out _)) return new Identity(parts[0], parts[1], Convert.FromBase64String(parts[2]));
                throw new InvalidDataException("The participant key file is damaged; it is not replaced automatically (that would change this player's identity).");
            }
            using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var id = Guid.NewGuid().ToString("N");
            var pub = Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo());
            var priv = ec.ExportPkcs8PrivateKey();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(keyFile))!);
            File.WriteAllText(keyFile + ".part", id + "\n" + pub + "\n" + Convert.ToBase64String(priv) + "\n");
            File.Move(keyFile + ".part", keyFile, overwrite: false);
            return new Identity(id, pub, priv);
        }

        public string Sign(string challenge)
        {
            using var ec = ECDsa.Create();
            ec.ImportPkcs8PrivateKey(_private, out _);
            return Convert.ToBase64String(ec.SignData(Encoding.UTF8.GetBytes(Context(ParticipantId, challenge)), HashAlgorithmName.SHA256));
        }
    }
}
