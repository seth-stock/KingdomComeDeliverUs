# Continuation build record ? 2026-10-11

Development packages for `KCD1`, branch `codex/coop-reliability` on
`seth-stock/KingdomComeDeliverUs`. The four requested features remain incomplete;
see [CONTINUATION-RESULTS.md](CONTINUATION-RESULTS.md).
VERSION was not changed. No human multiplayer, Proton gameplay or frame-rate
soak acceptance is claimed.

## Validation

| Check | Result |
|---|---|
| KCD1 managed/Lua | 446 passed |
| KCD1 native bounded-stream/schema | 25 passed; exact-engine bridge compiled |
| KCD2 client | 1,345 passed |
| KCD2 real-relay tests | 74 passed |
| KCD2 native tests | 434 passed |
| KCD2 Lua suites/static checks | 44 suites and three checks passed |
| Windows packaged agent/relay smoke | Passed for both games |
| Linux package hashes/fake-Steam doctor/link/install | Passed for both games |
| Canonical Windows/Linux Lua pak equality | Passed for both games |
| Native Linux agent/relay startup and handshake | Passed; KCD2 wire 16 |
| Real save baseline | All 401 files unchanged |
| Full-engine export audit | All 910,489 discovered functions attempted; 16 failures retained |

Logs, TRX files, the export audit and artifact metadata remain in ignored
`_work/continuation`. Full Ghidra databases/shards and original analysis logs
remain in the KCD1 worktree's ignored `_work/ghidra-full-session9`; no repeat
decompilation was performed. Private engine code and game binaries are excluded
from commits. Windows builds used their explicit development/skip-soak switches.

## Local artifacts

Matching files are in `release/continuation-20261011`, with `SHA256SUMS.txt`.
The Windows zip contains the installer and continuation/test guides. The Linux
archive carries self-contained agents/relays and matching Lua; KCD2 also carries
the Windows plugin/injector for Proton. These are local development outputs,
not a declaration that the missing engine integrations work.

| Artifact | SHA-256 |
|---|---|
| `KingdomComeDeliverUs-continuation-20261011.zip` | `2178aa54077e19c9507cb226c44a9c75cda00422b74560cb4e0bb0935feac7a5` |
| `KingdomComeDeliverUs-Linux-0.1.0.tar.gz` | `2fad2cb74fcc53f6391adaf206baf3f1e7ed8622a212e6123bb3934cb71db558` |
| `KingdomComeDeliverUs-Setup-0.1.0.exe` | `fe871580fa1950e795ba3b268268a41222385521a670275b26b437c080f3283e` |

The earlier intermediate test failures and build attempts are preserved in the
logs. The world-offer race was fixed by reannouncing on roster changes. Relay
refusal now publishes disconnected state before its final refusal notification.
Final package gates pass; these checks do not prove native damage/quest effects
or arbitrary reference restoration.
