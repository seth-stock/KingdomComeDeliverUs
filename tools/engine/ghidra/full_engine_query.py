"""Inspect/audit private full-engine exports without loading them all in RAM.

--audit verifies every committed JSONL record against the function inventory.
--match searches names and C text; --address selects a Ghidra virtual address.
No output is suitable for redistribution: all exports stay under _work.
"""
from __future__ import annotations
import argparse
import csv
import json
from pathlib import Path
import sys


def records(folder):
    for shard in sorted((folder / "functions").glob("shard-*.jsonl")):
        with shard.open(encoding="utf-8") as stream:
            for number, line in enumerate(stream, 1):
                try:
                    record = json.loads(line)
                except Exception as exc:
                    raise ValueError(f"Invalid committed record {shard}:{number}") from exc
                yield shard, number, record


def audit(folder):
    inventory = folder / "functions.tsv"
    if not inventory.exists():
        return {"module": folder.name, "phase": "analysis-or-not-started", "complete": False}
    with inventory.open(encoding="utf-8") as stream:
        functions = {row["address"]: row for row in csv.DictReader(stream, delimiter="\t")}
    seen = set()
    ok = failures = warnings = 0
    for shard, number, record in records(folder):
        address = record["address"]
        if address in seen or address not in functions:
            raise ValueError(f"Duplicate or foreign function {shard}:{number} {address}")
        if int(functions[address]["body_bytes"]) != record["body_bytes"]:
            raise ValueError(f"Function body changed since export {shard}:{number} {address}")
        seen.add(address)
        if record["status"] == "ok":
            if not record["c"].strip():
                raise ValueError(f"Empty successful decompilation {shard}:{number}")
            ok += 1
            if record.get("error") or "WARNING:" in record["c"]:
                warnings += 1
        elif record["status"] == "failed":
            failures += 1
        else:
            raise ValueError(f"Invalid status {shard}:{number}")
    all_attempted = len(seen) == len(functions) and (folder / "export-completed.txt").exists()
    return {"module": folder.name, "functions": len(functions), "attempted": len(seen),
            "ok": ok, "failed": failures, "warning_functions": warnings,
            "all_discovered_attempted": all_attempted,
            "complete": all_attempted and failures == 0,
            "note": "Complete here means discovered functions exported, not native semantics or all executable bytes proven."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--match")
    parser.add_argument("--address")
    parser.add_argument("--limit", type=int, default=20)
    parser.add_argument("--write", type=Path, help="write matching private source into an ignored directory")
    args = parser.parse_args()
    if args.audit:
        if (args.folder / "binary.json").exists():
            modules = [args.folder]
        elif (args.folder / "inventory-all.json").exists():
            expected = json.loads((args.folder / "inventory-all.json").read_text())
            modules = [args.folder / row["identity"] for row in expected]
        else:
            modules = [p for p in sorted(args.folder.iterdir()) if (p / "binary.json").exists()]
        values = [audit(p) for p in modules]
        print(json.dumps(values, indent=2))
        if not values or any(not value["complete"] for value in values):
            sys.exit(2)
        return
    if not args.match and not args.address:
        parser.error("--audit, --match or --address required")
    if args.write:
        if "_work" not in args.write.resolve().parts:
            parser.error("Local engine source inspection output must stay under _work")
        args.write.mkdir(parents=True, exist_ok=True)
    wanted = int(args.address, 16) if args.address else None
    count = 0
    for shard, number, record in records(args.folder):
        if wanted is not None and int(record["address"], 16) != wanted:
            continue
        if args.match and args.match.casefold() not in (record["name"] + "\n" + record["c"]).casefold():
            continue
        print(json.dumps({k: v for k, v in record.items() if k != "c"}) + f" {shard.name}:{number}")
        if args.write:
            (args.write / (record["address"] + ".c")).write_text(record["c"], encoding="utf-8")
        count += 1
        if count >= args.limit:
            break


if __name__ == "__main__":
    main()
