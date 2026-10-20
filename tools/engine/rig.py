"""Local two-player rig: TWO private KCD1 games on this computer (disposable profiles, never a real save), two agents, one relay.

    python tools/engine/rig.py up <clone.whs>      launch game A (host) and game B (guest); waits for both consoles
    python tools/engine/rig.py load                 load the cloned world in both (stock load graph through the guarded console)
    python tools/engine/rig.py agents               start the host agent on A and the guest agent on B (logs in _work/rig/)
    python tools/engine/rig.py menu a|b <verb> [arg]   what the Multiplayer tab would send (host, join, world save, world join, ...)
    python tools/engine/rig.py lua a|b "<code>"     a guarded Lua line in one game; prints the log lines it produced
    python tools/engine/rig.py status               processes, ports, the agents' last lines
    python tools/engine/rig.py down                 stop the rig's agents and games (only ever processes this rig started)

Game A uses the harness's default session, game B session "b" (KCDUS_ENGINE_SESSION). The isolation DLL gives each profile its own
saves folder and its own single-instance mutex. Each agent gets its own data folder (KCDUS_DATA_DIR), console port, status port and saves.
"""
import json
import os
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
WORK = ROOT / '_work'
RIG = WORK / 'rig'
AGENT = ROOT / 'dotnet' / 'KcdUs.Agent' / 'bin' / 'Release' / 'net8.0' / 'KcdUsAgent.dll'
SESS = {'a': '', 'b': 'b'}
STATUS = {'a': 1415, 'b': 1416}


def env_for(side):
    e = dict(os.environ, KCDUS_PROBE_BRIDGE='1', KCDUS_PROBE_ACTIONS='1')   # the research gate: native attack mirroring is only ever on in these private profiles
    staged = WORK / 'rig-mod' / 'kcdus'
    if staged.exists(): e['KCDUS_PROBE_MOD'] = str(staged)       # a build under test instead of the installed mod (the installed game is never touched)
    if SESS[side]:
        e['KCDUS_ENGINE_SESSION'] = SESS[side]
    else:
        e.pop('KCDUS_ENGINE_SESSION', None)
    return e


def harness(side, *args, capture=True):
    r = subprocess.run([sys.executable, str(HERE / 'engine_session.py'), *args], cwd=ROOT, env=env_for(side),
                       capture_output=capture, text=True)
    return (r.stdout or '') + (r.stderr or '')


def record(side):
    p = WORK / ('engine-current%s.json' % ('-' + SESS[side] if SESS[side] else ''))
    return json.loads(p.read_text(encoding='utf-8-sig'))


def ps(cmd):
    return subprocess.run(['powershell', '-NoProfile', '-Command', cmd], capture_output=True, text=True).stdout.strip()


def port_of(pid):
    out = ps(f"Get-NetTCPConnection -State Listen -OwningProcess {int(pid)} -ErrorAction SilentlyContinue | "
             "Where-Object {{ $_.LocalPort -ge 4600 -and $_.LocalPort -le 4620 }} | Select-Object -ExpandProperty LocalPort".replace('{{', '{').replace('}}', '}'))
    ports = [int(x) for x in out.split() if x.isdigit()]
    return min(ports) if ports else None


def alive(pid):
    return ps(f"(Get-Process -Id {int(pid)} -ErrorAction SilentlyContinue | Measure-Object).Count") == '1'


def up(src):
    RIG.mkdir(parents=True, exist_ok=True)
    for side in ('a', 'b'):
        print(side, harness(side, 'launch', src).strip().splitlines()[-1])
    end = time.time() + 600
    while time.time() < end:
        st = {s: (alive(record(s)['pid']), port_of(record(s)['pid'])) for s in ('a', 'b')}
        if all(v[1] for v in st.values()):
            print('both consoles up', st)
            return
        if not all(v[0] for v in st.values()):
            sys.exit('a game exited: %s' % st)
        time.sleep(8)
    sys.exit('timed out waiting for consoles')


def load():
    for side in ('a', 'b'):
        out = harness(side, 'file', str(HERE / 'load_probe_world.lua'))
        print(side, out.strip()[-160:])
    end = time.time() + 400
    ready = set()
    while time.time() < end and len(ready) < 2:
        for side in ('a', 'b'):
            if side in ready:
                continue
            out = harness(side, 'file', str(HERE / 'world_readback.lua'))
            if 'world-ready|true' in out:
                ready.add(side)
                print(side, 'world ready')
        time.sleep(10)
    if len(ready) < 2:
        sys.exit('not both worlds ready: %s' % ready)


def user_folder(r):
    """KingdomCome, or KingdomCome(1) for the game that started second (read from its own log)."""
    for name in ('kcd(1).log', 'kcd.log'):
        q = Path(r['gameRoot']) / name
        if q.exists() and "KingdomCome(1)'" in q.read_bytes().decode('utf-8', 'replace'):
            return 'KingdomCome(1)'
    return 'KingdomCome'


def staged_agent():
    """a copy of the built agent, so a rebuild never meets a locked DLL"""
    import shutil
    dst = RIG / ('agent-bin-%d' % int(time.time()))
    shutil.copytree(AGENT.parent, dst)
    return dst / AGENT.name


