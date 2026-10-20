# Playtest 2026-10-21 build record

Built on 2026-10-20; `playtest-20261021` is the release label.

VERSION remains 0.1.0 and wire protocol remains 4. Install this same build on
both computers. Copying the pak alone does not update the agent. These are
development playtest packages; the frame-rate soak was explicitly skipped
with `-SkipSoakGate`.

The Windows installer build passed all 558 managed/Lua tests, the quest-catalog
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
| `KingdomComeDeliverUs-Setup-0.1.0.exe` | `4c8c1809c4fdee21da959f53e1bd111b8dd370c6bf9f942f9aa08d3dfd84dbd8` |
| `KingdomComeDeliverUs-Linux-0.1.0.tar.gz` | `eecbff5413bd8f8311599b7b2ae1fd040907aeb64565d06b3c1c8592634c7b1b` |
| `kcdus.pak` | `61f09c8160134854b41d7404ace41e2724983d55d9ba62cdd7254bd89e24771a` |

The friends zip contains this installer and the current readme and findings;
its SHA-256 is recorded alongside the other downloadable artifacts in
`release/SHA256SUMS-20261021.txt`.

The private two-game rig on one Windows PC verified the swing animation and a
torch drop, receipt, native pickup and host confirmation. See
`PLAYTEST-20261021.md` for exact evidence. This is not two-computer proof.
Actual Proton/Bazzite gameplay remains untested. The Linux native adapter and
Windows-only behaviors remain unproved there. Swings are cosmetic; independent
native damaging combat, complete quest-state synchronization, durable economy
checkpoints/item lineage and arbitrary native character restoration remain
unfinished. Cinematics use notices and optional go-to-host; KCD1 cannot replay
the host's cinematic on the guest.

Build and verification logs remain under ignored `_work`; game data, saves and
Ghidra exports are not release assets. Packaging never runs the installer.
