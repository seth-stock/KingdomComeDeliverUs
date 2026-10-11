"""Offline, sequential, memory-bounded Ghidra analysis of installed engines.

Only ignored _work receives binaries' metadata, projects and decompiler output.
This tool never launches a game, edits a save, installs a mod or changes a firewall.
Run --inventory first, then without --inventory to analyze/export the matrix.
"""
from __future__ import annotations
import argparse
import atexit
import ctypes
from ctypes import wintypes as w
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time
from full_engine_query import audit

GIB = 1024**3


class MemoryStatus(ctypes.Structure):
    _fields_ = [("length", w.DWORD), ("load", w.DWORD)] + [
        (name, ctypes.c_ulonglong) for name in
        ("total", "available", "page_total", "page_available", "virtual_total", "virtual_available", "extended")]


class BasicLimit(ctypes.Structure):
    _fields_ = [("process_time", ctypes.c_longlong), ("job_time", ctypes.c_longlong),
                ("flags", w.DWORD), ("min_working_set", ctypes.c_size_t),
                ("max_working_set", ctypes.c_size_t), ("active_processes", w.DWORD),
                ("affinity", ctypes.c_size_t), ("priority", w.DWORD), ("scheduling", w.DWORD)]


class IoCounters(ctypes.Structure):
    _fields_ = [(name, ctypes.c_ulonglong) for name in
                ("read_ops", "write_ops", "other_ops", "read_bytes", "write_bytes", "other_bytes")]


class ExtendedLimit(ctypes.Structure):
    _fields_ = [("basic", BasicLimit), ("io", IoCounters),
                ("process_limit", ctypes.c_size_t), ("job_limit", ctypes.c_size_t),
                ("peak_process", ctypes.c_size_t), ("peak_job", ctypes.c_size_t)]


def kernel():
    dll = ctypes.WinDLL("kernel32", use_last_error=True)
    dll.CreateJobObjectW.argtypes = (ctypes.c_void_p, w.LPCWSTR)
    dll.CreateJobObjectW.restype = w.HANDLE
    dll.SetInformationJobObject.argtypes = (w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD)
    dll.SetInformationJobObject.restype = w.BOOL
    dll.AssignProcessToJobObject.argtypes = (w.HANDLE, w.HANDLE)
    dll.AssignProcessToJobObject.restype = w.BOOL
    dll.QueryInformationJobObject.argtypes = (w.HANDLE, ctypes.c_int, ctypes.c_void_p, w.DWORD, ctypes.c_void_p)
    dll.QueryInformationJobObject.restype = w.BOOL
    dll.TerminateJobObject.argtypes = (w.HANDLE, w.UINT)
    dll.TerminateJobObject.restype = w.BOOL
    dll.CloseHandle.argtypes = (w.HANDLE,)
    dll.CreateMutexW.argtypes = (ctypes.c_void_p, w.BOOL, w.LPCWSTR)
    dll.CreateMutexW.restype = w.HANDLE
    dll.WaitForSingleObject.argtypes = (w.HANDLE, w.DWORD)
    dll.WaitForSingleObject.restype = w.DWORD
    dll.ReleaseMutex.argtypes = (w.HANDLE,)
    dll.GlobalMemoryStatusEx.argtypes = (ctypes.POINTER(MemoryStatus),)
    return dll


def free_memory(dll):
    info = MemoryStatus()
    info.length = ctypes.sizeof(info)
    if not dll.GlobalMemoryStatusEx(ctypes.byref(info)):
        raise ctypes.WinError(ctypes.get_last_error())
    return info.available


def claim_controller():
    dll = kernel()
    mutex = dll.CreateMutexW(None, False, "Local\\KCD-Coop-FullEngineAnalysis")
    if not mutex:
        raise ctypes.WinError(ctypes.get_last_error())
    result = dll.WaitForSingleObject(mutex, 0)
    if result not in (0, 0x80):  # acquired / prior owner exited
        dll.CloseHandle(mutex)
        raise RuntimeError("Another co-op full-engine analysis controller is already running")
    def release():
        dll.ReleaseMutex(mutex)
        dll.CloseHandle(mutex)
    atexit.register(release)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def atomic_json(path, value):
    temp = path.with_suffix(path.suffix + ".tmp")
    with temp.open("w", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2)
        stream.flush()
        os.fsync(stream.fileno())
    for attempt in range(10):
        try:
            os.replace(temp, path)
            break
        except OSError as exc:
            # Windows readers can briefly deny replacement while inspecting
            # progress; do not kill a long analysis over a transient sharing lock.
            if getattr(exc, "winerror", None) not in (5, 32) or attempt == 9:
                raise
            time.sleep(0.05)


