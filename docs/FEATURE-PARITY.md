# Multiplayer feature parity and acceptance contract

Updated 2026-10-07. Applies to Deliver Us (KCD1) and Together (KCD2) development branches. Read `CLAUDE-MULTIPLAYER-HANDOFF.md` for implementation design, source paths, engine constraints, validation and publishing instructions.

**Parity target:** equivalent supported online behavior within each game, with separate game-specific engine/save adapters. Room `gameId` must match. Bring the smaller feature set up to the full union of existing online features; removing or hiding a working feature is not parity. Human acceptance remains outstanding in both games.

Statuses below describe evidence available at handoff, not a new gameplay audit. **KCD2 source** means relevant code exists; verify its exact runtime path and historical evidence before upgrading the status. **KCD1 engine** means behavior was seen in a disposable single real game, sometimes with a synthetic sender; it does not mean two-person acceptance.

| Online feature | KCD1 current evidence | KCD2 current evidence | Required common result / remaining work |
|---|---|---|---|
| Host/join/leave, room/password, chat | Implemented; menu/agent/relay observed | Source and existing tests | Real clients connect/disconnect/rejoin, authenticated participant binding, correct room/version rejection |
| Main-menu multiplayer UI | Engine observed; locally generated menu revision 5 | Main-menu entry/pages/settings observed in earlier work | Installed launcher/agent end-to-end; settings persist and required capabilities are visible |
| Pause-menu UI and actions in a live session | Audit current menu integration | Not established by earlier main-menu proof | Both titles expose working host/join/leave/status/world controls during supported live sessions |
| Settings, keys and controllers | Browser settings and configurable join/stay hotkeys; audit controller paths | Launcher settings and native keybind source; controller behavior needs verification | Equivalent accessible settings and actions in both games, persisted preferences and verified keyboard/controller navigation; preserve each existing UI |
| Peer identity and position | Ordinary NPC presence bodies follow peers | Native/Lua source | Persistent participant identity, per-load handles, late join/removal, no stale-handle mutation |
| Walk/idle/run/sprint | KCD1 engine proof on ordinary NPCs | Source/native motion | Correct visible gait and smoothing in two/four-player gameplay and after load/streaming |
| Weapons, attack/block and other poses | Walking does not prove combat; current bodies noncombat | Native combat/motion source | Real remote actors participate in hit tests/AI targeting with correct per-player state and cleanup |
| Equipment/outfits | Equipped classes/condition transport and rendering observed on supported Windows adapter | Source; audit actual supported fields | Native gear and visual state agree; economic gear stays separate from unlootable render-only proxies |
| Dirt/appearance/hair and character kinds | Incomplete beyond equipment/common Henry | Audit appearance and Henry/Godwin paths | Defined supported appearance/kind contract; correct transitions without arbitrary source block copying |
| NPC ownership and AI | Shared simulation absent; persistent identities readable | Native scan/drive, claims and validation source | One authority/epoch/revision, stable identities, safe suppression/replay and AI targeting all players |
| NPC streaming and distant players | No shared region simulation | Audit host-range/claim behavior | Proved union-of-interest simulation or an explicit enforced supported-area policy; no silent solo regions |
| Shared health/stamina/damage | Only setter readback proved; tested DealDamage calls had no effect | Combat authority code exists; incomplete coordination | Ordinary attacks validated by host, correct character stats/armor/perks, reliable outcomes before side effects |
| Death, kill credit, assists and drops | Absent | One-health/death-authority gap remains | One death revision/corpse/reward history; simultaneous kills cannot duplicate loot/XP or revive NPCs |
| Player injury/death/respawn/friendly fire | Presence bodies invulnerable/AI-invisible | Source/options; audit policy | NPC attacks affect either player, consistent configurable friendly fire and recoverable personal death policy |
| Shared quests/objectives/variables | Polling/story consent, not quest mirroring | Quest source; audit side-effect authority | Host decisions and attributed guest causes yield one quest/world history without duplicate rewards/spawns |
| Full quest/DLC coverage and compatibility | Earlier catalogue classified 287 quests including five DLCs; classification is not shared simulation | Earlier catalogue classified 201 quests; DLC source needs runtime audit | Audit installed title-specific main/side/DLC coverage, reject incompatible required content and validate quest mutations and scripted character transitions |
| Dialogue, scripted scenes, join/stay and tether | Implemented presence/story controls | Source | Consistent choice, entry/exit/barrier behavior, late join and branch policy; source quest snapshots are authoritative |
| Time of day | Forward-only behavior observed/documented | Time source | Same declared clock semantics; no pretend rollback; authoritative effect clocks independent of cosmetic clock sync |
| Weather | Not shared | Native weather source | Proved equivalent supported weather synchronization or an explicitly unresolved adapter gate |
| Horse/mount ownership, riding and saddlebags | Shared horse simulation absent; character capture refuses mounted state | Native/Lua horse source and probe tooling | Safe mount/horse control, participant ownership, inventories, dismount/death/streaming and reconnect |
| Dice/Farkle | No dice synchronization | Farkle source; 59 recorded unit tests | Shared game/turn/randomness/wager authority and correct native UI integration; no duplicated economic rewards |
| Exact personal XP/stats/skills | Live saved-state staging and native readback observed; levels can decrease | Save splice/character source | Correct progression/accounting for each participant through joins, deaths, saves, solo branches and reconnect |
| Selected perks, points and abilities | Perk records survive native saving; effective gameplay/other abilities not fully proved | Audit character/block and native effects | Exact selected perks/accounting and effective derived abilities; no inherited destination-only bonuses |
| Health/energy/nourishment and effects | Portable resource core loaded; arbitrary effects/timers incomplete | Audit complete physiology/timers | Correct health/stamina/resources, injuries/poison/bleed/buffs/cooldowns with proven clock conversion |
| World-linked character state | Nonempty core reference refused; other state preserved in destination provisionally | Seed/kind checks; audit reference policies | Explicit world/personal/derived classifications; valid actor/item remapping and preserved source on unsupported transitions |
| Inventory/equipment restoration | Native record replacement observed; item ordering normalized; cross-participant ledger absent | Character splice and ledger source | Exact legitimate ownership/counts/metadata; host-Henry cloning cannot duplicate economic items |
| Corpse/container take/put | Shared loot authority absent | Scoped in-memory body retry cache and host readback | Pre-transfer control/escrow, one authority queue including host-local moves, durable unique ownership and deduplication |
| Loose pickup/drop/split/merge/consume/equip | Shared arbitration absent | Some source/regression suites; not complete durable authority | Every ownership/quantity mutation shares the ledger; unconfirmed items cannot be used or saved |
| Shops/trade/theft/takedowns/quest items | No shared economy | Audit source and uncovered pathways | Correct provenance, conservation and quest/crime consequences through the same durable operation system |
| World save/load and bring Henry | Live KCD1 install/native-acceptance path observed | Existing joins/rejoin plus offline reconciliation preparation | Consistent world/roster/character/ledger barrier, correlated native acceptance and journaled promotion |
| New world together and prologue | Announcement/New Game binding exists; native saving refuses the unsaveable prologue | Fresh-character join source | Define shared genesis, story authority and supported pre-first-save behavior; respect native save restrictions without mislabeling independent prologues as shared simulation |
| Reconnect: one winning world, restore players | Legacy hours/time resolution; immutable store not integrated into live ancestry | Manifest selection/staging tested offline; live promotion missing | Exchange/import paired checkpoints; descendant selection or explicit divergent choice; archive both; no independent-world merge |
| Sending character home and solo branching | Portable native preparation implemented; active state/ref limits remain | Audit equivalent world/character lifecycle | Explicit branch/home identity and recoverable, legitimate personal-state/economy transfer |
| Save-slot protection/archive/restore | Five-slot refusal and offline leasing implemented | Audit slot/profile behavior | Preserve original saves and logical home bindings; usable slot UI/recovery; no unsupported removal of physical limits |
| Process/load/connection restart isolation | Load journal/validation; no global simulation epoch | Loot correlation scopes; global epoch incomplete | Whole authority incarnation/epoch on all world mutations, queued old operations cancelled and durable recovery |
| Content/native/Lua/agent compatibility | Exact Windows native hash gate; no complete room content profile | Protocol 11; native/pak fingerprint gap | Matched packages, DLC/mod/content profiles and required capabilities verified before shared mutable gameplay |
| Platform parity | Windows identity/outfit adapter; Linux runtime adapter not proved | Linux packaging/native behavior needs audit | Publish only platforms that pass the same engine/online acceptance; packaging alone is insufficient |
| Installation, upgrades and distribution | Current dev build local; complete new release/soak/package missing | Missing-game-data detection tested; matched release payload/upgrade gates missing | Runnable matching self-contained packages, genuine applicable terms, preserved settings/saves and tested upgrade/rollback |

