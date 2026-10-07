# Claude Code handoff: complete multiplayer in KCD1 and KCD2

Prepared 2026-10-07. This is an implementation brief, not a statement that multiplayer is complete. Sections marked **proposed** describe work that has not been implemented or proved in the engine. Human testers will perform multiplayer acceptance tests; automated tests and engine probes remain the implementer's responsibility.

## Instruction to Claude

Continue from the two `codex/coop-reliability` worktrees below. Complete KCD1 shared combat/NPC/quest/loot simulation and the remaining personal-character state restoration. Complete KCD2's missing authority, durable loot and live reconnect integration. Bring both games to the feature contract in `FEATURE-PARITY.md`, preserve existing working features, build matching local installations and downloadable packages, and push tested commits to Seth's two GitHub repositories. Do not replace this task with another plan, remove a difficult feature to manufacture parity, or call a stub/synthetic result working multiplayer. Record specific engine blockers and unfinished behavior honestly. Obtain human acceptance results before marking a release multiplayer-ready.

The user has authorized implementation, local mod preparation and pushes to these forks in the prior conversation. Inspect current instructions, dirty files, account and remotes before acting. Publishing a development branch is distinct from declaring a validated release. Preserve any newer work found after this handoff.

## 1. Start from the right branches

| | KCD1: Deliver Us | KCD2: Together |
|---|---|---|
| Worktree to develop | `C:\Users\seths\Projects\kcd1-coop\codex-reliability` | `C:\Users\seths\Projects\kcd2-coop\codex-reliability` |
| Branch | `codex/coop-reliability` | `codex/coop-reliability` |
| Verified starting commit | `4f7397bf2f6bcc87e1b8ec5597d7e215cf4725cc` | `1653c5c47f71d5604183d5fe8fdd913b999ccf6e` |
| Authorized destination | `https://github.com/seth-stock/KingdomComeDeliverUs.git` | `https://github.com/seth-stock/KingdomCome-Together.git` |
| Original checkout to preserve | `C:\Users\seths\Projects\kcd1-coop\KingdomComeDeliverUs` | `C:\Users\seths\Projects\kcd2-coop\KingdomCome-Together` |
| Original baseline | `main`, `bd17e9538e3efd7f94fea7ce4cb180fd33d220a9` | `feature/linux-support`, `6ed6bf1a18d7f608e9d5b88b2df878805a87f21b` |
| Current VERSION / protocol | `0.1.0` / `1` | `0.45.0` / `11` |
| Local game | `F:\SteamLibrary\steamapps\common\KingdomComeDeliverance` | `E:\SteamLibrary\steamapps\common\KCD2Mod` |

The KCD2 `origin` points to **DeepFriedDepp's upstream**, not Seth's fork. Use the explicit destinations above. KCD1 has no configured origin at this starting point. Do not reset, clean, force-push, or overwrite the original checkouts. These documentation files were created or updated after the listed commits; review and commit them alongside the continuation work.

Read each repository's `docs/IMPLEMENTATION-STATUS.md`. In KCD1 also read `docs/ENGINE-INTEGRATION-20261007.md`, `docs/ENGINE-INTEGRATION-20261006.md`, `docs/KNOWN-LIMITS.md`, `docs/SHARED-WORLDS.md` and `tools/engine/README.md`. Prefer the newest evidence over historical descriptions. Some older feature claims and test counts predate these branches.

## 2. Current truth; preserve these advances

### KCD1

- Ordinary **NPC** peer bodies walk/run/sprint and return to idle, follow positions/headings and replicate equipped item classes/condition. This was observed in the retail engine. They are currently invulnerable, AI-invisible, have behavior evaluation disabled and dialogue restricted: **presence bodies, not verified combat participants**.
- The Windows startup adapter reads equipped flags and persistent soul/item identities on one exact retail engine build. Unsupported native adapters must remain disabled. It does not provide native combat, quest or inventory interception.
- Normal **My own Henry** joins capture a fresh native save, retain an immutable personal snapshot, stage XP/perk/core/inventory replacement, install with archived originals, load, and require a new native save from the expected playline before acknowledging. No additive `CARDSET` fallback handles these joins or sending Henry home.
- Stat/skill XP can decrease to the captured value. Perk records, inventory amounts/equipment/condition/extensions and portable core resources are staged. Story XP, destination position, quest knowledge and companion references remain destination-owned under the present implementation. Other soul state/timers and arbitrary abilities/buffs are incomplete.
- `137D` is a nested `04B0` record containing an actor reference and timer. **It is not an ability bitset.** Only its observed empty payload can move today. Drawn weapons, riding, dialogue, death or combat danger prevent personal capture.
- Native inventory list order can change during equipment loading. Acceptance matches complete item records by persistent instance identity and compares the header; it does not ignore changed quantities, condition, equipment flags or extensions. Sparse zero XP and destination story XP have specific normalization rules.
- The UI load-completed event can precede actual save readiness. The current path delays verification; a personal capture whose call was accepted but wrote nothing retries once. Explicit refusal never uses an old save. Preserve this distinction.
- Concurrent relay admission IDs/host selection were fixed. A native verification autosave preserves the received checkpoint's stamp to avoid resynchronization loops. Save capture correlates its tag and refuses ambiguous/wrong-playline writes. Offers/chunks/retransmissions are scoped to the requested sender/recipient.
- Five physical playlines remain. Offline archive/restore leasing and verified save-install recovery are implemented. Removing the game limit is not established. Never weaken the existing-save protection.
- Latest suite: **240 passing tests**. Production join and pending-load retry were observed with one real disposable game and a synthetic sender. **No two-PC acceptance pass.** All 260 real `.whs` hashes were unchanged.

