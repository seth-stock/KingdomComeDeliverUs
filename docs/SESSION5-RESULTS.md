# Session 5 implementation and engine findings (2026-10-09)

The request for complete enemies/combat, economy, quest consequences, character state and KCD1 mounts/weather/dice remains **unfinished**. These changes fix specific live paths and add a Candidate weather adapter. They are not complete shared simulation, full parity or human acceptance. VERSION remains 0.1.0 / 0.45.0. KCD1 wire is now **3**, KCD2 wire **15**; matching new agents/relay/paks are required. The KCD1 pak builder now derives its Lua protocol from Wire.cs instead of stamping an obsolete constant.

## Implemented this session

* KCD1 NPC ownership: host incarnation UUID, monotonic ownership revision, a new term on every reassignment, sender sample sequence, per-term owner checks and retirement of old host incarnations. Host interest is bounded to 96 NPCs. Older/out-of-order samples and samples from a previous owner do not reach a current puppet. NPC replacement during streaming is checked by native handle; releasing an old puppet never enables the replacement actor's brain. This improves ownership but does not add remote native attacks or a complete acknowledged handoff barrier.
* KCD1 rewards: the inventory baseline now precedes native quest mutation. Previously it was captured after mutation and native payment could be added again. Reordered reward messages wait for successful quest readback. All rows validate before granting. Native creation/addition must produce the measured inventory delta; uncertain partial calls are not reported as success or retried in this load. No durable reward receipt, XP sharing, arbitrary script-spawn control or replacement for heuristic attribution is claimed.
* KCD2 puts: definitive refusal can compensate a **whole original native item** when its observed identity/count/condition still match, preserving the same instance rather than fabricating a class-only replacement. Partial splits/merges or changed/missing identity are not guessed. Timeout/crash quarantine remains unresolved and blocks supported capture. Unknown host outcomes use `uncertain`, distinct from `gone`, so a host mutation that might have happened cannot trigger a speculative refund. Uncertain native compensation is not retried. Suspended mode changes stop new watcher asks; these are not native inventory-screen locks or complete crash recovery.
* KCD1 weather: scoped host-selected open-world preset adapter, change-gated Lua application, refresh after load, whitelist from the installed rataje mission, and exclusion of the quest-specific dream profile. Candidate, **default off**; use `kcdus_weather on` on both clients, `off` to stop further overrides. Session-local setting. It does not read or preserve all native/scripted weather transitions or restore a previously forced profile on off; real scene/visual and two-PC proof is still required.

## Evidence

KCD1: 385 automated tests passed after the implementation. Private native probe exercised ownership ordering/term release and an inert reward-creation path (failure reported, never success); those checks passed. The weather API accepted the installed foggy_storm profile; the capture was indoors and cannot prove actual outdoor weather rendering. Direct ordinary-NPC `actor:SimulateOnAction('attack1',...)` returned without an error but animation-state readback stayed `<unknown>` and weapon remained undrawn: **not a working remote-combat path**. No Player/channel proxy was re-enabled. Detailed private logs/screenshots stay ignored under `_work`.

KCD2: 1323 client and 74 relay tests passed; the expanded live-container/reward Lua suite passed 40 cases, including original-item refusal compensation and refusal to refund/retry an uncertain put. These are stand-ins, not a KCD2 native-screen proof. Build/install results are recorded separately after the package gates.

The owned KCD1 process was stopped by the private harness. Its 260 original .whs hashes and all 401 real-save files at the session baseline remained unchanged. No firewall rules were changed.

## Engine work that still prevents completion

1. KCD1 requires a proven native attack/block/hit-context path, independently targetable peer actors and safe combat actor lifecycle. Input-call acceptance, health polling and locomotion clips cannot substitute for it. The current owner handoff still lacks acknowledgements and complete native side-effect authority.
2. Both games require native pre-transfer interception for stock inventory/shop/consumption/crime paths, exact instance/provenance records, durable reservations and recipient receipts plus native-state recovery across crash boundaries. The current post-transfer watcher and source/destination journals cannot make every mutation atomic across two running games.
3. Quests require native causes and reward/XP/spawn/effect interception before guest side effects. Inventory-window inference still risks attributing an unrelated gain; in-memory readback fixes do not make it universal or durable.
4. Personal restoration needs a validated complete serializer/reference/clock map for arbitrary buffs, injuries, cooldowns, abilities, effective perk state and world-linked references. The known KCD1 137D actor-reference/timer field remains refused when active. Blindly copying unknown fields would attach invalid actors/state to another world.
5. KCD1 shared riding/mount ownership and the stock dice/Farkle UI still lack engine integration. The preset adapter is not complete native weather parity. Real Proton remains unproved.

All two-computer acceptance, four-player testing and performance soak remain pending. No installer or test result should be described as satisfying the full request above.

## Package gate record

Final KCD1 Windows installer gate: **385 tests**, quest catalogue drift zero, native adapter rebuild and published agent/relay smoke passed. KCD2: **1323 client, 74 relay, 413 native**, all 44 Lua synthetic suites and 3 static checks passed (new parity suite 40 cases); published runtime protocol-15 authentication/ping passed. Frame-rate soak was explicitly skipped for playtest installers. An outdated hard-coded KCD1 wire-2 test expectation was updated to the authoritative wire constant; a world-transfer timing failure passed in isolation and the final complete gate. KCD1's Lua pak protocol now comes from the same wire source as its relay/agent.

The 96-NPC interest set now has a bounded 48-message/s budget: 96 NPCs / 12 rows * 5 Hz requires 40 messages/s. A regression test covers all later chunks; the previous 12-message limit could starve them. This is a transport budget proof, not a performance soak or combat proof.

New payloads are identified by **playtest-20261009-session5**, source commit and SHA-256, since VERSION remains unchanged. Windows Setup/friends ZIPs and Linux archives retain Candidate labels and the unfinished request. Distribution checks (archive hashes, fake Steam/native relay smoke, local installer verification and anonymous public download hashes) are saved in ignored session5 logs; release SHA256-session5.txt is the final artifact manifest. No full-campaign or human-accepted claim follows from successful packaging.

## Built payload hashes

| Artifact | SHA-256 |
|---|---|
| kcdus.pak | `273ce1ee15675acf3f64d45ecd1d8bee6be7d44d6c079b335ece96d6c3e11df1` |
| KingdomComeDeliverUs-Setup-0.1.0.exe | `2d5a953a915df697393c3385d2f7c4d0c006f3b7bd25db1e3bf7d3d9cf0b3df0` |
