# Claude handoff continuation — 2026-10-11

The four requested features remain incomplete. These are development builds;
no two-computer gameplay, Proton gameplay or soak acceptance is established.
VERSION remains 0.1.0; the wire protocol remains 4.

The existing Ghidra projects and logs are preserved under ignored
`_work/ghidra-full-session9`. A fresh streaming audit was saved at
`_work/continuation/ghidra-audit.json`: 37 images, 910,489 discovered functions
attempted, 16 failed exports and 160,023 functions with warnings. The six decoder
repairs remain in the separate review project. Audit exit 2 deliberately
reports the remaining failures. Nothing was re-decompiled or published.

Claude's commits add guest custody, quest XP observations and character buff/
accumulator splicing. Those changes do not establish universal recovery,
causal quest effects or arbitrary character restoration. This continuation:

- Opens persistent custody for normal KCD1 guests after state paths are set and
  releases it when the agent exits; only constructor/idle sessions use memory.

- Carries extended combat samples through the existing ownership-term and
  sequence checks. Engagement holds ownership during a fight and clears on
  disengagement, disconnect, expiry or authority reset.
- Keeps native mirrored attacks behind the existing owned-profile probe gate.
  Cosmetic attacks against invulnerable ghosts are not damaging multiplayer
  combat. Numeric entity handles are encoded correctly; a refused attack still
  releases a previously accepted native target.
- Deduplicates repeated take events without losing the original host answer.
- Records host compensation intent before completing a receipt; interrupted
  compensation remains visible as unknown and is never applied twice.
- Retains completed receipts on disk and retries at most 32 every ten seconds,
  rotating through the journal. There is no receipt acknowledgement protocol;
  repeated host receipts are idempotent. Partial returns are quarantined.
- Continues interrupted settlement records forward without repeating native
  work. Class deletion requires count readback; failed/partial deletion does
  not produce a successful return or a full shop refund.
- Preserves native recovery evidence exactly when the common journal
  quarantines an interrupted operation.
- Fixes quest XP pagination: the observer's ring head is not the end of a
  returned page. Reports overwritten observations and refuses forwarding when
  the recipient's persistent identity is unknown.
- Reannounces world stamps when the connected peer roster changes, fixing a
  guest joining after the initial host stamp.

## Remaining implementation requirements

Independent participant targeting, contacts, damage, blocks, reactions and
credit still need native integration and multiplayer proof. Crash economy
recovery still lacks a consistent checkpoint and persistent item lineage;
world time and class counts cannot prove item custody. Host compensation lacks
native durable acknowledgement, and consumption/barter/crime are not one
authoritative ledger. Corrupt/unavailable guest journals refuse connected transfers; their original
files are preserved.

Quest item rewards and XP receipt deduplication are still load-local; a native
64-row observation ring is not a durable effect queue. Spawn/script effects
and guest quest execution fencing remain open. Character restoration still
refuses active world-referenced core state and lacks complete arbitrary native
construction/clock/reference mapping. Linux's native adapter/injection path
and actual Proton gameplay remain unproved.

Build and test logs are retained in ignored `_work/continuation`. The packages
do not change installed games, real saves or firewall rules. Artifact hashes and local build outputs are recorded in
[CONTINUATION-BUILDS.md](CONTINUATION-BUILDS.md).

## Verified checks

KCD1: 446 managed/Lua tests passed. The exact-engine Windows bridge compiled with warnings treated as errors.

Windows development installers passed their payload smoke gates; the frame-rate
soak gate was explicitly skipped and no gameplay capability was promoted.
Linux self-contained builds use the existing WSL .NET SDK. Native Linux
execution/package smoke evidence is recorded separately from Proton gameplay.
All 401 real-save baseline files across both games remain unchanged.