### KCD2

- It already contains substantial native/Lua NPC, movement, combat, quest, item, horse, weather and dice implementation. Audit actual code paths and evidence individually; a configuration flag or source file is not proof that a feature works end to end.
- `ReconcileService` captures immutable paired manifests, selects ancestors or explicitly chosen divergent branches, checks character kind/build/playthrough seed and prepares personalized saves. It **does not promote them into live playlines or complete a running reconnect barrier**.
- Body take/put operations carry connection/load scopes and replay results in memory. Native mutation is checked by inventory readback. This is **not durable exactly-once transfer**, a global world epoch, or protection for every loot/drop/takedown path. Optimistic inventory UI can expose an unconfirmed item.
- Normal rejoin stopped using an unrelated newest snapshot; this is not the complete manifest protocol.
- Installer preparation detects missing/empty required game paks. A matching native/Lua/agent payload and installer upgrade/rollback still need validation. A KCD2 Warhorse/PLAION modding EULA was not found in the earlier work; locate the authentic applicable text and distribution requirements, never invent them.
- Latest recorded checks: client 1,272; relay 62; Farkle 59; world-item Lua 64; drop Lua 55; load/presence Lua 50; Lua local-count 6; installer detection 24. These are historical offline/synthetic results, not a new engine or two-PC pass.

## 3. Source map

| Concern | KCD1 existing code | KCD2 existing code |
|---|---|---|
| Session/relay routing | `dotnet/KcdUs.Agent/Session.cs`, `AgentHost.cs`, `dotnet/KcdUs.Relay/RelayServer.cs` | `dotnet/KcdMp.Client/GameBridge.cs`, its `GameBridge.Wo*.cs` partials, `dotnet/KcdMp.Server/Features/ClientHandling/` |
| Native engine | `native/KcdUs.EngineBridge/EngineBridge.cpp`, `dotnet/KcdUs.Agent/EngineGameStart.cs` | `native/KCDMP/`, especially `combat_write.*`, `combat_construct.cpp`, `npc_drive.*`, `npc_scan.*`, `npc_trace.*` |
| Runtime Lua | `mod/kcdus/lua/` | `kdcmp/Data/Scripts/Startup/kdcmp.lua`, `kdcmp_loot_operations.lua` |
| Personal state | `dotnet/KcdUs.Agent/Worlds/CharacterSave.cs`, `ExactTraitsSave.cs`, `ExactInventorySave.cs`, `CharacterCheckpoint.cs` | `dotnet/KcdMp.Client/WhsSave.cs`, `WhsSave.Wo125.cs`, `GameBridge.Wo125.cs`, `ReloadReconcile.cs` |
| World joins/recovery | `Worlds/WorldCoordinator.cs`, `WorldLogic.cs`, `SaveStore.cs`, `SaveInstallTransaction.cs` | `ReconcileService.cs`, `CheckpointCommands.cs`, relevant live `GameBridge` save/load paths |
| Shared persistence foundation | `dotnet/KcdUs.Protocol/CheckpointStore.cs` | `dotnet/KcdMp.Protocol/CheckpointStore.cs` |
| Identity/movement | `native_identity.lua`, `ghosts.lua`, `locomotion.lua`, `outfits.lua` | `NativeNpc.cs`, `NpcStateCodec.cs`, `NpcScanCodec.cs`, native NPC/motion code |
| Tests | `dotnet/KcdUs.Tests/` | `dotnet/KcdMp.Client.Tests/`, `KcdMp.Relay.Tests/`, `KcdMp.Farkle.Tests/`, `tools/Test-*.ps1` and Lua fixtures |

**Proposed additions**, not existing APIs: a common authority state machine, durable economy journal, live checkpoint barrier, versioned personal-state schema, adapter capability report and shared contract test vectors. Give the final files sensible repository-native names and update this source map.

## 4. Freeze the common multiplayer contract first

Use identical semantics and contract fixtures in both repositories, with separate game/save/native adapters. Preserve GPL notices and provenance. Do not copy KCD2 offsets, calling conventions, souls, save tags or native code assumptions into KCD1. Both `CheckpointStore.cs` files are a reusable foundation, not a completed runtime coordinator.

**Proposed identifiers and envelopes:**

```text
RoomHandshake:
  gameId, productVersion, wireVersion, contractVersion,
  agentHash, luaHash, nativeHash, engineHash, contentProfileHash,
  requiredCapabilities, participantId, connectionNonce

MutationEnvelope:
  worldId, authorityIncarnation, epoch, checkpointId,
  participantId, connectionNonce, actorLoadNonce,
  operationId, targetPersistentId, expectedRevision,
  payloadHash, typedPayload

AuthorityCommit:
  operationId, globalRevision, affectedEntityRevisions,
  typedOutcome, outcomeHash, journalSequence
```

