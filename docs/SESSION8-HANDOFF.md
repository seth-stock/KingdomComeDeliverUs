# Session 8 continuation

Read SESSION8-RESULTS.md, FEATURE-PARITY.md and SESSION7-HANDOFF.md. The full request is unfinished. Preserve VERSION, real saves, firewall rules and existing features. Push only explicit seth-stock fork URLs, never force/upstream. Do not enable the native Candidate paths in normal profiles without actual integration proof. No human acceptance has been recorded.

## New native entries (exact gated KCD1 engine only)

| Operation | Native entry / fields | Evidence / next gate |
|---|---|---|
| Human by entity handle | RVA 0x10c9854; binding +0x70 -> actor-system +0xc8 -> resolve +0x18 | Live target resolution; do not store native actor pointers across calls |
| Combat target strategy | CombatActor +0x730, setter 0x303720 | Uses target wrappers/notifications; live replacement and clearing, not stable authority |
| Attack factory | CombatActor +0x720 -> +0x50; factory 0x460934(context,out,zone,flags); default zone at 0x359c2f0 | Real attack class vtable 0x21aeca8; no fabricated factory |
| Submit | CombatActor vtable +0x290 -> 0x4619cc -> +0x6a8 / action environment 0x219914 | Factory returns a retained local reference; release through action vtable +0x10. +0x10 is NOT an activation entry |
| Attack state | action +0xa8 scheduled, +0xbc slot; environment +0xd0 running slot; combat soul +0xbd8 active, +0xbdc mode | Installation/state transitions are not damage/contact proof |
| Native attack execution | action secondary interface +0xe8 -> real methods primary +0x168 (0x6bc734), +0x178 (0x5cb974), +0x170 (0x5ccf6c) | Helpers/contact callbacks differ from generic TAction; continue provenance/culling/target ownership work |
| Combat initialization | 0x5fdc88, wrapper 0xfa5408; CombatActor +0x524 means contexts registered | Do not reinitialize an already initialized actor or manually toggle the flag |
| XP apply | Stat vtable 0x21c7a48, +0x18 -> 0x6ac280; skill 0x21c7a00 -> 0x5a40b8 | Stat modifier +0x34, skill +0x38; raw amounts +0x1c/+0x20, target +0x10, index +0x18 |
| XP cause | Effect +8 is source; source vtable 0x21beb08, +8 owns I_Cause | Generic script cause 0x2201b10 observed. Never call the source vtable a quest cause |
| Quest causes | Objective cause vtable 0x21c8fd0; ctor 0x5d5f38 copies 24-byte data; state cause 0x2306d30 | Source factory 0x5d5e2c creates SoulStatsLogEffect (0x21c9020), whose apply is a no-op. Do not misidentify this as a quest reward writer |
| Buff reading | archive ctor 0xf32bdc, byte read 0xf34668, child close 0xf33608, final close/free 0xf3368c | Existing known timed instance load/readback proved; no arbitrary capsule loading |

Private probes: native_xp_apply_probe.lua (set `KCDUS_XP_PROBE_SKILL=true` for reading), native_buff_restore_probe.lua, native_fight_probe.lua. They require a fresh owned disposable loaded world, the session-7 startup gates and screenshot inspection before Enter. The fight probe may use a locally selected stock shared-soul/weapon preset through KCDUS_FIGHT_TEMPLATE / KCDUS_FIGHT_WEAPON; this can change native hostility and is not participant ownership. It samples NPC health/poses, clears targets before removal, and must not report combat success merely from cleanup. Final owned process was stopped.

Reuse ignored `_work/ghidra-session7/projects/KCD1Targets`; session-8 outputs live in `_work/ghidra-session8`. Run bounded targets with JDK 25 and the documented memory caps. Some labels in exploratory outputs reflect hypotheses, not verified RTTI. Check assembly, containing unwind spans and actual RTTI before using any entry.

## Remaining implementation gates

1. Combat: keep the real scheduler; add participant-bound actor lifetimes, ownership/term fencing and target control that survives native retargeting. Prove contact, blocks, reactions and credit against each independent participant, including off-view/streaming, release and load. The old Player/channel bypass remains rejected.
2. Economy: retain KCD2's recipient readback protections. Journal both source intent and destination receipt with persistent item metadata/lineage and checkpoint identity before mutation; recover uncertain transfers by native evidence. Current pending tables disappear on load/restart; only a narrow single-destination stock merge has readback coverage. Do not substitute counts or refunds for custody. Integrate KCD1 transferInstance into the same stock UI/host-local/consumption/barter/crime path.
3. Quests: trace persistent quest execution provenance and effect generation/application. XP observation is not a durable queue, and a generic script cause cannot identify the originating quest. Correlate native item/XP/spawn/script effects, fence guest execution, and persist exactly-once outcomes with recovery evidence.
4. Characters: extend explicit validated schemas, clocks, construction/removal, effective stats/perks and safe world-reference mapping. One existing cooldown timer is insufficient. Preserve the current refusal of nonempty unsupported actor/world references.
5. Complete both game-specific engine adapters and room protocols, then acceptance and packaging. Do not call compilation, API acknowledgments or single-process probes full multiplayer.
