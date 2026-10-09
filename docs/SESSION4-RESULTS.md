# Session 4 results: KCD1 Deliver Us (2026-10-08)

This is a playtest on `codex/coop-reliability`, not release-grade, human-accepted or complete shared simulation. VERSION is unchanged (KCD1 0.1.0 / KCD2 0.45.0). KCD2 wire is 14. Use matching tagged builds within each game; the two titles cannot share one room.

## Changes and evidence

KCD1: own-menu native pause gate, nearest-player NPC ownership/puppets, ground pickup/drop, saddlebags, shop stock, pack-to-container puts and Candidate inventory quest rewards. Fresh owned private-engine rerun passed **25/25**, with synthetic peer; it does not prove two-computer combat targeting, sword swings, complete economy or all rewards. The game was stopped and 260 original KCD1 .whs hashes remained unchanged.

KCD2: Candidate live chests, named horse saddlebags, shop stock and inventory quest rewards added to existing WO-134/137, with scoped durable host decisions and native readback. Personal chest mode and existing features retained. New parity Lua suite **35/35**, existing item suite **77/77**. No fresh KCD2 native gameplay proof. Detailed design/limitations in KCD2 `SESSION4-PARITY-DESIGN.md`.

## Distribution validation

Windows build gates, Linux archive fixture checks, local installation and public download checks are recorded in the final build record appended below. Installer performance soak is explicitly skipped by requested `-SkipSoakGate` (KCD1) / `-PlaytestBuild` (KCD2). Packaged Linux success does not establish real Proton gameplay or KCD1 native Windows adapter parity. Published release `SHA256-session4.txt` is the authoritative artifact checksum list; installer/ZIP/archive hashes are generated after all builds and avoid a self-referential embedded checksum.

## Still open

Human tests T-57..T-65 and H-43..H-50 are all pending. Complete native escrow/economy and recipient durability, arbitrary buffs/timers/abilities/world-linked character state, independent KCD1 AI attack targeting/animations, universal quest rewards/XP, full mounts/weather/dice parity, and live checkpoint/reconnect promotion remain unresolved. No capability is promoted to human-accepted or shared simulation from these results. Real saves and firewall rules must remain untouched.

## Final build and local installation record

KCD1 Windows gate: **376 tests passed**, quest catalogue drift zero, native adapter rebuilt, published relay/agent smoke passed. KCD2: **1323 client, 74 relay, 59 Farkle and 413 native tests passed**; all **44 synthetic Lua suites and 3 static checks passed** (new parity suite 35, existing item suite 77). KCD2 published agent/relay completed protocol-14 authentication/ping with no assembly-load errors. The flat merged payload reports nine dependency-version differences as informational; the runtime smoke passed. These are not new launcher GUI or two-computer gameplay proofs.

Both Windows installers compiled and completed locally with optional tasks disabled; KCD1's firewall task was not selected. No processes were killed by executable name. KCD2 verified **1030 components** and generated its local keys pak. Installed programs match their unchanged VERSION files, and installed Lua/native files match the build payload. All **401 real-save files** at the installation baseline retained identical paths and hashes. The owned KCD1 probe was stopped; no game was left running by this session. The previous KCD2 development mod was copied to an ignored backup before installing.

Both Linux archives rebuilt in WSL. Internal SHA256SUMS, fake Steam doctor/link/install, canonical Windows/Linux Lua-pak equality, native KCD1 relay information and KCD2 protocol-14 authentication/ping passed. Fixture menu/key generation warns as expected because fake data lacks game assets. Actual Proton gameplay and injection remain unproved. Packaging cannot promote Windows-only KCD1 engine adapter capabilities on Linux.

Download the NEW **playtest-20261008-session4** pre-release from the matching seth-stock fork. Friends' ZIPs include Setup, current instructions, acceptance tests, capabilities, parity, this report and checksums. Release SHA256-session4.txt lists Setup, friends ZIP and Linux archive checksums. Public downloads are independently checked after upload; this report records build/install evidence, not a substitute for that check. No old release is overwritten. Full feature parity and human acceptance remain open.

## Verified built payload hashes

| Artifact | SHA-256 |
|---|---|
| kcdus.pak | `f8e0609280cab965741f0a771a5335870142999a0c099e07f7ef662059d411b1` |
| KcdUsEngineBridge.dll | `1bfc981498cd57fa172ea5fbfb96f8b542fc696e3f61b913ae27448a2ed3907b` |
| KingdomComeDeliverUs-Setup-0.1.0.exe | `e955d6bf0821dec34832e96414bd265b3b8f466a9a2a05ad77973cf42f875af4` |