- `gameId` must match within a room; parity means equivalent co-op behavior within each title.
- Persist participant identity outside game saves. Relay slot numbers, display names, PIDs, entity IDs, soul WUIDs and machine names are not participant identity. Establish a room-authenticated binding so another connection cannot replace an existing participant merely by claiming its UUID. Handle intentional profile cloning/conflicting logins explicitly.
- Use persistent world/branch identity and per-load handle maps. KCD1's native 32-hex soul/item keys are raw bytes, not necessarily conventional GUID text. Keep their encoding explicit and consistent. Never use names/proximity/class GUIDs as permanent NPC/item identity.
- A new authority incarnation is random on authority-process restart; epoch changes on world selection/load/authority change. Persist the last committed world/checkpoint and revision. Even if an old numeric epoch repeats, a stale incarnation cannot authorize a mutation.
- Derive sender identity from the authenticated relay connection; validate it against the payload. Host authority is assigned by the room, not a peer flag.
- Validate world, incarnation, epoch, participant connection, actor load generation, revision, bounds and capability compatibility **again immediately before native mutation**. Discard queued old-world operations, not just incoming old packets.
- The host serializes world-changing operations through one authority queue. Include host-local inventory/quest/combat paths in that queue. Guests send intents; they do not choose damage amounts, loot awards, rewards or final quest outcomes.
- Give every operation a unique ID and durable payload digest. Same ID/same digest returns its stored result. Same ID/different digest is a conflict. An unknown result after a crash enters recovery/quarantine; it is not blindly replayed.
- NPC transforms/cosmetic poses can use sequenced coalesced updates. Health/death, ownership and quest commits require reliable ordered delivery and gap repair. Revision checks must stop an old alive snapshot reviving a dead NPC.
- Preserve per-title product versions. Define a shared contract version and capability set; identical executable version strings across different games are unnecessary. Increment each affected wire/native/Lua ABI version and refuse unsafe mixtures.

The full shared mode must negotiate **all required capabilities before accepting the player into mutable gameplay**. Display the specific missing engine/payload capability if it fails. Do not silently run independent worlds while labeling the session shared multiplayer. Preserve the existing presence mode as an explicitly labeled mode if desired.

## 5. Engine capability work: the first hard dependency

Create a capability table per tested engine hash with: entry point/calling convention, ownership/lifetime/thread requirements, preconditions, readback, gameplay-effect probe, persistence probe, rejection behavior and evidence path. Distinguish `candidate`, `engine-verified`, `integration-verified`, and `human-accepted`.

KCD1's supported `WHGame.dll` SHA-256 is:

```text
cf9f6dc384edcf35c20647a912745ddb8adb5ba65953e329c24e89dd9c4381aa
```

The existing adapter uses RVA `0x10c9a98` for its startup `ItemManager.GetItem` replacement; its complete file hash and original-prologue checks are mandatory. Its soul lookup uses RVA `0x284b04`, the environment pointer at RVA `0x35ac728`, registry offset `0x548` and registry lookup base `+0x48`; the existing code checks the WUID before reading the persistent soul GUID at `+0x38`. These are **existing identity-reader details**, not verified combat mutation entry points. Treat other reverse-engineering leads as provisional until independently proved.

Required capabilities to prove before building the shared engine adapter:

1. Observe and intercept **ordinary weapon hits** before their native HP/death/XP/crime/quest mutations. Record actual attacker/victim persistent IDs, hit/weapon/contact data, thread and call stack. Test sword, fist, bow/projectile, block/parry, fall, poison/bleed and scripted/environmental damage separately.
2. On a guest, suppress or defer a native world mutation while preserving cosmetic input/animation; subsequently apply one authorized host outcome. A post-hit log callback is insufficient if irreversible side effects already happened.
3. Drive a remote ordinary NPC body as a combat participant: real attack/block/weapon state, collision/hit testing, AI perception, factions/aggro, damage attribution and cleanup. The present invulnerable/AI-invisible body is not sufficient. Keep economic inventory separate from render-only proxy clothes.
4. Intercept actual inventory transfer/split/merge/drop/pickup/consume/equip paths before ownership changes. Include the stock corpse/container/shop UI and host-local actions, not only custom Lua RPC helpers.
5. Intercept quest/objective/condition/variable changes, associated rewards/spawns and NPC-death causes before guest world effects. Prove authoritative replay executes each side effect once.
6. Pause/drain or otherwise consistently capture world, personal states, ownership ledger and revision at one save barrier; confirm native load acceptance for the intended checkpoint and participant state.

Use the existing read-only disassembly/script/table tools to locate paths, then guarded disposable probes to validate them. `pcall` success, setting a health number, or registration in a script does not prove native combat/death handling. The tested KCD1 `DealDamage` calls did not change HP or enter the wrapped Lua `BasicActor.Server.OnHit`; investigate the actual native RPG event/damage path rather than using that assumption as implementation. `HasPerk`/`GetPerks` are absent in the tested retail Lua API.

