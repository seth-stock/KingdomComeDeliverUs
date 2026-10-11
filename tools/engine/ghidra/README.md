# Full offline engine analysis

This is the Session 9 research workflow. It analyzes complete installed engine
images before the next native integration work. It does not run either game,
enable Candidate features or prove online behavior.

Run from either reliability worktree on this Windows machine:

```powershell
python tools/engine/ghidra/full_engine_analysis.py --inventory
python tools/engine/ghidra/full_engine_analysis.py
```

The matrix contains KCD1's complete WHGame image, KCD2's complete retail WHGame
image, the separate Modding Tools gameplay modules, and that build's supporting
Cry engine DLLs. Third-party libraries, editors, test programs and ARM binaries
are excluded. Retail KCD2 and its split Modding Tools build are different native
targets; current adapter offsets must not be applied across them.

All imports, binary hashes, project databases, C exports and diagnostics remain
in ignored `_work/ghidra-full-session9`. Never distribute those files, game code
or assets. The source tools alone belong in the repositories.

One engine image runs at a time, using Ghidra at E:\Ghidra and JDK 25. A named
Windows mutex excludes a second controller in either worktree. The default
limits are two CPUs, a 6 GiB Java heap, 512 MiB direct memory, 768 MiB metaspace,
and 10 GiB committed memory across the owned Windows job and native decompiler
children. Start requires 12 GiB free physical RAM; a drop below 4 GiB stops only
that owned job. `controller.json` records the exact child PID, module, peak job
commit, available physical RAM and log. No process is selected for termination
by name. Closing the controller closes its job and stops its own children.

Whole-image discovery, references, RTTI, switches and stack analysis remain on;
PDB symbol-server analysis is off. The first KCD1 pass exhausted its bounded
6 GiB heap in the optional whole-image Create Address Tables analyzer's address
set intersection. That heuristic is disabled for the retry; native RTTI/vtable
and instruction-reference analysis remain enabled. This is a recorded analysis
limitation, not an exclusion of engine images or functions from decompilation.
A two-hour per-image analysis timeout is an explicit failure. Import, analysis
and export use separate headless invocations so failures do not discard earlier
phases. CLI success alone
is insufficient: the controller requires the script markers and unchanged input
SHA-256. It refuses reuse after an installed binary changes.

The exporter attempts every internal function discovered by Ghidra, including
thunks, with an eight-second per-function timeout. Atomic, flushed batches of
256 JSONL records make export resumable. A crash can repeat an uncommitted batch;
it cannot mark a partial batch complete. Cache flushes and periodic decompiler
replacement bound retained native state. Slow functions get one 30-second retry.
Failed functions and warnings remain
in the record. `attempted-all-discovered` does not mean all functions succeeded,
and discovery can miss code. `discovery.json` reports executable-byte coverage
so that gaps can be investigated rather than hidden.
Symbols, decoded strings and native cross-references are exported separately for
call-chain and provenance inspection. The controller audits each completed export.

```powershell
python tools/engine/ghidra/full_engine_query.py _work/ghidra-full-session9 --audit
python tools/engine/ghidra/full_engine_query.py _work/ghidra-full-session9/kcd1-game --address 1806ac280 --write _work/native-review
```

The audit reads shards one at a time, validates their JSON and unique addresses
against the saved function inventory, and exits 2 while analysis/export/failures
remain. Before integration, resolve failed exports and executable gaps relevant
to the feature, verify signatures and layouts in assembly and independent call
sites, then test only in owned isolated game profiles. Decompilation is evidence
for investigation, never proof of a correct multiplayer adapter.

Current results and failures must be recorded separately from capabilities.
Existing targeted tools remain useful for deeper follow-up after full analysis;
their former noanalysis/2 GiB settings are for that smaller workflow.

`prepare_unwind.py` also exports validated x64 exception/unwind ranges locally.
It distinguishes primary procedures from chained fragments, checks bounds and
cycles, and reports unsupported records. It does not merge functions or assume
every byte in an exception range is an instruction. Use this metadata to review
bad function boundaries alongside actual control flow, preserving raw exports.

`BoundedSwitchRepair.java` is a follow-up for **copied** research projects only.
It validates an imported binary SHA-256 and a local TSV of relative DWORD jump
tables, optionally preceded by byte remaps. It limits each table to 256 entries,
the code region to 20 KiB and the invocation to eight rows, with 30-second export
timeouts. Use the same owned-job controller with a 2 GiB heap and two CPUs. Its
manual switch overrides and guessed signatures still require assembly review;
successful C output must not erase raw failures or promote engine capabilities.
