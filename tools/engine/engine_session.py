"""Isolated KCD1 engine experiments. Never press Continue or select a real save.
The user-folder and cloud switches must be confirmed before any Lua probe.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import socket
import subprocess
import time
import uuid
import re
import ctypes
from ctypes import wintypes

GAME = Path(r'F:\SteamLibrary\steamapps\common\KingdomComeDeliverance')
SAVED = Path(os.environ['USERPROFILE']) / 'Saved Games'
REAL = SAVED / 'kingdomcome' / 'saves'
ROOT = Path(__file__).resolve().parents[2]
CURRENT = ROOT / '_work' / 'engine-current.json'


def hashes():
    return {str(p.relative_to(REAL)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in REAL.rglob('*.whs')} if REAL.exists() else {}


def session():
    return json.loads(CURRENT.read_text())


def log_path():
    return Path(session().get('gameRoot', str(GAME))) / 'kcd.log'


def verified():
    s = session()
    if not re.fullmatch(r'KCDUS-engine-[0-9a-f]{32}',s.get('profile','')):
        raise RuntimeError('Session marker is invalid')
    work=ROOT/'_work'/s['profile']
    if Path(s.get('saveRoot','')).resolve()!= (work/'saved-games').resolve() or Path(s.get('gameRoot','')).resolve()!= (work/'game-root').resolve():
        raise RuntimeError('Session paths are outside this owned probe')
    pid=int(s.get('pid') or 0)
    if pid<=0:raise RuntimeError('No owned engine process is active')
    query=f"$p=Get-CimInstance Win32_Process -Filter 'ProcessId={pid}'; if ($p -and $p.CommandLine.Contains('{s['profile']}')) {{ 'owned' }}"
    if subprocess.check_output(['powershell','-NoProfile','-Command',query],text=True).strip()!='owned':
        raise RuntimeError('Recorded engine process exited or its PID was reused')
    log = log_path().read_text(errors='replace').replace('\\', '/').lower()
    expected = str(Path(s['saveRoot']) / 'KingdomCome').replace('\\', '/').lower()
    if f"user folder is '{expected}'" not in log:
        raise RuntimeError('Engine has not confirmed the disposable user folder; do not load or mutate anything.')
    if hashes() != s['realSaveHashes']:
        raise RuntimeError('Real-save hashes differ; stop this experiment and preserve evidence.')
    return s


def send(code, wait=1.5, guard=True):
    if guard:
        s=verified()
        query='Get-NetTCPConnection -State Listen -LocalPort 4600 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess'
        owners=subprocess.run(['powershell','-NoProfile','-Command',query],capture_output=True,text=True).stdout.split()
        if owners!=[str(s['pid'])]:raise RuntimeError('Remote console is not owned by this disposable process')
    if len(code.encode('utf-8')) > 1700:
        raise ValueError('Remote console command exceeds safe size')
    log = log_path()
    offset = log.stat().st_size
    with socket.create_connection(('127.0.0.1', 4600), timeout=4) as sock:
        sock.settimeout(0.3)
        end = time.monotonic() + 0.5
        while time.monotonic() < end:
            try:
                sock.recv(65536)
            except socket.timeout:
                pass
        sock.sendall(b'5#' + code.encode('utf-8') + b'\0')
        end = time.monotonic() + wait
        while time.monotonic() < end:
            try:
                sock.recv(65536)
            except socket.timeout:
                pass
    with log.open('rb') as stream:
        stream.seek(offset)
        lines = stream.read().decode('utf-8', 'replace').splitlines()
        return '\n'.join(line for line in lines if line and not line.startswith(('KCDUS|ST|', 'KCDUS|HB|', 'KCDUS|Q|')))


def run_file(path):
    verified()
    source = path.read_text(encoding='utf-8')
    send('KCDUS_ENGINE_SRC=""')
    for at in range(0, len(source), 700):
        send('KCDUS_ENGINE_SRC=KCDUS_ENGINE_SRC..' + json.dumps(source[at:at+700]))
    return send('local f,e=loadstring(KCDUS_ENGINE_SRC); if not f then System.LogAlways("KCDUS|ENGINE|compile|"..tostring(e)) else f() end', 1)


def key(name):
    s=verified()
    allowed={'Escape':27,'Enter':13,'Up':38,'Down':40}
    if name not in allowed: raise ValueError('Only owned test menu navigation keys are allowed')
    user=ctypes.WinDLL('user32',use_last_error=True)
    user.GetForegroundWindow.restype=wintypes.HWND
    user.SetForegroundWindow.argtypes=[wintypes.HWND]
    user.GetWindowThreadProcessId.argtypes=[wintypes.HWND,ctypes.POINTER(wintypes.DWORD)]
    handle=int(subprocess.check_output(['powershell','-NoProfile','-Command',f'(Get-Process -Id {int(s["pid"])}).MainWindowHandle'],text=True).strip())
    user.SetForegroundWindow(handle)
    time.sleep(.3)
    pid=wintypes.DWORD()
    user.GetWindowThreadProcessId(user.GetForegroundWindow(),ctypes.byref(pid))
    if pid.value!=s['pid']: raise RuntimeError('Owned game is not foreground; no keys sent')
    code=allowed[name]
    scan=user.MapVirtualKeyW(code,0)
    flags=1 if name in ('Up','Down') else 0
    user.keybd_event(code,scan,flags,0);time.sleep(.08);user.keybd_event(code,scan,flags|2,0)


def isolated_process(args, cwd, save_root):
    """Suspend our child, install its private folder hook, then allow initialization."""
    dll = ROOT / '_work' / 'engine-native' / 'ProfileIsolation.dll'
    if not dll.is_file():
        raise RuntimeError('Build tools/engine/native/ProfileIsolation.vcxproj first')
    class SI(ctypes.Structure):
        _fields_ = [('cb', wintypes.DWORD), ('reserved', wintypes.LPWSTR), ('desktop', wintypes.LPWSTR),
                    ('title', wintypes.LPWSTR), ('x', wintypes.DWORD), ('y', wintypes.DWORD),
                    ('xs', wintypes.DWORD), ('ys', wintypes.DWORD), ('xc', wintypes.DWORD),
                    ('yc', wintypes.DWORD), ('fill', wintypes.DWORD), ('flags', wintypes.DWORD),
                    ('show', wintypes.WORD), ('cbres', wintypes.WORD), ('res', ctypes.c_void_p),
                    ('stdin', wintypes.HANDLE), ('stdout', wintypes.HANDLE), ('stderr', wintypes.HANDLE)]
    class PI(ctypes.Structure):
        _fields_ = [('process', wintypes.HANDLE), ('thread', wintypes.HANDLE),
                    ('pid', wintypes.DWORD), ('tid', wintypes.DWORD)]
    k = ctypes.WinDLL('kernel32', use_last_error=True)
    def bind(name, result, *types):
        f = getattr(k, name); f.restype = result; f.argtypes = types; return f
    create = bind('CreateProcessW', wintypes.BOOL, wintypes.LPCWSTR, wintypes.LPWSTR,
                  ctypes.c_void_p, ctypes.c_void_p, wintypes.BOOL, wintypes.DWORD,
                  ctypes.c_void_p, wintypes.LPCWSTR, ctypes.POINTER(SI), ctypes.POINTER(PI))
    alloc = bind('VirtualAllocEx', ctypes.c_void_p, wintypes.HANDLE, ctypes.c_void_p, ctypes.c_size_t, wintypes.DWORD, wintypes.DWORD)
    write = bind('WriteProcessMemory', wintypes.BOOL, wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p)
    module = bind('GetModuleHandleW', wintypes.HMODULE, wintypes.LPCWSTR)
    address = bind('GetProcAddress', ctypes.c_void_p, wintypes.HMODULE, ctypes.c_char_p)
    remote = bind('CreateRemoteThread', wintypes.HANDLE, wintypes.HANDLE, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.c_void_p)
    wait = bind('WaitForSingleObject', wintypes.DWORD, wintypes.HANDLE, wintypes.DWORD)
    resume = bind('ResumeThread', wintypes.DWORD, wintypes.HANDLE)
    close = bind('CloseHandle', wintypes.BOOL, wintypes.HANDLE)
    terminate = bind('TerminateProcess', wintypes.BOOL, wintypes.HANDLE, wintypes.UINT)
    env = dict(os.environ, KCDUS_PROBE_SAVE_ROOT=str(save_root))
    env_block = ctypes.create_unicode_buffer('\0'.join(f'{key}={value}' for key, value in sorted(env.items())) + '\0\0')
    si = SI(); si.cb = ctypes.sizeof(si); pi = PI()
    command = ctypes.create_unicode_buffer(subprocess.list2cmdline(args))
    if not create(None, command, None, None, False, 0x4 | 0x400 | 0x08000000, env_block, str(cwd), ctypes.byref(si), ctypes.byref(pi)):
        raise ctypes.WinError(ctypes.get_last_error())
    thread = None
    try:
        payload = ctypes.create_unicode_buffer(str(dll))
        memory = alloc(pi.process, None, ctypes.sizeof(payload), 0x3000, 0x04)
        if not memory or not write(pi.process, memory, payload, ctypes.sizeof(payload), None):
            raise ctypes.WinError(ctypes.get_last_error())
        thread = remote(pi.process, None, 0, address(module('kernel32.dll'), b'LoadLibraryW'), memory, 0, None)
        if not thread or wait(thread, 30000) != 0 or not (save_root / 'isolation-installed.txt').is_file():
            raise RuntimeError('Private profile hook did not install; owned suspended process terminated')
        if resume(pi.thread) == 0xFFFFFFFF:
            raise ctypes.WinError(ctypes.get_last_error())
        return pi.pid
    except BaseException:
        terminate(pi.process, 1)
        raise
    finally:
        if thread: close(thread)
        close(pi.thread); close(pi.process)


def launch(source):
    if CURRENT.exists() and session().get('pid'):
        raise RuntimeError('Existing session record; stop and archive that owned probe before starting another.')
    profile = 'KCDUS-engine-' + uuid.uuid4().hex
    work = ROOT / '_work' / profile
    work.mkdir(parents=True)
    baseline = hashes()
    save_root = work / 'saved-games'
    slot = save_root / 'KingdomCome' / 'saves' / 'playline0'
    slot.mkdir(parents=True)
    data = source.read_bytes()
    (slot / 'probe.whs').write_bytes(data)
    game_root = work / 'game-root'
    game_root.mkdir()
    # The process-local hook handles early folder discovery. Config disables cloud.
    config = (GAME / 'system.cfg').read_text()
    config += f'\nsys_user_folder = "{profile}"\nsys_useSteamCloudForPlatformSaving = 0\nlog_EnableRemoteConsole = 1\n'
    (game_root / 'system.cfg').write_text(config)
    for folder in ('Data', 'Engine', 'Localization', 'Bin'):
        if (GAME / folder).is_dir():
            subprocess.run(['powershell', '-NoProfile', '-Command',
                            "New-Item -ItemType Junction -Path '" + str(game_root / folder) + "' -Target '" + str(GAME / folder) + "' | Out-Null"], check=True)
    import shutil
    shutil.copytree(GAME / 'Mods' / 'kcdus', game_root / 'Mods' / 'kcdus')
    args = [str(GAME / 'Bin' / 'Win64' / 'KingdomCome.exe'), '-devmode', '-root', str(game_root),
            '+sys_user_folder', profile, '+sys_user_subfolder', '',
            '+sys_useSteamCloudForPlatformSaving', '0', '+log_EnableRemoteConsole', '1',
            '+sys_intromoviesduringinit', '0', '+r_Fullscreen', '0', '+r_width', '1280', '+r_height', '720']
    pid = isolated_process(args, game_root, save_root)
    record = {'pid': pid, 'profile': profile, 'work': str(work), 'realSaveHashes': baseline, 'saveRoot': str(save_root),
              'sourceSha256': hashlib.sha256(data).hexdigest(), 'arguments': args, 'gameRoot': str(game_root)}
    CURRENT.parent.mkdir(parents=True, exist_ok=True)
    CURRENT.write_text(json.dumps(record, indent=2))
    (work / 'session.json').write_text(json.dumps(record, indent=2))
    print('Started engine PID', pid, 'with disposable profile', profile)


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('action', choices=['launch', 'verify', 'lua', 'file', 'key'])
    p.add_argument('value', nargs='?')
    a = p.parse_args()
    if a.action == 'launch':
        launch(Path(a.value).resolve(strict=True))
    elif a.action == 'verify':
        s = verified()
        print('Disposable profile verified; real saves unchanged:', len(s['realSaveHashes']))
    elif a.action == 'lua':
        print(send(a.value))
    elif a.action == 'key':
        key(a.value)
    else:
        print(run_file(Path(a.value)))
