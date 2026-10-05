# Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
# belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
r"""
The frame-rate soak: the real game, on a THROWAWAY save, with and without the mod, measured by the same sampler.

  python tools\perf\soak.py run --minutes 4 --warmup 1.5 --pairs 2   # vanilla, mod, vanilla, mod, then the verdict, then restores everything
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
NOISE_LIMIT_PCT = 3.0
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


def probe():
    """(camera distance from the player in metres, world time) through the remote console (-devmode), or None while the game cannot answer."""
    try:
        out = rclua.send(['#local c=System.GetViewCameraPos(); local p=player:GetWorldPos(); System.LogAlways(string.format("SOAKPOS %.1f %.1f %.1f", c.x-p.x, c.y-p.y, Calendar.GetWorldTime()))'], 0.8)
    except Exception:
        return None
    m = re.search(r'SOAKPOS (-?[\d.]+) (-?[\d.]+) (-?[\d.]+)', out)
    if not m:
        return None
    return (float(m.group(1)) ** 2 + float(m.group(2)) ** 2) ** 0.5, float(m.group(3))


def enter_world(timeout=480):
    """Continue ONCE, then wait for the camera to be on the player (the main menu's camera is hundreds of metres away). Works with and without the mod.
    If nothing happens within 90 s the menu is backed out of with Esc (a stray press may have opened a submenu) before pressing Continue again."""
    t0 = time.time()
    attempt = 0
    while time.time() - t0 < timeout:
        p = probe()
        if p and p[0] < 20:
            if p[1] > 200000:
                gd.kill()
                sys.exit('GUARD: the loaded save is not the day-0 throwaway (world time %d): game killed' % p[1])
            time.sleep(5)
            return p[1]
        if attempt == 0 or time.time() - last > 90:
            try:
                gd.fg()
            except Exception:
                pass
            if attempt > 0:
                for _ in range(3):
                    gd.key('Esc', 1.5)
            gd.key('Enter', 2.0)
            attempt += 1
            last = time.time()
        time.sleep(4)
    try:
        gd.shot(os.path.join(RUNS, 'world-did-not-load.png'))
    except Exception:
        pass
    gd.kill()
    sys.exit('the world did not load (see runs/world-did-not-load.png)')


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


def one_mod_run(a, label):
    procs = []
    try:
        subprocess.run([sys.executable, os.path.join(ROOT, 'tools', 'Build-Pak.py'), '--install-to', MODS], check=True, capture_output=True)
        gd.launch(devmode=True)
        enter_world()
        if not a.no_players:
            procs.append(sh([os.path.join(BIN, 'KcdUs.Relay', 'bin', 'Debug', 'net8.0', 'KcdUsRelay.exe'), '--port', '7791']))
            time.sleep(2)
            procs.append(sh([os.path.join(BIN, 'KcdUs.Bot', 'bin', 'Debug', 'net8.0', 'KcdUsBot.exe'), '--relay', '127.0.0.1:7791', '--role', 'host',
                             '--name', 'Host', '--center', '736.0,3419.0,63.91', '--radius', '1.2', '--speed', '0.5', '--time', '40000']))
            time.sleep(2)
            procs.append(sh([os.path.join(BIN, 'KcdUs.Agent', 'bin', 'Debug', 'net8.0', 'KcdUsAgent.exe'), '--role', 'guest', '--relay', '127.0.0.1:7791',
                             '--name', 'Henry', '--game-dir', GAME, '--no-hotkeys', '--status-port', '1416']))
        return measure(label, a.minutes, a.warmup)
    finally:
        for p in procs:
            try: p.kill()
            except Exception: pass
        gd.kill(); time.sleep(5)


def one_vanilla_run(a, label):
    try:
        if os.path.isdir(PARK): shutil.rmtree(PARK)
        shutil.move(os.path.join(MODS, 'kcdus'), PARK)
        gd.launch(devmode=True)
        enter_world()
        return measure(label, a.minutes, a.warmup)
    finally:
        gd.kill(); time.sleep(5)
        if os.path.isdir(PARK):
            shutil.move(PARK, os.path.join(MODS, 'kcdus'))


def run(a):
    """vanilla, mod, vanilla, mod: the game's frame rate drifts by a few percent from one session to the next (observed 98.5 then 94.8 for the
    same vanilla game), so a single A then B cannot tell an overhead from drift. Alternating and averaging can, and the spread between the two
    vanilla runs is the noise floor the verdict prints."""
    os.makedirs(RUNS, exist_ok=True)
    if a.vanilla_only:
        return run_vanilla_only(a)
    gd.kill(); time.sleep(4)
    vans, mods = [], []
    for i in range(a.pairs):
        vans.append(one_vanilla_run(a, 'vanilla-%d' % (i + 1)))
        mods.append(one_mod_run(a, 'mod-%d' % (i + 1)))
    avg = lambda rs, k: round(statistics.mean(r[k] for r in rs), 2)
    van = dict(label='vanilla', samples=sum(r['samples'] for r in vans), mean=avg(vans, 'mean'), median=avg(vans, 'median'), p5=avg(vans, 'p5'),
               min=min(r['min'] for r in vans), max=max(r['max'] for r in vans), script_errors=sum(r['script_errors'] for r in vans), runs=[r['mean'] for r in vans])
    mod = dict(label='mod', samples=sum(r['samples'] for r in mods), mean=avg(mods, 'mean'), median=avg(mods, 'median'), p5=avg(mods, 'p5'),
               min=min(r['min'] for r in mods), max=max(r['max'] for r in mods), script_errors=sum(r['script_errors'] for r in mods), runs=[r['mean'] for r in mods])
    json.dump(dict(vanilla=van, mod=mod, vanilla_runs=vans, mod_runs=mods), open(os.path.join(RUNS, 'summary.json'), 'w'), indent=1)
    return verdict(mod, van, runs=vans + mods)


def run_vanilla_only(a):
    gd.kill(); time.sleep(4)
    try:
        if os.path.isdir(PARK): shutil.rmtree(PARK)
        shutil.move(os.path.join(MODS, 'kcdus'), PARK)
        gd.launch(devmode=True)
        enter_world()
        rec = measure('vanilla-' + a.tag, a.minutes, a.warmup)
        print(json.dumps(rec))
    finally:
        gd.kill(); time.sleep(3)
        if os.path.isdir(PARK):
            shutil.move(PARK, os.path.join(MODS, 'kcdus'))
    return 0


def validity(runs, van, mod):
    """A measurement is only worth a verdict when the machine was quiet: identical runs must agree, and no run may show a stalled or catching-up game."""
    why = []
    for name, rs in (('vanilla', van.get('runs')), ('mod', mod.get('runs'))):
        if rs and len(rs) > 1 and (max(rs) - min(rs)) / (sum(rs) / len(rs)) * 100 > NOISE_LIMIT_PCT:
            why.append('the %s runs differ by %.1f%% (limit %.0f%%): %s' % (name, (max(rs) - min(rs)) / (sum(rs) / len(rs)) * 100, NOISE_LIMIT_PCT, rs))
    for r in runs or []:
        if r['max'] > 1.5 * r['median']:
            why.append('%s: a second at %.0f fps against a median of %.0f (the game stalled and caught up: focus lost? something else running?)' % (r['label'], r['max'], r['median']))
    return why


def verdict(mod, van, tuning=False, runs=None):
    dm = (mod['mean'] - van['mean']) / van['mean'] * 100
    dp = (mod['p5'] - van['p5']) / van['p5'] * 100
    ok_mean, ok_p5, ok_err = dm > -5.0, dp > -10.0, mod['script_errors'] == 0
    print('            mean    median  p5      min     n    script errors')
    for r in (mod, van):
        print('%-10s  %-7.1f %-7.1f %-7.1f %-7.1f %-4d %d' % (r['label'], r['mean'], r['median'], r['p5'], r['min'], r['samples'], r['script_errors']))
    if van.get('runs') and len(van['runs']) > 1:
        print('noise floor: the vanilla runs differ by %.1f%% (%s)   mod runs: %s' % ((max(van['runs']) - min(van['runs'])) / van['mean'] * 100, van['runs'], mod.get('runs')))
    print('mean %+.1f%% (need > -5%%): %s | p5 %+.1f%% (need > -10%%): %s | script errors %d: %s' %
          (dm, 'ok' if ok_mean else 'FAIL', dp, 'ok' if ok_p5 else 'FAIL', mod['script_errors'], 'ok' if ok_err else 'FAIL'))
    bad = validity(runs, van, mod) if not tuning else []
    for w in bad:
        print('INVALID:', w)
    passed = ok_mean and ok_p5 and ok_err and not bad
    print('SOAK', 'INCONCLUSIVE (leave the machine alone while it runs, and run it again)' if bad else ('PASS' if passed else 'FAIL'))
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
    r = sub.add_parser('run'); r.add_argument('--minutes', type=float, default=4); r.add_argument('--warmup', type=float, default=1.5); r.add_argument('--pairs', type=int, default=2); r.add_argument('--vanilla-only', action='store_true', help='tuning: one vanilla measurement'); r.add_argument('--tag', default='b'); r.add_argument('--no-players', action='store_true', help='tuning: the mod with no second player (no relay, bot or agent): its own overhead'); r.add_argument('--mod-only', action='store_true', help='tuning: measure the mod only and compare with the last vanilla run')
    v = sub.add_parser('verdict'); v.add_argument('mod'); v.add_argument('vanilla')
    a = ap.parse_args()
    if a.cmd == 'run': sys.exit(run(a))
    sys.exit(verdict(json.load(open(a.mod)), json.load(open(a.vanilla))))
