# KCD1 engine integration evidence, 6 October 2026

This development branch does not yet implement full shared simulation or exact
inventory/equipment restoration. The normal Henry card remains additive. No
experimental player proxy is enabled in the runtime mod.

## Implemented progression preparation

`KcdUsAgent --prepare-exact-traits --character-save <source.whs> --world-save
<destination.whs> --output <new staged.whs>` prepares a separate save with the
source Henry's stat XP, skill XP and perk-state bytes. It preserves the
destination's world, inventory, equipment, resources, identity and other soul
fields. Story XP field 8 stays with the destination. Sparse XP arrays mean zero
earned XP, not permission to retain a destination character's higher XP.

The tool validates the container checksum, 32 KB framing, stream version 20,
canonical Henry GUID/entity identity, soul counts, field uniqueness and XP
array bounds. It preserves unknown fields and the opaque trailer, updates
ancestor lengths and checks the prepared file by reading it again. It creates a
new output and refuses existing files, the default live-save directory and
output through junctions. It does not install the result or change the normal
joining/character-card flow.

In the installed retail 1.9.8 engine, a disposable early Henry's progression was
prepared into a cloned 52-hour world. That world loaded at its original position
and world time. Readback showed strength/agility/vitality 1, speech 3, and
reading/sword/stealth/herbalism 0. Reading was 3 in the original destination.
The engine wrote another save successfully. Its skill XP and perk bytes matched
the source and staged save exactly; its stat XP matched the source after the
engine removed explicit zero entries. This establishes this particular
progression splice, not general save compatibility or full character transfer.

Still required: fresh character snapshot capture in the agent, immutable paired
checkpoint storage, transactional promotion, engine acknowledgements and
recovery, skill/ability-flag coverage, inventory/equipment references and
duplicate-item handling. Perk transfer also needs tests with deliberately
different chosen perks and their gameplay effects.

## Walking prototype and rejected routes

The retail `human:PlayAnim` binding requires **two strings**, fragment and tag.
The installed animation database supplies `MotionMovement`, `MotionIdle` and
walk/run tags. One-argument calls do not implement walking.

An engine-created `Player` actor on disposable channel 77 reached
`MotionMovement` and returned to `MotionIdle`. The original Henry, his soul and
loaded-world readiness remained intact. The engine exposed `ENTITY_FLAG_NO_SAVE`
as 32768; the flagged proxy's name did not appear in the test save, and the
saved soul count remained 2673.

This route is **not safe to enable**: `System.RemoveEntity` left the channel
lookup populated and the owned process subsequently exited unexpectedly.
Lifecycle cleanup and repeated join/leave/load tests are unresolved. A
channel-backed ordinary `NPC` also crashed when attempting animation. The
legacy AI goal-pipe experiment crashed. These probes are retained as negative
evidence and must not be used in normal play.

`SetPhysicalizationProfile('none')` returned without an error but readback stayed
`alive`; it does not establish collision-free proxies. Ordinary NPC
`SimulateOnAction`/animation calls did not establish reliable locomotion.

## Exact characters, outfits and shared simulation

Retail Lua has no observed level setters or perk enumeration. Adding negative
reading XP raised the disposable Henry from level 3 to 20; never use negative XP
to lower a character. Read-only inventory enumeration exposes instance, class,
condition and amount but did not establish equipped-slot enumeration.
`MakeLookAsActor` is an in-process appearance operation, not a remote outfit
capture/restore protocol.

Shared combat, NPC ownership, quests and loot still lack a proven authoritative
engine backend. State transport, receiving saves and displaying remote bodies
do not establish those features. They need input/damage interception,
authoritative actor lifecycle, stable identities, inventory arbitration and
two-computer tests before release.

## Isolation and reproducibility

All mutations above used cloned saves under ignored `_work` directories.
The owned test process reported its private save directory before probes ran;
all 260 real `.whs` save hashes remained unchanged. A test-only native shim
redirects the process's Saved Games lookup before engine initialization. Game
executables and DLLs on disk were not patched. The game's ordinary `+sys_user_folder`
override was too late to isolate this retail build.

See [the harness instructions](../tools/engine/README.md). Probe scripts are
development tools, not packaged runtime modules. DLL build output, game data,
saves, screenshots, logs and session records must remain excluded from Git.
