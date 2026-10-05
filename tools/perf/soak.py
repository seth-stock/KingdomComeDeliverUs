# Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
# belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
r"""
The frame-rate soak: the real game, on a THROWAWAY save, with and without the mod, measured by the same sampler.

  python tools\perf\soak.py run --minutes 8 --warmup 2          # both runs, then the verdict, then restores everything
  python tools\perf\soak.py verdict runs\mod.json runs\vanilla.json

What a run does (docs/SOAK.md):
  * starts the game with -devmode (so the remote console accepts the Lua sampler), Continues into the newest save,
    and REFUSES to go on unless that save is the day-0 throwaway (world time under 200000): it kills the game instead;
  * mod run: the mod is installed, a relay, a bot host and the guest agent run, and the bot's ghost orbits Henry: the real workload
    (10 Hz sampling both ways, ghost moves at 30 Hz, quest polling, the agent's console traffic);
  * vanilla run: the mod folder is moved out of Mods (and put back at the end), nothing else runs;
  * the sampler writes SOAK|fps lines once a second for the measuring window after the warm-up.
The verdict passes when the mod's mean frame rate is within 5% of vanilla's, its 5th-percentile (the worst seconds) within 10%, and
the mod logged no script error. It prints the numbers either way; it never loosens itself.
"""
import argparse, json, os, re, shutil, statistics, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
import gamedrive as gd
import rclua

GAME = gd.G
MODS = os.path.join(GAME, 'Mods')
PARK = os.path.join(GAME, 'kcdus-parked')
RUNS = os.path.join(HERE, 'runs')
BIN = os.path.join(ROOT, 'dotnet')


def sh(args, **kw):
    return subprocess.Popen(args, stdout=open(os.path.join(RUNS, 'proc.log'), 'ab'), stderr=subprocess.STDOUT, **kw)


def enter_world(timeout=300):
    gd.fg(); time.sleep(0.6)
    t0 = time.time()
    while time.time() - t0 < timeout:
        gd.key('Enter', 12.0)
        txt = gd.log_text()
        if re.search(r'KCDUS\|READY\|1', txt) or (gd.pid() and re.search(r'Loading Screen|OnLoadingComplete', txt)):
            break
    # in the world: the camera is on the player (the menu's is hundreds of metres away)
    t0 = time.time()
    while time.time() - t0 < timeout:
        out = rclua.send(['#local c=System.GetViewCameraPos(); local p=player:GetWorldPos(); System.LogAlways(string.format("SOAKPOS %.1f %.1f %.1f", c.x-p.x, c.y-p.y, Calendar.GetWorldTime()))'], 0.8)
        m = re.search(r'SOAKPOS (-?[\d.]+) (-?[\d.]+) (-?[\d.]+)', out)
        if m and (float(m.group(1)) ** 2 + float(m.group(2)) ** 2) ** 0.5 < 20:
            wt = float(m.group(3))
            if wt > 200000:
                gd.kill()
                sys.exit('GUARD: the loaded save is not the day-0 throwaway (world time %d): game killed' % wt)
            return wt
        time.sleep(3)
    gd.kill()
    sys.exit('the world did not load')


def measure(label, minutes, warmup):
    lua = open(os.path.join(HERE, 'sampler.lua'), encoding='utf-8').read()
    rclua.send(rclua.lua_lines(lua), 1.0)
    print('%s: warm-up %.0f min, measuring %.0f min' % (label, warmup, minutes), flush=True)
    time.sleep(warmup * 60)
    start = len(gd.log_text())
    time.sleep(minutes * 60)
    seg = gd.log_text()[start:]
    fps = [float(m.group(1)) for m in re.finditer(r'SOAK\|([\d.]+)\|', seg)]
    errs = len(re.findall(r'KCDUS\|ERR\|', seg)) + len(re.findall(r'\[Error\] Lua error', seg))
    s = sorted(fps)
    rec = dict(label=label, samples=len(fps), mean=round(statistics.mean(fps), 2), median=round(statistics.median(fps), 2),
               p5=round(s[int(len(s) * 0.05)], 2), min=round(s[0], 2), max=round(s[-1], 2), script_errors=errs,
               minutes=minutes, warmup=warmup)
    os.makedirs(RUNS, exist_ok=True)
    json.dump(rec, open(os.path.join(RUNS, label + '.json'), 'w'), indent=1)
    return rec