**Do not re-enable the native Player/channel-77 proxy approach.** Removing that proxy crashed the engine; a null read was traced into the retail module but the exact singleton lifetime contract remains unresolved. Ordinary NPC clip animation/removal works. If ordinary NPCs cannot support a proven combat role, design and prove a different native actor lifecycle instead of restoring the crashing route. Do not use negative XP to lower levels: the observed reading level went from 3 to 20.

## 6. Work package: host-owned NPCs and combat

**Proposed behavior:** one authoritative simulation determines NPC AI, transforms, attacks, health/stamina, incapacitation/death, drops and kill attribution. Remote players remain independently controllable and have distinct personal combat state.

1. Build a persistent entity registry with per-load native handles, lifecycle generation, region, state revision and last authoritative snapshot. Reject stale/reused handles. Represent unloaded, dormant, alive, incapacitated and dead separately; do not delete an NPC because a stream update is absent.
2. Host interest is the union of participant regions. Prove native streaming/simulation support outside the host camera's normal range. If an engine cannot simulate a remote region, implement a visible, enforced supported-area policy until a safe streaming/ownership mechanism exists. Never quietly hand uncontrolled authority to guests or claim unrestricted shared roaming without this gate.
3. Send sequenced NPC state snapshots and lifecycle commits. Clients use native movement/animation/AI suppression appropriate to the state. Account for teleports, ladders, interiors, mounted NPCs, combat poses, corpses and streaming re-entry. Losing authority/connection suspends writes and enters an explicit recovery/solo transition.
4. Intercept a player's normal attack into an intent with input sequence, weapon instance, target/contact and bounded timing data. Host validates range, line of sight/contact, weapon ownership, actor state, perks, block/parry and revision. Use the host's verified native combat rules; never trust a client-provided HP delta as damage.
5. Commit resulting health/stamina, injuries/status effects, death revision, aggressor, participant credit, personal XP, crime/reputation effects and loot-generation cause under one operation. Capture native side-effect identities so repeated delivery cannot award XP, generate a corpse/drop or advance a quest twice.
6. Apply the commit on replicas under a re-entrancy guard so replay is not captured as another local attack. Suppressed guest side effects may not run independently. Cosmetic prediction is allowed; irreversible health/death/economic/quest prediction is not.
7. Route damage to a host-side remote avatar back to the corresponding participant's real character exactly once. Prove AI can target the remote avatar and attribute the hit correctly. Do not compute every guest using the host Henry's stats/perks or make avatars invulnerable to conceal missing damage.
8. Resolve simultaneous lethal hits in authority order; issue one death and one corpse/loot generation with an explicit kill/assist policy. Old state cannot resurrect it. Intent rejection returns an understandable result without applying rewards.

**Exit evidence:** ordinary engine attacks by either player damage the same enemy; an enemy targets and damages either player; blocking/armor/status effects use the correct character; simultaneous kills result in one authoritative death/reward/drop; reload/reconnect and stale packets do not change those outcomes.

## 7. Work package: complete personal-character restoration

Audit the entire character persistence graph, not just the existing seven-field native core. Produce `character-state-map` documentation/tests per engine hash with exact field provenance and these ownership classes:

| Class | Policy |
|---|---|
| Personal progression/economy | Restore participant XP, selected perks/accounting, money, exact owned inventory and equipment according to the authoritative ledger |
| Personal physiology/effects | Restore health/stamina/nourishment/energy, injuries, intoxication, poisons, bleeding, timed buffs, cooldowns and learned abilities with verified semantics |
| Derived/cache data | Rebuild from restored base state/perks/equipment using proven native recomputation; do not transfer stale caches |
| World/story state | Preserve the selected world's quests, world variables, narrative unlocks and state that belongs to that world; classify reputation/crime/disguise rules explicitly |
| Actor/item references | Remap by stable identity only if the referenced object exists and the ownership/role is valid; otherwise apply a documented recovery/deferred-state policy without discarding the source |
| Unknown fields | Preserve in the source capsule/archive, keep the operation pending/refused until semantics are established; do not silently treat unknown bytes as portable |

For KCD1, investigate state groups `0926`, `0931`, `0934`, `0935`, `0936`, the nonempty `137D/04B0` record, perk instance extensions and any references in the actor/save modules outside the soul/inventory records. Current preservation in the destination is an interim policy, not proof they are all world-owned. Do not label `0926` as timers, or `0934` as fully understood knowledge, from tag numbers alone. Determine source/destination archetype compatibility and story-driven character switches. KCD2 currently checks Henry/Godwin kind and seed; define a deliberate character-kind transition policy rather than copying another character's block blindly.

**Proposed timer conversion**, only after proving each field's clock:

```text
absolute expiry on clock C:
  remaining = max(0, sourceExpiry - sourceClock(C))
  destinationExpiry = destinationClock(C) + remaining

start-time anchored elapsed duration:
  elapsed = max(0, sourceClock(C) - sourceStart)
  destinationStart = destinationClock(C) - elapsed
```

