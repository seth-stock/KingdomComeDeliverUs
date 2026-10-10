# Session 8 builds and publication

2026-10-10. Full multiplayer remains unfinished; read SESSION8-RESULTS.md. The builds below are development playtests with skipped soak gates, private Candidate native integrations, and no human acceptance.

Release tag `playtest-20261010-session8` points to implementation commits:

* KCD1: `445e1e9a5e21dd0a8cbe7110fe4cb23942804873` on the seth-stock/KingdomComeDeliverUs fork.
* KCD2: `2b6cf09b22b595760320f25d62b5e09c7591221f` on the seth-stock/KingdomCome-Together fork.

Both implementation commits were pushed to `codex/coop-reliability` by explicit fork URL, without force. No upstream repository was pushed. Documentation commits after these tags do not change the tagged binaries.

[KCD1 development pre-release](https://github.com/seth-stock/KingdomComeDeliverUs/releases/tag/playtest-20261010-session8)

[KCD2 development pre-release](https://github.com/seth-stock/KingdomCome-Together/releases/tag/playtest-20261010-session8)

Each is a new pre-release, not marked latest. Existing tags and assets remain unchanged. All eight public assets, including the two checksum manifests, were downloaded anonymously and independently verified with streaming SHA-256. The digests below are also in each release's SHA256-session8.txt.

| Artifact | SHA-256 |
|---|---|
| KingdomComeDeliverUs-Setup-0.1.0.exe | `922c89f1f374bbd047700f66e58bbdf313e3c78c9ff2bd4afdac2be8302bb6f1` |
| KingdomComeDeliverUs-playtest-20261010-session8.zip | `74494eb928061cf7bd76a9a17071088e3d7100ab60b27d011c88e2f550a38747` |
| KingdomComeDeliverUs-Linux-0.1.0.tar.gz | `03f7753bc130d43dfbe1c5ba7c2952cdb65b9da745ec634fc8bbf06aca72fbda` |
| KingdomComeTogether-Setup-0.45.0.exe | `267aad07d59d183adb69d940bb62a71a86e8636e7942b5e7982e4ec4e0b4b8b1` |
| KingdomComeTogether-playtest-20261010-session8.zip | `5f97c7a217afed02dd7acf1997eb0ed3a2b5fc55a8b7029c9891065bf6d7dcb1` |
| KingdomComeTogether-Linux-0.45.0.tar.gz | `015003050f989662e536c619daad42cd672615f3f4df8b1662f808ecaa687d9f` |

Both Windows installers passed their required build/payload gates (KCD1 -SkipSoakGate, KCD2 -PlaytestBuild). KCD2 also passed 74 relay checks, 434 native checks, all 44 Lua synthetic suites and three static checks. The updated item suite has 87 checks; container/reward parity has 40. KCD1 has 393 managed/Lua tests and 25 native bounded-stream/schema checks.

Linux archives passed internal file hashes, fake-Steam doctor/link/install checks, canonical Windows/Linux KCD2 pak equality and native Linux relay smoke tests. Real Proton gameplay/injection remains unproved; KCD1 Windows-only native adaptations are not thereby supported on Linux.

These artifacts are in each worktree's ignored `release` folder. The real installed mods/programs remain session 6: the session-8 packages were not installed into the real games. The owned private game was stopped, all 401 real-save baseline files remained unchanged, and firewall rules and VERSION were not changed. Native KCD1 adapter compiled SHA-256: `08211279fee3de8a3deda108c7b26e9918dde6ef5af321f54fef6282bbbeb5e8`.
