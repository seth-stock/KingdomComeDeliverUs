# Session 6 engine integration results (2026-10-09)

**Full multiplayer remains unfinished in both games.** This continuation adds native combat diagnostics and fixes damage readback/lifetime handling. It does not implement complete independent combat, economy, quest consequences/XP or arbitrary personal character state. No human acceptance, full feature parity or shared simulation is claimed. VERSION stays KCD1 0.1.0 / KCD2 0.45.0; wire stays 3 / 15.

## KCD1: what the real engine established

The original Windows adapter still refuses every WHGame.dll except SHA-256 `cf9f6dc384edcf35c20647a912745ddb8adb5ba65953e329c24e89dd9c4381aa`. The new read-only extension is `entity.human:PlayAnim('@kcdus/combat-read','')`. It resolves each actor from the binding on the game thread and returns version, actor presence/vtable, input support, channel guard and animation-controller presence. It keeps no actor/controller handles across loads. Ordinary PlayAnim calls still reach the original binding; an unsupported engine is never patched.

Private real-engine readback: Henry uses C_Player (vtable RVA 0x26c3e30), input support 1, channel guard 1. Eight nearby NPCs use C_NPCActor (0x21fc0d8), input support 0, channel guard 0. Both have CAnimatedCharacter (0x21bb588) and an animation controller. Controller presence is NOT proof of a safe writer or native combat ownership.

Read-only registration/disassembly identifies the actual blockers:

| Native path | Verified route in this engine |
|---|---|
| SimulateOnAction Lua wrapper | 0x10c3174 -> C_ScriptBindActor slot +0x70 -> 0x10e7a84 -> actor slot +0x460 |
| Ordinary NPC input | C_NPCActor +0x460 -> 0x2e39b0, an immediate return |
| Player input | C_Player +0x460 -> 0x1ffa64 -> 0x1fffc0, player input queue |
| PlayAnim binding | C_ScriptBindHuman vtable 0x26c5ed8, slot +0x128 -> 0x10da760 |
| Its channel guard | 0x370ed4 calls actor+0x28 interface slot +0x80; CGameObject's secondary interface getter reads a 16-bit field |
| Animation lookup | actor+0x2f8 -> +0x20, checked against CAnimatedCharacter; this field is NOT a combat actor |
| Native PlayAnim routine | 0x10a5024; cancels the actor's previous script action through 0x10aa0a0 before queuing another |
| CombatSoul portability | KCD1 C_CombatSoul has no verified function-pointer span at +0x150; KCD2 melee/missile slot offsets MUST NOT be copied into KCD1 |

A private experiment called the native PlayAnim routine directly on a spawned ordinary NPC after checking its animation controller. It faulted while cancelling a previous script action: WHGame+0xf541af <- 0x10aa0b8 <- 0x10a5225. The fault guard caught it, but submission failed and immediate removal did not confirm destruction. Henry identity/world readback survived; the owned process was stopped. **The bypass writer and its enabling environment switch were removed from production source.** It is not Candidate gameplay, and must not be reintroduced by zeroing arbitrary actor fields or restoring the rejected Player/channel-77 proxy.

The production shared-outcomes Lua now confirms the expected health reduction and native death before counting damage as applied. Accepted-but-inert, missing-readback and partial-then-error calls report failure/uncertainty; partial changes are not echoed as another player's attack. There is no automatic retry of an ambiguous mutation.

## KCD2: native hit integration fixes

The native hit hook now reads/publishes a coherent entity/soul/lifetime snapshot. An avatar release or soul replacement invalidates queued watches. Even reuse of BOTH its entity ID and soul address cannot revive a hit from its prior lifetime. Existing same-lifetime refreshes preserve valid pending hits; the registry remains bounded at 16.

Health/stamina corrections are read back after the native SetState call. Only actually restored amounts are accumulated. An inert setter cannot invent forwarded damage; partial restoration cannot inflate it; failed or missing readback marks the watch uncertain and prevents forwarding it as confirmed damage. Native logs and status include `restore_unverified`. Discard callbacks are not sent for a gone player or an unverified correction.

This is still the existing POST-damage watcher, not exact native cause interception. Mixed NPC/PvP causes, multiple attackers in one watch, effects/bleed during the window, delayed causes and player-load lifetime coverage remain unresolved. The changes have native test/build evidence, not new real KCD2 engine or two-computer acceptance.

## Validation and boundaries

- KCD1: 388 automated tests pass; Windows native adapter and Lua pak compile. Three new cases exercise accepted inert damage, partial mutation before an exception, and missing native readback.
- KCD2: 1323 client tests and 434 native tests pass; native guard check passes. Twenty-one new native checks cover lifetime reuse/replacement, coherent concurrent publication, registry bounds, partial/inert/faulted readback, regeneration and nonfinite values.
- KCD1 native reader passed in a disposable real world; animation bypass was a negative result and was removed.
- Original KCD1 save hashes (260 .whs) and all 401 original-save files across both games remained unchanged. No firewall changes. Only harness-owned game PIDs were stopped.
- The session's private screenshots, local asset inspection, disassembly and fault logs stay in ignored _work. No game data, saves, private identity or extracted assets are published.
- No human test is accepted. Linux packaging does not prove Proton engine support.

## Still required for the full request

1. A KCD1-specific, safe combat actor/action lifecycle, independent AI targeting, native weapon contact attribution, attack/block/hit reactions, streaming and acknowledged ownership handoff. An existing animation controller does not meet this gate.
2. Native inventory interception before stock UI, host-local transfers, consumption, barter and theft/crime; exact instance/provenance metadata, durable recipient receipts and inspection/recovery after interrupted operations. Present body/ground/chest/horse/shop watchers are insufficient.
3. Native causal quest/reward/XP/spawn/script-effect authority and durable once-only receipts. Three-second inventory-gain inference cannot establish reward provenance.
4. Native serializers for arbitrary buffs, injuries, timers/cooldowns, abilities, derived perk behavior and world-linked references. KCD1 field 137D must not be treated as an ability bitset.
5. KCD1 mounts/weather/Farkle supported engine equivalence, full live checkpoint/reconnect promotion, two-/four-computer acceptance and performance soak in both titles.

Do not tell players that completing these test suites makes full campaign co-op ready. Carry each missing native operation through a verified disposable-engine mutation/readback, safe lifecycle test, authoritative protocol, durability/recovery path and human acceptance scenario.