Clock kinds include world simulation time, unpaused session time and real wall time. Preserve infinite/disabled/sentinel meanings separately. Duration counters and cumulative statistics are not absolute timestamps. Account for paused games, offline elapsed-time policy, time-of-day synchronization and different world ages; define which effects tick while offline. Never use save-file modification time to rebase game effects.

Apply restoration as a staged, validated graph under the load barrier; apply base progression/inventory/effects in the native-required order, rebuild derived state, then verify. Perk byte equality is necessary evidence but does not prove effective combat/speech/regen modifiers, available perk points or removal of destination-only effects. Test real representative selected perks and timed effects against behavior as well as serialization.

Resource verification currently allows values to tick and does not compare every extra core field. Replace that broad gap with a controlled pause/readback point or a documented, bounded drift model per proven field. Continue allowing only proved semantic normalization, such as inventory order and sparse zero XP; do not accept a broad mismatch to get a green test.

A carried item may already be owned in the chosen world or another participant's capsule. The current KCD1 check scans other destination inventories but does not establish **cross-participant** ownership. Cloning the host's Henry to seed a guest can clone the same economic item IDs. Create a join/genesis ownership policy: grant new starter items through authority mint operations, mirror host appearance without ownership, or restore the guest's verified unique ownership. Do not resolve conflicting claims by merely renaming an item GUID, granting extra copies, or silently deleting a participant's inventory. Preserve the losing source in archive/escrow and require a defined ownership resolution.

**Exit evidence:** different low/high-stat characters round-trip through both worlds with exact owned items/perks, supported timed effects preserve remaining duration, abilities have the intended gameplay effect, references cannot point to unrelated NPCs/items, destination story remains consistent, and failed stages are recoverable without losing the source.

## 8. Work package: durable loot and inventory authority

Create one durable ownership/quantity ledger for the room. An economic item's logical identity includes its origin/world identity and native instance identity; define imported-item remapping explicitly where native GUID namespaces collide. Class GUID + amount/health cannot uniquely identify an asset. Record owner/container, revision, amount, provenance, condition and required native metadata. Render-only avatar items are outside the economy, unlootable and separately identifiable.

**Proposed durable transfer states:** `Requested -> Reserved -> IntentRecorded -> EngineApplying -> EngineVerified -> LedgerCommitted -> Delivered -> RecipientVerified -> Complete`; failures can enter `Rejected` or `RecoveryRequired`. The journal records complete source/destination preconditions, before/after identities and counts, operation digest and native evidence. State transitions and the ledger are durable before success is exposed.

1. Prove a pre-transfer native hook or controlled stock-UI bridge. Until confirmation, reserve the item and keep it unusable in the destination. Prevent selling, consuming, equipping, dropping or saving an unconfirmed item. Do not implement another optimistic rollback that assumes a spent item still exists.
2. Serialize every host/guest transfer, corpse take/put, loose pickup, drop, split/merge, shop/trade, theft/takedown, consumption and quest-item mutation through authority. Define stack splitting/merging identities and provenance; native stack merging may replace identity and needs an explicit ledger operation.
3. Implement cross-engine delivery using durable escrow if no atomic two-engine transfer exists. Verify source reservation/removal and recipient grant with native readback before releasing escrow. Keep locks/escrow recoverable if either side disappears. A file journal and an engine mutation are not one atomic transaction.
4. On crash after a native call but before recording its result, inspect actual native state and before/after item identities. Complete or compensate only when the observed state proves which outcome happened. Ambiguous state is quarantined; do not replay a grant just because no success record exists.
5. Exact retries return the durable original outcome. A reused ID with a conflicting digest rejects. Connection/load/epoch changes invalidate old uncommitted requests and invoke journal recovery. Persist deduplication across process restarts.
6. Two players attempting the same corpse item get one authoritative owner. A blocked/lost reply does not create a second item. Container/corpse removal, death, NPC streaming and inventory-full failures must respect reservations.
7. Save barriers cannot snapshot an unresolved half-transfer. Drain it, capture escrow/reservations coherently, or abort the checkpoint without discarding the journal.

KCD2's `kdcmp_loot_operations.lua` and scoped Wo134 request/reply code are starting points, not a replacement for this ledger. Bring host-local and non-body paths into the same mechanism. Preserve its existing readback checks and scope tests.

**Exit evidence:** conserved quantities/value and unique active ownership across simultaneous requests, reply loss, duplicates, disconnect, load, restart and each journal crash boundary; no usable unconfirmed assets; exact metadata and quest provenance survive transfer.

## 9. Work package: quests and world-owned consequences

Design the host quest adapter around real quest causes and side effects, not repeated `CompleteObjective` calls that may award rewards or spawn entities again.