def agents():
    RIG.mkdir(parents=True, exist_ok=True)
    procs = {}
    agent = staged_agent()
    for side in ('a', 'b'):
        r = record(side)
        data = RIG / ('data-' + side)
        data.mkdir(parents=True, exist_ok=True)
        args = ['dotnet', str(agent), '--game-dir', r['gameRoot'], '--console-port', str(port_of(r['pid'])),
                '--saves-dir', str(Path(r['saveRoot']) / user_folder(r) / 'saves'), '--status-port', str(STATUS[side]),
                '--no-hotkeys', '--idle', '--name', 'Henry' + side.upper(), '--relay', '127.0.0.1']
        if side == 'b':
            args.append('--allow-loopback-join')
            if os.environ.get('RIG_HOTKEYS_B') == '1':
                args.remove('--no-hotkeys')   # B reads F11/F12 (only while B's game window is in front)
        env = dict(os.environ, KCDUS_DATA_DIR=str(data))
        logs = [q for q in (Path(r['gameRoot']) / 'kcd.log', Path(r['gameRoot']) / 'kcd(1).log') if q.exists()]
        if logs:
            env['KCDUS_GAME_LOG'] = max(logs, key=lambda q: q.stat().st_mtime).name
        log = open(RIG / ('agent-%s.log' % side), 'ab')
        flags = 0x00000200 | 0x08000000      # new process group, no window
        p = subprocess.Popen(args, cwd=str(agent.parent), env=env, stdout=log, stderr=subprocess.STDOUT, creationflags=flags)
        procs[side] = p.pid
        print(side, 'agent pid', p.pid)
    (RIG / 'agents.json').write_text(json.dumps(procs))


def lua(side, code, wait=1.5):
    return harness(side, 'lua', code)


def menu(side, verb, arg=''):
    line = 'KCDUS|MENU|%s%s' % (verb, ('|' + arg) if arg else '')
    print(lua(side, "System.LogAlways('%s')" % line).strip()[-300:])


def status():
    for side in ('a', 'b'):
        try:
            r = record(side)
            print(side, 'pid', r['pid'], 'alive', alive(r['pid']) if r['pid'] else None, 'console', port_of(r['pid']) if r['pid'] else None, r['profile'])
        except Exception as e:
            print(side, 'no record', e)
        lp = RIG / ('agent-%s.log' % side)
        if lp.exists():
            lines = lp.read_bytes().decode('utf-8', 'replace').splitlines()
            print('\n'.join('   ' + l[:200] for l in lines[-8:]))


def stop_agents():
    try:
        for side, pid in json.loads((RIG / 'agents.json').read_text()).items():
            line = ps(f"(Get-CimInstance Win32_Process -Filter 'ProcessId={int(pid)}').CommandLine")
            if 'KcdUsAgent.dll' in line:
                ps(f'Stop-Process -Id {int(pid)} -Force'); print('stopped agent', side, pid)
    except FileNotFoundError:
        pass


def wait_log(side, text, timeout=240):
    lp = RIG / ('agent-%s.log' % side)
    end = time.time() + timeout
    while time.time() < end:
        if lp.exists() and text in lp.read_bytes().decode('utf-8', 'replace'):
            return True
        time.sleep(2)
    return False


def session():
    """agents up, A hosts, B joins, A shares its world, B loads it: both games in one shared world"""
    for side in ('a', 'b'):
        lp = RIG / ('agent-%s.log' % side)
        if lp.exists():
            lp.rename(RIG / ('agent-%s-%d.log' % (side, int(time.time()))))
    agents()
    print('agents connected:', wait_log('a', 'the player is in the world', 90) or wait_log('a', 'remote console is connected', 30), wait_log('b', 'remote console is connected', 90))
    time.sleep(5)
    menu('a', 'host')
    print('hosting:', wait_log('a', 'joined as host', 60))
    menu('b', 'join')
    print('joined:', wait_log('a', 'joined as guest', 60))
    time.sleep(8)
    menu('a', 'world', 'save')
    time.sleep(15)
    menu('b', 'world', 'join')
    print('shared world:', wait_log('b', 'shared fights and loot: on', 240))


def down():
    try:
        for side, pid in json.loads((RIG / 'agents.json').read_text()).items():
            line = ps(f"(Get-CimInstance Win32_Process -Filter 'ProcessId={int(pid)}').CommandLine")
            if 'KcdUsAgent.dll' in line:
                ps(f'Stop-Process -Id {int(pid)} -Force'); print('stopped agent', side, pid)
    except FileNotFoundError:
        pass
    for side in ('a', 'b'):
        try:
            r = record(side)
        except FileNotFoundError:
            continue
        if r.get('pid'):
            line = ps(f"(Get-CimInstance Win32_Process -Filter 'ProcessId={int(r['pid'])}').CommandLine")
            if r['profile'] in line:
                ps(f"Stop-Process -Id {int(r['pid'])} -Force"); print('stopped game', side, r['pid'])
            r['pid'] = None
            p = WORK / ('engine-current%s.json' % ('-' + SESS[side] if SESS[side] else ''))
            p.write_text(json.dumps(r, indent=2), encoding='utf-8')


if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'up':
        up(sys.argv[2])
    elif cmd == 'load':
        load()
    elif cmd == 'agents':
        agents()
    elif cmd == 'menu':
        menu(sys.argv[2], sys.argv[3], sys.argv[4] if len(sys.argv) > 4 else '')
    elif cmd == 'lua':
        print(lua(sys.argv[2], sys.argv[3]))
    elif cmd == 'status':
        status()
    elif cmd == 'stop-agents':
        stop_agents()
    elif cmd == 'session':
        session()
    elif cmd == 'all':
        up(sys.argv[2]); time.sleep(200); load(); session()
    elif cmd == 'down':
        down()
    else:
        sys.exit(__doc__)