## Acceptance record to maintain

For every row, record both games' source commit, engine and content hashes, launcher/agent/relay/native/Lua hashes, contract/wire versions, test scenario, observed outcome and evidence path. Separate unit, synthetic integration, real-engine and human multiplayer results. A known limitation remains a failed parity target until implemented and validated or explicitly excluded by the user.

The gameplay invariant is one authoritative NPC/combat/quest/economy history per room and epoch, with independently controlled, correctly restored personal characters. Never mark true multiplayer complete based only on visible peer bodies, matching health numbers, immutable offline files or successful pcall.

## Human acceptance minimum

- Two computers with distinct characters, stats/perks and inventories; then a four-player session.
- Simultaneous combat kills and looting, NPC attacks targeting every participant, normal stock UI/equipment/shop/quest interactions.
- Different world clocks and timed effects, scripted transitions, interiors/distance/mounts and re-entry.
- Save/load/reconnect, host or guest checkpoint winning, independent solo branches, dropped/duplicated/reordered messages and interrupted operations.
- Multi-hour gameplay and install/upgrade/uninstall/reinstall on the actual packaged payloads.

Implementation and native adapter proof must precede these tests. Give humans concrete steps and expected results; do not ask them to compensate for missing code or to certify undocumented behavior.

The earlier KCD1 parity document is retained in Git history. Its claims that KCD1 could never synchronize animation/outfits/perks or support a native backend are superseded by the current implementation/evidence. Its broad KCD2 multiplayer claims must be traced to actual dated scenarios and package hashes before treating them as acceptance evidence.