1. Inventory all quest state: quest/objective start/completion/cancellation, conditions, script variables, dialogue choices, timers, NPC deaths/spawns, reputation/crime and quest items. Cover side quests, DLC, monastery/tournament/battles, flashbacks, failures and scripted transitions.
2. Host owns narrative decisions and quest/world transitions. Record guest interactions as authorized causes/requests with participant attribution. Define how party members consent/join/stay for scripted scenes and who chooses dialogue branches.
3. Capture guest native changes before irreversible mutation, submit to authority and suppress unauthorized local rewards/spawns. Applying a host commit must not emit a second intent or repeat side effects.
4. Commit a quest revision plus native outcome and side-effect operation IDs. Separate one shared world reward/drop from deliberate personal XP/reward grants; do not accidentally create one economic reward on every machine.
5. Install an authoritative quest snapshot on join/load/streaming repair without replaying every historical completion event. If native snapshot restoration is unavailable, prove a safe staged-save/state restoration path. A quest-state polling log or teleport beside the host is not quest synchronization.
6. Treat dialogue/cutscene transitions as a barrier with ready/abort/exit and epoch checks. Guests choosing stay remain in a consistent shared world; independent solo play starts an explicit branch.
7. Foreign-world quest items and actor references require an explicit selected-world ownership/provenance rule; retain unportable source state in archive rather than deleting it or attaching it to an unrelated quest.

**Exit evidence:** either player can cause supported shared progress, all replicas agree on objective/world state, rewards/spawns/items happen according to the one-commit policy, divergent dialogue/quest causes serialize, and snapshots/reconnect do not repeat historical rewards.

## 10. Work package: one live checkpoint/reconnect lifecycle in both games

Use the existing immutable store and extend its schema/migrations for global ledger, authority revisions, complete roster and capsule provenance. Preserve old manifests/blobs; never rewrite immutable history to make ancestry agree.

**Proposed session states:**

```text
Disconnected -> Negotiating -> SelectingCheckpoint -> Quiescing
 -> Capturing/Importing -> Preparing -> Promoting -> Loading
 -> Verifying -> Ready

Any interrupted stage -> Recovering or Aborted
```

1. Quiesce authority and collect participant acknowledgements. Freeze new economic/quest/combat mutations, settle or capture escrow, and drain the queue through revision R. Transform/cosmetic traffic can pause separately.
2. Capture a verified world plus each participant's complete paired capsule and ledger at R. Include participants who disconnected according to an explicit roster policy. A set of manually chosen newest files is not a consistent barrier.
3. Publish blobs before a manifest that references all of them. Exchange/import bounded manifests and artifacts with digest/build/content/seed/kind verification and ancestry. Include the character/economy data needed for a selected guest checkpoint, not only a host world file.
4. Same checkpoint is no-op. A proved descendant can win automatically under the configured policy. Divergence requires a visible choice of one world/branch; archive both. Hours/wall-clock save time can be displayed as information but are not proof of ancestry. **Do not merge independently progressed quest/NPC worlds in this task.**
5. Reconcile personal capsules against the selected world's ledger. Missing participants, duplicate ownership, incompatible character kinds/seed/content or unknown provenance cannot silently choose another snapshot. Present/recover the specific conflict.
6. Stage personalized saves outside live playlines and verify them. Journal promotion and registry changes. In KCD1, choose an empty or explicitly owned co-op slot or a verified lease; retain the five-slot safety. Give KCD2 an equivalent verified install/recovery transaction.
7. Increment the authority incarnation/epoch as required, invalidate native handle maps and queued old mutations, load the intended checkpoint, and verify actual native world/character/ledger state per participant. KCD1's current playline and personal readback is useful but lacks the full checkpoint/world marker and party barrier; improve correlation beyond an untagged UI event.
8. Store native acceptance and roster state durably. Announce `Ready` only for verified participants under a documented late-join policy. A disk commit, `PREPARED.txt`, generic READY, or successful native call is not the room barrier.
9. On restart, recover pending disk/ledger operations before enabling gameplay. Rejected loads retain originals and allow safe retry/rollback. Cloud or externally added saves are conflicts to preserve, not files to erase. Verification saves do not advance gameplay ancestry or trigger resync loops.
10. When players play independently, fork explicit branches and retain complete personal ownership/effect state. Reconnect chooses one world and restores compatible participants; it cannot invent a merged history. Host disconnect must fail closed until a selected checkpoint and new authority are established; silent split-brain host election is unacceptable.

**Exit evidence:** end-to-end host or guest checkpoint selection, lower/higher progression characters, full-slot refusal/archive recovery, all interruption points, missing/late participants, game/agent/relay restarts, stale messages, Cloud/external-save conflicts and no world-swap loop.

## 11. Bring the complete online feature sets into parity

Use `FEATURE-PARITY.md` as the living acceptance matrix. First inspect and list every user-visible online feature in both repos, including existing settings/UI/installers and the KCD2 native modules. For each, record `absent`, `implemented`, `engine-verified`, `integration-verified`, `human-accepted`, or `unsupported on this engine/platform`, with evidence and exact payload hashes.

Reconcile upward: complete/port the missing behavior through each engine adapter. Preserve KCD2 horse, weather, inventory/economy and dice behavior while bringing equivalent supported behavior to KCD1. If an engine lacks a mechanism, record its evidence and resolve the implementation gap; hiding a toggle or marking both games unsupported does not satisfy requested full parity. Do not port the known KCD1 limitations into KCD2 to equalize feature counts.

