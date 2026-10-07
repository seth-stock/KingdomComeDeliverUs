// SPDX-License-Identifier: GPL-3.0-only
// Shared contract tests: the SAME file runs in both games' test projects (only the namespace line differs), against docs/contract-vectors.json.
using System.Text.Json;
using Coop.Contract;
using Xunit;

namespace KcdUs.Tests;

public class CoopContractTests
{
    private static JsonElement Vectors()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "docs", "contract-vectors.json");
            if (File.Exists(p)) return JsonDocument.Parse(File.ReadAllText(p)).RootElement.Clone();
        }
        throw new FileNotFoundException("docs/contract-vectors.json");
    }

    private static string Hash(string? s) => string.IsNullOrEmpty(s) ? "" : new string(s[0], 64);

    private static RoomHandshake Make(JsonElement e) => new(
        e.GetProperty("game").GetString()!, e.TryGetProperty("product", out var p) ? p.GetString()! : "0.0.0",
        e.GetProperty("wire").GetInt32(), e.GetProperty("contract").GetInt32(),
        Hash(e.TryGetProperty("agent", out var a) ? a.GetString() : ""), Hash(e.TryGetProperty("lua", out var l) ? l.GetString() : ""),
        Hash(e.TryGetProperty("native", out var n) ? n.GetString() : ""), Hash(e.TryGetProperty("engine", out var en) ? en.GetString() : ""),
        Hash(e.TryGetProperty("content", out var c) ? c.GetString() : ""),
        e.GetProperty("caps").EnumerateObject().ToDictionary(o => o.Name, o => (CapabilityLevel)o.Value.GetInt32()));

    [Fact]
    public void The_handshake_encodes_exactly_as_the_shared_vectors_say_and_decodes_back()
    {
        foreach (var v in Vectors().GetProperty("handshake").EnumerateArray())
        {
            var h = Make(v);
            string encoded = v.GetProperty("encoded").GetString()!;
            Assert.Equal(encoded, h.Encode());
            var back = RoomHandshake.TryDecode(encoded)!;
            Assert.Equal(h.GameId, back.GameId);
            Assert.Equal(h.LuaHash, back.LuaHash);
            Assert.Equal(CapabilityLevel.EngineVerified, back.Level("presence.bodies"));
            Assert.Equal(CapabilityLevel.Candidate, back.Level("authority.npc"));
            Assert.Equal(CapabilityLevel.Absent, back.Level("authority.quest"));        // absent is not sent and reads as absent
            Assert.Equal(encoded, back.Encode());
        }
    }

    [Fact]
    public void Malformed_or_oversized_handshakes_are_refused_not_guessed_at()
    {
        foreach (var m in Vectors().GetProperty("malformedHandshakes").EnumerateArray()) Assert.Null(RoomHandshake.TryDecode(m.GetString()));
        Assert.Null(RoomHandshake.TryDecode(null));
        Assert.Null(RoomHandshake.TryDecode("c1;g=x;p=1;w=1;cv=1;k=" + new string('a', RoomHandshake.MaxEncodedLength)));
    }

    [Fact]
    public void Hostile_characters_cannot_add_fields_to_a_handshake()
    {
        var h = new RoomHandshake("kcd|1;x=y", "1.0|0", 2, 1, "", "", "", "", "", new Dictionary<string, CapabilityLevel> { ["a;b|c"] = CapabilityLevel.Candidate });
        string e = h.Encode();
        Assert.DoesNotContain('|', e);
        Assert.Equal(1, e.Split(';').Count(p => p.StartsWith("g=")));
        Assert.NotNull(RoomHandshake.TryDecode(e));
    }

    [Fact]
    public void Every_negotiation_vector_gives_the_same_mode_in_both_games()
    {
        foreach (var v in Vectors().GetProperty("negotiation").EnumerateArray())
        {
            var local = Make(v.GetProperty("local")); var remote = Make(v.GetProperty("remote"));
            var expected = Enum.Parse<RoomMode>(v.GetProperty("mode").GetString()!);
            var r = Negotiation.Negotiate(local, remote);
            Assert.True(expected == r.Mode, v.GetProperty("name").GetString() + ": " + r.Describe());
            // both ends compute the same answer
            Assert.Equal(expected, Negotiation.Negotiate(remote, local).Mode);
            Assert.Equal(expected != RoomMode.Refused, r.Admitted);
            if (expected == RoomMode.Refused) Assert.NotEmpty(r.Refusals);
            if (expected is RoomMode.Presence or RoomMode.Partial) Assert.True(r.Missing.Count > 0 || r.Notes.Count > 0);   // it says what is missing or why
        }
    }

    [Fact]
    public void A_presence_room_never_describes_itself_as_shared()
    {
        var a = new RoomHandshake("g", "1", 1, 1, "", new string('a', 64), "", "", "", new Dictionary<string, CapabilityLevel> { [CapabilityNames.PresenceBodies] = CapabilityLevel.IntegrationVerified });
        var r = Negotiation.Negotiate(a, a);
        Assert.Equal(RoomMode.Presence, r.Mode);
        Assert.Contains("NOT active", r.Describe());
        Assert.Contains(CapabilityNames.AuthorityCombat, r.Missing);
        Assert.DoesNotContain("shared simulation", r.Describe());
    }

    [Fact]
    public void A_development_payload_may_be_allowed_unverified_only_by_an_explicit_policy()
    {
        var a = new RoomHandshake("g", "1", 1, 1, "", "", "", "", "", new Dictionary<string, CapabilityLevel>());
        Assert.Equal(RoomMode.Refused, Negotiation.Negotiate(a, a).Mode);
        Assert.Equal(RoomMode.Presence, Negotiation.Negotiate(a, a, new RoomPolicy(AllowUnverifiedPayload: true)).Mode);
    }

    // ------------------------------------------------------------------ authority

    private static MutationEnvelope Env(AuthorityState s, string op = "op1", string target = "npc1", long rev = 0, string payload = "hit", string conn = "c1", string load = "l1", string who = "p1") =>
        new(s.WorldId, s.Incarnation, s.Epoch, s.CheckpointId, who, conn, load, op, target, rev, "damage", payload, MutationEnvelope.HashPayload(payload));

    private static AuthorityGuard Guard(out AuthorityState s)
    {
        s = AuthorityGuard.Start("w1", "cp1");
        var g = new AuthorityGuard(s);
        g.Connect("p1", "c1"); g.ActorLoaded("p1", "l1");
        return g;
    }

    [Fact]
    public void A_good_request_is_applied_once_under_one_lock_and_bumps_the_revisions()
    {
        var g = Guard(out var s);
        int applied = 0;
        var (v, c) = g.Commit(Env(s), () => { applied++; return true; });
        Assert.Equal(EnvelopeVerdict.Ok, v);
        Assert.Equal(1, applied);
        Assert.Equal(1, g.Revision("npc1"));
        Assert.Equal(1, c!.GlobalRevision);
        // the same request again (same expected revision) is now stale: a replay cannot mutate twice
        var (v2, c2) = g.Commit(Env(s), () => { applied++; return true; });
        Assert.Equal(EnvelopeVerdict.StaleRevision, v2);
        Assert.Null(c2);
        Assert.Equal(1, applied);
    }

    [Fact]
    public void Every_stale_scope_is_refused_before_the_native_mutation_runs()
    {
        var g = Guard(out var s);
        bool ran = false;
        Func<bool> apply = () => { ran = true; return true; };
        Assert.Equal(EnvelopeVerdict.WrongWorld, g.Commit(Env(s) with { WorldId = "w2" }, apply).Verdict);
        Assert.Equal(EnvelopeVerdict.StaleIncarnation, g.Commit(Env(s) with { Incarnation = new string('0', 32) }, apply).Verdict);
        Assert.Equal(EnvelopeVerdict.StaleEpoch, g.Commit(Env(s) with { Epoch = s.Epoch + 1 }, apply).Verdict);
        Assert.Equal(EnvelopeVerdict.WrongCheckpoint, g.Commit(Env(s) with { CheckpointId = "cp2" }, apply).Verdict);
        Assert.Equal(EnvelopeVerdict.UnknownParticipant, g.Commit(Env(s, who: "nobody"), apply).Verdict);
        Assert.Equal(EnvelopeVerdict.StaleConnection, g.Commit(Env(s, conn: "old"), apply).Verdict);
        Assert.Equal(EnvelopeVerdict.StaleActorLoad, g.Commit(Env(s, load: "old"), apply).Verdict);
        Assert.Equal(EnvelopeVerdict.StaleRevision, g.Commit(Env(s, rev: 5), apply).Verdict);
        Assert.Equal(EnvelopeVerdict.PayloadMismatch, g.Commit(Env(s) with { Payload = "tampered" }, apply).Verdict);
        Assert.Equal(EnvelopeVerdict.Malformed, g.Commit(Env(s, op: ""), apply).Verdict);
        Assert.False(ran);
    }

    [Fact]
    public void A_new_authority_process_cannot_be_authorised_by_the_old_one_even_when_the_epoch_repeats()
    {
        var old = AuthorityGuard.Start("w1", "cp1", epoch: 7);
        var fresh = AuthorityGuard.Start("w1", "cp1", epoch: 7);
        Assert.NotEqual(old.Incarnation, fresh.Incarnation);
        var g = new AuthorityGuard(fresh); g.Connect("p1", "c1");
        Assert.Equal(EnvelopeVerdict.StaleIncarnation, g.Validate(Env(old)));
    }

    [Fact]
    public void A_new_epoch_invalidates_queued_old_operations_and_the_per_load_maps()
    {
        var g = Guard(out var s);
        var queued = Env(s);                                   // built before the world changed
        var next = g.NewEpoch("w1", "cp2", 0);
        Assert.Equal(s.Epoch + 1, next.Epoch);
        Assert.Equal(EnvelopeVerdict.StaleEpoch, g.Validate(queued));
        Assert.Equal(EnvelopeVerdict.UnknownParticipant, g.Validate(Env(next)));   // connections must re-announce after a load
        g.Connect("p1", "c2");
        Assert.Equal(EnvelopeVerdict.Ok, g.Validate(Env(next, conn: "c2", load: "anything")));
    }

    [Fact]
    public void A_mutation_the_engine_declines_commits_nothing()
    {
        var g = Guard(out var s);
        var (v, c) = g.Commit(Env(s), () => false);
        Assert.Equal(EnvelopeVerdict.Ok, v);
        Assert.Null(c);
        Assert.Equal(0, g.Revision("npc1"));
        Assert.Equal(0, g.State.GlobalRevision);
    }

    [Fact]
    public void A_disconnected_participant_cannot_mutate()
    {
        var g = Guard(out var s);
        g.Disconnect("p1");
        Assert.Equal(EnvelopeVerdict.UnknownParticipant, g.Validate(Env(s)));
    }

    // ------------------------------------------------------------------ participant binding

    [Fact]
    public void A_participant_id_cannot_be_taken_over_by_claiming_its_uuid()
    {
        string dir = Path.Combine(Path.GetTempPath(), "coop-id-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var room = new ParticipantBindings();
            var alice = ParticipantBindings.Identity.LoadOrCreate(Path.Combine(dir, "alice.key"));
            var again = ParticipantBindings.Identity.LoadOrCreate(Path.Combine(dir, "alice.key"));
            Assert.Equal(alice.ParticipantId, again.ParticipantId);                                  // the same player keeps the same identity
            var c1 = room.Challenge();
            Assert.Equal(ParticipantBindings.Result.Bound, room.Claim(alice.ParticipantId, alice.PublicKey, c1, alice.Sign(c1)));
            var c2 = room.Challenge();
            Assert.Equal(ParticipantBindings.Result.Accepted, room.Claim(alice.ParticipantId, alice.PublicKey, c2, alice.Sign(c2)));

            // another connection claiming alice's id with its own key
            var mallory = ParticipantBindings.Identity.LoadOrCreate(Path.Combine(dir, "mallory.key"));
            var c3 = room.Challenge();
            Assert.Equal(ParticipantBindings.Result.WrongKey, room.Claim(alice.ParticipantId, mallory.PublicKey, c3, mallory.Sign(c3)));
            // ... or with alice's public key but a signature it cannot make
            var c4 = room.Challenge();
            Assert.Equal(ParticipantBindings.Result.BadSignature, room.Claim(alice.ParticipantId, alice.PublicKey, c4, mallory.Sign(c4)));
            // a challenge is single use, and an unknown one is refused
            Assert.Equal(ParticipantBindings.Result.UnknownChallenge, room.Claim(alice.ParticipantId, alice.PublicKey, c2, alice.Sign(c2)));
            Assert.Equal(ParticipantBindings.Result.UnknownChallenge, room.Claim(alice.ParticipantId, alice.PublicKey, "nope", alice.Sign("nope")));
            Assert.Equal(ParticipantBindings.Result.Malformed, room.Claim("not-a-uuid", alice.PublicKey, room.Challenge(), "x"));

            File.WriteAllText(Path.Combine(dir, "alice.key"), "damaged");                          // a damaged key file is never silently replaced
            Assert.Throws<InvalidDataException>(() => ParticipantBindings.Identity.LoadOrCreate(Path.Combine(dir, "alice.key")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ------------------------------------------------------------------ the durable journal

    private static string TempJournal() => Path.Combine(Path.GetTempPath(), "coop-j-" + Guid.NewGuid().ToString("N")[..8], "ops.jsonl");
    private static readonly string D1 = new('1', 64), D2 = new('2', 64);

    private static void Walk(OperationJournal j, string id, OpState upTo)
    {
        foreach (var s in new[] { OpState.Reserved, OpState.IntentRecorded, OpState.EngineApplying, OpState.EngineVerified, OpState.LedgerCommitted, OpState.Delivered, OpState.RecipientVerified, OpState.Complete })
        {
            j.Advance(id, s);
            if (s == upTo) return;
        }
    }

    [Fact]
    public void An_exact_retry_returns_the_stored_result_and_a_conflicting_one_is_rejected_across_restarts()
    {
        string path = TempJournal();
        using (var j = new OperationJournal(path))
        {
            Assert.Equal(BeginKind.New, j.Begin("take-1", "take", D1).Kind);
            Assert.Equal(BeginKind.InProgress, j.Begin("take-1", "take", D1).Kind);
            Walk(j, "take-1", OpState.Complete);
            Assert.Throws<KeyNotFoundException>(() => j.Advance("take-2", OpState.Reserved));      // the journal never invents an operation
        }
        try
        {
            using var j2 = new OperationJournal(path);                                              // a process restart
            var replay = j2.Begin("take-1", "take", D1);
            Assert.Equal(BeginKind.Replay, replay.Kind);
            Assert.Equal(OpState.Complete, replay.Record.State);
            Assert.Equal(BeginKind.Conflict, j2.Begin("take-1", "take", D2).Kind);                  // same id, different digest
            Assert.Equal(OpState.Complete, j2.Get("take-1")!.State);                                // the conflict changed nothing
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void Transitions_only_go_forward_and_rejection_is_possible_only_before_the_engine_is_touched()
    {
        string path = TempJournal();
        try
        {
            using var j = new OperationJournal(path);
            j.Begin("a", "take", D1);
            Assert.Throws<InvalidOperationException>(() => j.Advance("a", OpState.EngineApplying));      // skipping
            j.Advance("a", OpState.Reserved);
            j.Advance("a", OpState.Rejected, "out of range");                                            // fine: nothing reached the engine
            Assert.Throws<InvalidOperationException>(() => j.Advance("a", OpState.IntentRecorded));      // terminal
            Assert.Equal(BeginKind.Replay, j.Begin("a", "take", D1).Kind);

            j.Begin("b", "take", D1);
            Walk(j, "b", OpState.EngineApplying);
            Assert.Throws<InvalidOperationException>(() => j.Advance("b", OpState.Rejected));            // the engine may have been touched
            Assert.Throws<InvalidOperationException>(() => j.Advance("b", OpState.Complete));            // not skipping verification
            Assert.Throws<ArgumentException>(() => j.Begin("bad id!", "take", D1));
            Assert.Throws<ArgumentException>(() => j.Begin("c", "take", "not-a-digest"));
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void After_a_crash_an_operation_that_may_have_reached_the_engine_is_quarantined_never_replayed()
    {
        string path = TempJournal();
        try
        {
            using (var j = new OperationJournal(path))
            {
                j.Begin("before", "take", D1); j.Advance("before", OpState.Reserved);                   // crashed before intent
                j.Begin("mid", "take", D1); Walk(j, "mid", OpState.EngineApplying);                      // crashed after the native call may have happened
                j.Begin("late", "take", D1); Walk(j, "late", OpState.Delivered);
                j.Begin("done", "take", D1); Walk(j, "done", OpState.Complete);
            }
            using var j2 = new OperationJournal(path);
            var q = j2.Recover();
            Assert.Equal(new[] { "mid", "late" }, q.Select(r => r.OperationId));
            Assert.Equal(OpState.Rejected, j2.Get("before")!.State);
            Assert.Equal(OpState.Complete, j2.Get("done")!.State);
            Assert.Equal(BeginKind.Quarantined, j2.Begin("mid", "take", D1).Kind);                       // a retry does NOT run again
            Assert.Throws<ArgumentException>(() => j2.Resolve("mid", Observed.NotApplied, " "));         // evidence is required
            j2.Resolve("mid", Observed.NotApplied, "inventory readback shows the item still on the corpse");
            Assert.Equal(OpState.Rejected, j2.Get("mid")!.State);
            j2.Resolve("late", Observed.Applied, "item present only in the recipient's inventory", "granted");
            Assert.Equal(OpState.Complete, j2.Get("late")!.State);
            Assert.Empty(j2.Quarantined());
            Assert.Empty(j2.Recover());                                                                   // idempotent
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void A_write_cut_off_by_a_crash_is_ignored_but_damage_in_the_middle_fails_closed()
    {
        string path = TempJournal();
        try
        {
            using (var j = new OperationJournal(path)) { j.Begin("a", "take", D1); j.Advance("a", OpState.Reserved); }
            var bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes.Concat(System.Text.Encoding.UTF8.GetBytes("{\"S\":3,\"Id\":\"a\",\"K\":\"ta")).ToArray());   // torn final write
            using (var j2 = new OperationJournal(path))
            {
                Assert.True(j2.IgnoredTornTail);
                Assert.Equal(OpState.Reserved, j2.Get("a")!.State);
                j2.Advance("a", OpState.IntentRecorded);                                                  // appends cleanly after the cut
            }
            using (var j3 = new OperationJournal(path)) Assert.Equal(OpState.IntentRecorded, j3.Get("a")!.State);

            var text = File.ReadAllText(path).Replace("\"Reserved\"", "\"Reserved\"").Replace(D1, D2);   // alter an earlier line
            File.WriteAllText(path, text);
            Assert.Throws<InvalidDataException>(() => new OperationJournal(path));
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void The_journal_is_durable_before_the_caller_acts_on_the_new_state()
    {
        string path = TempJournal();
        try
        {
            using var j = new OperationJournal(path);
            j.Begin("a", "take", D1);
            j.Advance("a", OpState.Reserved);
            // read the file through another handle while the journal is still open: the record is already on disk
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var text = new StreamReader(fs).ReadToEnd();
            Assert.Equal(2, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }
}
