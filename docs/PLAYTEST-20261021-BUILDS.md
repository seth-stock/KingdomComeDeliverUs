# Playtest 2026-10-21 build record

Built on 2026-10-20; `playtest-20261021` is the release label.

VERSION remains 0.1.0 and wire protocol remains 4. Install this same build on
both computers. Copying the pak alone does not update the agent. These are
development playtest packages; the frame-rate soak was explicitly skipped
with `-SkipSoakGate`.

The Windows installer build passed all 567 managed/Lua tests, the quest-catalog
drift check, native bridge compilation and the published relay/agent payload
smoke. The native rule executable was run separately and passed all 34 bounded
stream and effect-record checks; the installer script does not run those tests.
Existing compiler warnings remain.

The Linux package was built in a fresh WSL source directory. All 220 manifest
files passed SHA-256 verification, the manifest covers every package file, and
the package contains no symbolic or hard links. Its embedded mod pak matches
the Windows pak byte for byte.

| Artifact | SHA-256 |
| --- | --- |
| `KingdomComeDeliverUs-Setup-0.1.0.exe` | `bbe9f37614e43d1876762cf6e70ebed7c7a7bb709a5cd9c59c1be4c35b0928ba` |
| `KingdomComeDeliverUs-Linux-0.1.0.tar.gz` | `bddb8c5ac18405186eaf69b3bd73831ca6c8045e3460a6fcd38b04335bdac96b` |
| `kcdus.pak` | `764cb9e4a9dbdfeac04688e51ab4c7cb400e59db0c72efbdfb24f2307a4396cf` |

The friends zip contains this installer and the current readme and findings;
its SHA-256 is recorded alongside the other downloadable artifacts in
`release/SHA256SUMS-20261021.txt`.

The private two-game rig on one Windows PC verified the swing animation and a
torch drop, receipt, native pickup and host confirmation. See
`PLAYTEST-20261021.md` for exact evidence. This is not two-computer proof.
The stock type-2 item-handle fallback is limited to the current load's pack
membership, with persistent UUIDs preferred when the bridge supplies them.
Synthetic tests cover its pickup and rejection cases. A separate bridge-free
Windows probe loaded the exact current Lua and verified a stock type-2 handle,
native torch pickup and exactly one guest request. The final valid evidence
follows `stock-new-scope` in ignored `_work/stock-pickup-live.log`; earlier
attempts in that log used a stale installed pak and do not prove this path.
The probe's 314-save guard passed before owned-process cleanup. This handle
matching is not durable identity or actual Proton/Bazzite gameplay proof.
Actual Proton/Bazzite gameplay remains untested. The Linux native adapter and
Windows-only behaviors remain unproved there. Swings are cosmetic; independent
native damaging combat, complete quest-state synchronization, durable economy
checkpoints/item lineage and arbitrary native character restoration remain
unfinished. Cinematics use notices and optional go-to-host; KCD1 cannot replay
the host's cinematic on the guest.

Build and verification logs remain under ignored `_work`; game data, saves and
Ghidra exports are not release assets. Packaging never runs the installer.