For features both already have, unify semantics: late join, ownership, timers, friendly-fire policy, death/respawn, teammate identity, reconnect/solo transitions, story consent, item provenance, unavailable capability messages, menu/pause-menu behavior and key/settings persistence. Share contract fixtures and independently prove adapters. A room must verify native/Lua/agent compatibility; KCD2 protocol 11 alone does not verify its installed pak/plugin.

No single-player release assertion may substitute for two-PC testing. Require equivalent scenario results in each game's acceptance matrix. Document any intended per-game narrative/content differences rather than guessing that game-specific quest IDs or physics must be identical.

## 12. Validation plan and evidence required

Automate contract/serialization/state-machine/fault tests, then prove native hooks in private real engines. Humans do the two/four-computer acceptance stage. Supply explicit test steps, expected states, collection commands/logs and bug-report fields; do not assign them undefined implementation research.

| Test | Required result |
|---|---|
| Simultaneous attacks/kill | One authoritative HP history, one death/corpse, correct attribution/XP, no stale resurrection |
| NPC attacks each player | Correct personal armor/perks/block/status effects and actual damage to each local character |
| Concurrent loot/split/drop/consume/trade | Unique owner and conserved quantities/value; rejected/unconfirmed item never usable |
| Duplicate/out-of-order/conflicting requests | Exact repeat replays durable result; conflict/stale scope rejected before mutation |
| Death/loot/quest reward overlap | One world effect, deliberate personal grants, no duplicate drop/reward after replay |
| Native/agent crash at every transfer boundary | Proven recovery or quarantine with all source/escrow copies preserved |
| Save/load/reconnect at each barrier stage | Consistent manifest/roster/ledger; no half-transfer capture or premature Ready |
| Two independently progressed copies | Explicit one-world selection with both branches archived, compatible personal restoration |
| Poison/bleed/buff/cooldown after different clocks | Proven remaining duration/derived effect, correct paused/offline policy |
| Weak/strong and differently equipped/perked characters | Exact supported restoration without inheriting extra destination progression/items |
| Interiors/distance/streaming/cutscene/mount transitions | Stable ownership and handles, correct actor visibility/state, no independent guest simulation |
| Payload/game/DLC/mod mismatch | Refuse required shared mode before mutating saves/world; specific diagnostic |
| Disconnect/rejoin/host restart | New scopes/incarnation, durable deduplication, no duplicate economy or split authority |
| Fresh/upgrade/uninstall installs | Matched runnable package, preserved saves/settings/archives, verified rollback and slot restoration |
| Multi-hour two/four-PC session | Measured bounded queues/memory/bandwidth/frame cost; gameplay invariants remain true |

Define queue/packet/entity/item bounds from measured engine budgets and expose metrics. Track operation acceptance/rejection reasons, epoch, persistent target IDs, revisions, checkpoint hashes and native readback without logging secrets. Keep failed probes as evidence; do not alter tests/gates to approve unproved behavior. Verify peers' cameras/UI, actual actions, AI, rewards and persistence, not only matching health numbers or bytes.

## 13. Safe local runbook

The user has real KCD1 saves under `C:\Users\seths\Saved Games\kingdomcome\saves`. Do not use them as mutating fixtures. The earlier 260-file hash count is historical; enumerate/hash the actual set before each new engine experiment and compare afterwards. Do not assume the count stays 260 if the user has played since this handoff.

KCD1 cloned source examples are under `C:\Users\seths\Projects\kcd1-coop\_work\e2e\hostsaves\playline0\exit.whs` and `playline4\world.whs`. Preserve them; verify availability/content before reusing. `_work` contains private saves, logs and junctions and is not a distributable asset directory.

From the KCD1 worktree:

```powershell
dotnet test dotnet/KcdUs.sln
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-Engine.ps1
dotnet build tools/engine/NativeStartSmoke
dotnet build tools/engine/LiveWorldSmoke
# Build the test-only ProfileIsolation.vcxproj, Release/x64, first.
$env:KCDUS_PROBE_NATIVE_START = '1'
python tools/engine/engine_session.py launch <verified-cloned-save.whs>
python tools/engine/engine_session.py verify
# Wait for initialization and verify the listener's ownership before loading.
python tools/engine/engine_session.py lua "KCDUS_In('1~LOAD|0|0')"
# After world readiness, run exactly one production smoke agent:
dotnet tools/engine/LiveWorldSmoke/bin/Debug/net8.0/LiveWorldSmoke.dll _work/engine-current.json <verified-cloned-host-world.whs>
```

The harness requires the private folder in the log, matching owned PID/UUID marker, unchanged real saves and the owned remote-console listener. `KCDUS_PROBE_ITEMS=1` conflicts with the runtime adapter's item hook; do not combine them. Native modules install only during owned suspended startup, never by attaching to an arbitrary running game. Do not remove native hash/prologue/lifetime gates to make an experiment run.

Only stop the specifically recorded owned PID after checking its command-line marker and save hashes. Never kill by executable name. Wait for handle release before copying installed paks/DLLs; do not rebuild a running smoke agent's loaded binaries. Never recursively delete/move a shadow game-root tree containing junctions to real game data. Check resolved paths and use `-LiteralPath` for filesystem operations. Do not change firewall rules or click prompts as part of automated tests.