def run(a):
    os.makedirs(RUNS, exist_ok=True)
    gd.kill(); time.sleep(4)
    procs = []
    try:
        # ---- mod run
        subprocess.run([sys.executable, os.path.join(ROOT, 'tools', 'Build-Pak.py'), '--install-to', MODS], check=True, capture_output=True)
        gd.launch(devmode=True)
        enter_world()
        procs.append(sh([os.path.join(BIN, 'KcdUs.Relay', 'bin', 'Debug', 'net8.0', 'KcdUsRelay.exe'), '--port', '7791']))
        time.sleep(2)
        procs.append(sh([os.path.join(BIN, 'KcdUs.Bot', 'bin', 'Debug', 'net8.0', 'KcdUsBot.exe'), '--relay', '127.0.0.1:7791', '--role', 'host',
                         '--name', 'Host', '--center', '736.0,3419.0,63.91', '--radius', '1.2', '--speed', '0.5', '--time', '40000']))
        time.sleep(2)
        procs.append(sh([os.path.join(BIN, 'KcdUs.Agent', 'bin', 'Debug', 'net8.0', 'KcdUsAgent.exe'), '--role', 'guest', '--relay', '127.0.0.1:7791',
                         '--name', 'Henry', '--game-dir', GAME, '--no-hotkeys', '--status-port', '1416']))
        mod = measure('mod', a.minutes, a.warmup)
        for p in procs:
            p.kill()
        procs.clear()
        gd.kill(); time.sleep(5)
        if a.mod_only:
            van = json.load(open(os.path.join(RUNS, 'vanilla.json')))
            print('(tuning run: vanilla reused from an earlier run, not a release record)')
            return verdict(mod, van, tuning=True)
        # ---- vanilla run
        if os.path.isdir(PARK): shutil.rmtree(PARK)
        shutil.move(os.path.join(MODS, 'kcdus'), PARK)
        gd.launch(devmode=True)
        enter_world()
        van = measure('vanilla', a.minutes, a.warmup)
    finally:
        for p in procs:
            try: p.kill()
            except Exception: pass
        gd.kill(); time.sleep(3)
        if os.path.isdir(PARK):
            shutil.move(PARK, os.path.join(MODS, 'kcdus'))
    return verdict(mod, van)


def verdict(mod, van, tuning=False):
    dm = (mod['mean'] - van['mean']) / van['mean'] * 100
    dp = (mod['p5'] - van['p5']) / van['p5'] * 100
    ok_mean, ok_p5, ok_err = dm > -5.0, dp > -10.0, mod['script_errors'] == 0
    print('            mean    median  p5      min     n    script errors')
    for r in (mod, van):
        print('%-10s  %-7.1f %-7.1f %-7.1f %-7.1f %-4d %d' % (r['label'], r['mean'], r['median'], r['p5'], r['min'], r['samples'], r['script_errors']))
    print('mean %+.1f%% (need > -5%%): %s | p5 %+.1f%% (need > -10%%): %s | script errors %d: %s' %
          (dm, 'ok' if ok_mean else 'FAIL', dp, 'ok' if ok_p5 else 'FAIL', mod['script_errors'], 'ok' if ok_err else 'FAIL'))
    passed = ok_mean and ok_p5 and ok_err
    print('SOAK', 'PASS' if passed else 'FAIL')
    import hashlib
    pak = open(os.path.join(ROOT, 'build', 'mod', 'kcdus', 'Data', 'kcdus.pak'), 'rb').read()
    rec = dict(version=open(os.path.join(ROOT, 'VERSION')).read().strip(), pak_sha256=hashlib.sha256(pak).hexdigest(), passed=passed, mean_delta_pct=round(dm, 2), p5_delta_pct=round(dp, 2),
               mod=mod, vanilla=van, date=time.strftime('%Y-%m-%d'))
    if tuning:
        rec['passed'] = False
        rec['tuning_only'] = True
    json.dump(rec, open(os.path.join(ROOT, 'tools', 'perf', 'soak-record.json'), 'w'), indent=1)
    return 0 if passed else 1


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest='cmd', required=True)
    r = sub.add_parser('run'); r.add_argument('--minutes', type=float, default=8); r.add_argument('--warmup', type=float, default=2); r.add_argument('--mod-only', action='store_true', help='tuning: measure the mod only and compare with the last vanilla run')
    v = sub.add_parser('verdict'); v.add_argument('mod'); v.add_argument('vanilla')
    a = ap.parse_args()
    if a.cmd == 'run': sys.exit(run(a))
    sys.exit(verdict(json.load(open(a.mod)), json.load(open(a.vanilla))))