def matrix():
    k1 = Path("F:/SteamLibrary/steamapps/common/KingdomComeDeliverance/Bin/Win64")
    retail = Path("E:/SteamLibrary/steamapps/common/KingdomComeDeliverance2/Bin/Win64MasterMasterSteamPGO")
    modular = Path("E:/SteamLibrary/steamapps/common/KCD2Mod/Bin/Win64ReleaseSteamLTO_DLL")
    # Complete game logic binaries first; supporting modules follow, sequentially.
    entries = [("kcd1-game", k1 / "WHGame.dll"), ("kcd2-retail", retail / "WHGame.dll")]
    order = ["CombatModule", "RPGModule", "QuestModule", "EntityModule", "ConceptModule",
             "ShopModule", "PlayerModule", "DialogModule", "AnimationModule", "Framework",
             "XBehaviorModule", "XGenAIModule", "DatabaseModule", "EnvironmentModule",
             "GUIModule", "MusicModule", "SoundModule", "UtilsModule", "WHGame"]
    entries.extend(("kcd2-mod-" + name, modular / (name + ".dll")) for name in order)
    entries.extend(("kcd2-mod-" + p.stem, p) for p in sorted(modular.glob("Cry*.dll")))
    return entries


def bounded_process(command, env, log, state_path, identity, heap_gib):
    dll = kernel()
    if free_memory(dll) < (heap_gib + 6) * GIB:
        raise RuntimeError("Insufficient free physical RAM for heap plus native/OS reserve; nothing launched")
    job = dll.CreateJobObjectW(None, None)
    if not job:
        raise ctypes.WinError(ctypes.get_last_error())
    limits = ExtendedLimit()
    # Bound aggregate committed memory, including Java's native decompiler child.
    limits.basic.flags = 0x200 | 0x2000  # JOB_MEMORY | KILL_ON_JOB_CLOSE; only our own child tree.
    limits.job_limit = (heap_gib + 4) * GIB
    if not dll.SetInformationJobObject(job, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
        dll.CloseHandle(job)
        raise ctypes.WinError(ctypes.get_last_error())
    process = None
    started = time.time()
    try:
        with log.open("wb") as output:
            process = subprocess.Popen(command, env=env, stdout=output, stderr=subprocess.STDOUT,
                                       creationflags=subprocess.CREATE_NO_WINDOW)
            if not dll.AssignProcessToJobObject(job, w.HANDLE(process._handle)):
                process.terminate()  # exact Popen child, never a process name.
                process.wait()
                raise ctypes.WinError(ctypes.get_last_error())
            state = {"identity": identity, "pid": process.pid, "phase": "starting", "log": str(log)}
            while process.poll() is None:
                current = ExtendedLimit()
                if not dll.QueryInformationJobObject(job, 9, ctypes.byref(current), ctypes.sizeof(current), None):
                    raise ctypes.WinError(ctypes.get_last_error())
                available = free_memory(dll)
                state = {"identity": identity, "pid": process.pid, "phase": "running",
                         "seconds": round(time.time() - started), "peak_job_mib": current.peak_job // 1048576,
                         "available_mib": available // 1048576, "log": str(log)}
                atomic_json(state_path, state)
                if available < 4 * GIB:
                    dll.TerminateJobObject(job, 88)
                    process.wait()
                    state["phase"] = "stopped-for-memory-pressure"
                    atomic_json(state_path, state)
                    raise RuntimeError("Stopped owned Ghidra job only: physical RAM reserve fell below 4 GiB")
                time.sleep(5)
            state["phase"] = "exited"
            state["exit_code"] = process.returncode
            state["seconds"] = round(time.time() - started)
            atomic_json(state_path, state)
            if process.returncode:
                raise RuntimeError(f"Ghidra exited {process.returncode}; inspect {log}")
    finally:
        dll.CloseHandle(job)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", action="store_true")
    parser.add_argument("--only", help="one matrix identity")
    parser.add_argument("--heap-gib", type=int, default=6, choices=range(2, 9))
    parser.add_argument("--timeout", type=int, default=8, choices=range(1, 121))
    parser.add_argument("--output", type=Path)
    parser.add_argument("--ghidra", type=Path, default=Path("E:/Ghidra"))
    parser.add_argument("--java", type=Path, default=Path("C:/Program Files/Eclipse Adoptium/jdk-25.0.4.101-hotspot"))
    args = parser.parse_args()
    if os.name != "nt":
        parser.error("This controller uses Windows job memory limits")
    root = Path(__file__).resolve().parents[3]
    output = (args.output or root / "_work/ghidra-full-session9").resolve()
    if "_work" not in output.parts:
        parser.error("Decompiler output must stay in an ignored _work directory")
    output.mkdir(parents=True, exist_ok=True)
    entries = matrix()
    if args.only:
        entries = [(identity, p) for identity, p in entries if identity == args.only]
        if not entries:
            parser.error("Unknown matrix identity")
    records = []
    for identity, binary in entries:
        if not binary.is_file():
            raise FileNotFoundError(binary)
        records.append({"identity": identity, "binary": str(binary), "size": binary.stat().st_size,
                        "sha256": digest(binary)})
    inventory = output / ("inventory-" + (args.only or "all") + ".json")
    atomic_json(inventory, records)
    print(f"Inventoried {len(records)} binaries ({sum(r['size'] for r in records)//1048576} MiB); {inventory}", flush=True)
    if args.inventory:
        return
    claim_controller()
    env = dict(os.environ, JAVA_HOME=str(args.java), GHIDRA_HEADLESS_MAXMEM=f"{args.heap_gib}G",
               GHIDRA_HEADLESS_JAVA_OPTIONS="-XX:MaxDirectMemorySize=512m -XX:MaxMetaspaceSize=768m -Dcpu.core.limit=2")
    projects = output / "projects"
    projects.mkdir(exist_ok=True)
    for record in records:
        identity, binary = record["identity"], Path(record["binary"])
        target = output / identity
        target.mkdir(exist_ok=True)
        manifest = target / "binary.json"
        if manifest.exists() and json.loads(manifest.read_text()) != record:
            raise RuntimeError(f"Binary changed for existing research project {identity}; use a new output directory")
        atomic_json(manifest, record)
        if (target / "export-completed.txt").exists():
            report = audit(target)
            atomic_json(target / "export-audit.json", report)
            if not report["all_discovered_attempted"]:
                raise RuntimeError(f"Completed marker has an incomplete export inventory for {identity}")
            print(f"Already attempted all functions: {identity}; failures are still explicit", flush=True)
            continue
        # Persist the unanalysed import first. An analyzer OOM must not require
        # importing the entire engine again or leave an empty project mistaken
        # for a saved program.
        if not (target / "import-saved.json").exists():
            command = [str(args.ghidra / "support/analyzeHeadless.bat"), str(projects), identity,
                       "-max-cpu", "2", "-scriptPath", str(Path(__file__).parent),
                       "-import", str(binary), "-noanalysis", "-postScript", "ImportCheckpoint.java", str(target)]
            log = target / ("import-" + str(time.time_ns()) + ".log")
            print(f"Saving unanalysed import: {identity}", flush=True)
            bounded_process(command, env, log, output / "controller.json", identity, args.heap_gib)
            if not (target / "import-checkpoint.txt").exists():
                raise RuntimeError(f"No import checkpoint; inspect {log}")
            atomic_json(target / "import-saved.json", record)
        # Two CLI stages: completed analysis is saved before lengthy export.
        for stage in range(2):
            command = [str(args.ghidra / "support/analyzeHeadless.bat"), str(projects), identity,
                       "-max-cpu", "2", "-scriptPath", str(Path(__file__).parent)]
            command += ["-process", binary.name]
            checkpointed = (target / "analysis-completed.txt").exists()
            if checkpointed:
                command += ["-noanalysis"]
            else:
                command += ["-preScript", "FullAnalysisConfig.java", str(target), "-analysisTimeoutPerFile", "7200"]
            command += ["-postScript", "FullDecompile.java", str(target), str(args.timeout)]
            log = target / ("headless-" + str(time.time_ns()) + ".log")
            print(f"Analyzing/exporting {identity}, {args.heap_gib} GiB Java heap / {args.heap_gib+4} GiB whole job cap", flush=True)
            bounded_process(command, env, log, output / "controller.json", identity, args.heap_gib)
            if digest(binary) != record["sha256"]:
                raise RuntimeError("Installed binary changed during analysis")
            if (target / "export-completed.txt").exists():
                break
            if checkpointed or not (target / "analysis-completed.txt").exists():
                raise RuntimeError(f"Missing script finish marker despite CLI success; inspect {log}")
        if not (target / "export-completed.txt").exists():
            raise RuntimeError(f"No verified script finish marker despite CLI success; inspect {log}")
        report = audit(target)
        atomic_json(target / "export-audit.json", report)
        if not report["all_discovered_attempted"]:
            raise RuntimeError(f"Incomplete export inventory for {identity}")
        print((target / "export-completed.txt").read_text().strip(), flush=True)


if __name__ == "__main__":
    main()