At handoff, the KCD1 harness's last owned game had been stopped; `_work/engine-current.json` had `pid: null`. Check current state rather than trusting this forever. The local own Lua pak was installed, the player's generated menu was revision 5, and the rebuilt development launcher is under `dotnet/KcdUs.Launcher/bin/Debug/net8.0-windows/`. This requires local .NET; a self-contained friend package is still needed.

For KCD2, inspect each legacy probe before running it. `tools/Lua-Driver.ps1` has a stale `D:\SteamLibrary...` log path; the local Modding Tools workspace is on **E:**. Build a verified private-profile/process/API-owner harness equivalent to KCD1 before any mutating engine test. Do not run old scripts with assumed default Continue/save paths. Keep original game data/licenses local and do not redistribute extracted Warhorse assets.

Baseline/build commands from KCD2's worktree:

```powershell
dotnet test dotnet/KcdMp.Client.Tests
dotnet test dotnet/KcdMp.Relay.Tests
dotnet test dotnet/KcdMp.Farkle.Tests
powershell -NoProfile -ExecutionPolicy Bypass -File native/Build-Native.ps1 -Config Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-And-Install-Mod.ps1 -NoInstall
```

Inspect parameters/guards on relevant Lua/native/installer test scripts and run their safe synthetic modes. Observe the Lua 5.1 main-chunk local-variable limit; split new functionality into modules rather than growing the giant startup chunk past the verified limit.

## 14. Commits, local deployment and GitHub publication

Implement in reviewable stages: capability proofs; scope/identity contract; authority NPC/combat; full character codec; durable economy; quest authority; live checkpoint integration; parity and packaging. Keep code, relevant tests and truthful evidence with each stage. Push usable development progress without labeling incomplete work a finished release.

Before each push: inspect status/diff, preserve unrelated work, run appropriate affected tests/native gates, check formatting, remove no user data, and verify no saves, extracted game assets, credentials, private probe logs or generated player UI assets are staged. Own Lua paks/code may be tracked according to repository conventions. Update status/parity/evidence and meaningful commit/PR descriptions.

Explicit own-fork pushes, from the respective worktree:

```powershell
# KCD1
git -c credential.helper= -c 'credential.helper=!gh auth git-credential' push https://github.com/seth-stock/KingdomComeDeliverUs.git HEAD:refs/heads/codex/coop-reliability
# KCD2
git -c credential.helper= -c 'credential.helper=!gh auth git-credential' push https://github.com/seth-stock/KingdomCome-Together.git HEAD:refs/heads/codex/coop-reliability
```

Check `gh auth status`/account and verify each remote branch with `git ls-remote`. If credentials/tooling differ, use the authorized account's normal credential method. No force push and no upstream push. Never assume an `Everything up-to-date` result means the intended branch contains your changes: compare local and remote commit hashes.

Then prepare matched local payloads and friend artifacts for **both** games: launcher, agent/client, relay, native plugin, Lua pak, manifest/settings defaults, compatibility fingerprint and per-game installation guide. Version all layers consistently per game; negotiate the common contract/feature set. KCD1 menu assets must still be generated from the player's own game locally. Respect genuine licensing/distribution requirements; obtain the authentic KCD2 modding terms if applicable.

Use `tools/Build-Installer.ps1` in each repository after inspecting its prerequisites. KCD1's script requires `FEATURE-PARITY.md` and a soak record for the exact version/pak; `-SkipSoakGate` explicitly produces a non-release-grade build and cannot be used as release acceptance. Audit/sandbox old soak automation before running it. Build Linux/Proton packages only after their supported adapters and the same capabilities are proved; do not advertise platform parity from successful packaging alone.

Test a fresh install, upgrade from the previous version, incomplete game-data refusal, wrong payload fingerprint, uninstall/reinstall, restored/archive slots and preserved user settings/saves. Verify launch through the installed launcher, main/pause menus, relay connectivity and the complete multiplayer scenario on the exact packaged bytes. Record SHA-256s and release notes. A runnable self-contained package, not just a source branch or old installer filename, is what friends need.

Report the exact fork/branch/commit, local installed payload hashes, package filenames/download links, passed tests and engine/human scenarios, and any unsupported state/platform. Label pre-acceptance artifacts clearly as development/playtest builds. Do not claim perfect multiplayer, duplication-proof inventory or full parity while the evidence matrix contains required gaps.

## 15. Definition of completion

The task is complete only when both games pass the shared contract and engine gates, have matching supported online feature sets with preserved existing features, restore the supported complete personal state with correct timers/references, run one authoritative NPC/combat/quest/economy history, and recover live saves/reconnects without duplication or loss. Both own GitHub branches must contain the implementation, and both local installations/downloadable packages must match the tested payloads. Human two/four-PC acceptance and soak results must be recorded before the finished multiplayer release claim.

If a specific engine capability cannot be proved, record the failing actual entry path, tested build/hash, reproduction, readback and what deeper adapter work is required. Leave the task explicitly unfinished; do not substitute a presence mod, health mirroring, optimistic item rollback or another implementation plan for the missing behavior.
