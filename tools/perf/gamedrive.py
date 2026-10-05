"""Drive KCD1 for tests: launch, wait for the menu, CONTINUE into the throwaway save (it is the newest), guard, screenshot, quit.
usage: gamectl.py launch [-devmode] | cont | shot <png> | kill | state
The guard: the throwaway game is a day-0 game (world time under 200000). If what loaded is anything else, the game is killed at once
(a load writes nothing; a save would) and the script exits with an error."""
import ctypes, subprocess, sys, time, os, re
u = ctypes.windll.user32
G = r'F:\SteamLibrary\steamapps\common\KingdomComeDeliverance'
LOG = os.path.join(G, 'kcd.log')
VK = {'F11': 0x7A, 'F12': 0x7B, 'Enter': 0x0D, 'Esc': 0x1B, 'Up': 0x26, 'Down': 0x28, 'E': 0x45}

def ps(cmd):
    return subprocess.run(['powershell', '-NoProfile', '-Command', cmd], capture_output=True, text=True).stdout.strip()

def pid():
    out = ps('(Get-Process KingdomCome -ErrorAction SilentlyContinue).Id')
    return int(out.split()[0]) if out else None

def fg():
    h = ps('(Get-Process KingdomCome).MainWindowHandle')
    u.SetForegroundWindow(int(h))

def key(name, gap=1.0):
    vk = VK[name]
    sc = u.MapVirtualKeyW(vk, 0)
    ext = 1 if name in ('Up', 'Down') else 0
    u.keybd_event(vk, sc, ext, 0); time.sleep(0.06); u.keybd_event(vk, sc, ext | 2, 0)
    time.sleep(gap)

def shot(path):
    ps(r"Add-Type -AssemblyName System.Windows.Forms,System.Drawing; $b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $bmp=New-Object System.Drawing.Bitmap $b.Width,$b.Height; $g=[System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size); $bmp.Save('%s')" % path)

def log_text():
    try:
        return open(LOG, 'rb').read().decode('utf-8', 'replace')
    except Exception:
        return ''

def log_has(pattern):
    return re.search(pattern, log_text()) is not None

def kill():
    ps('Stop-Process -Name KingdomCome -Force -ErrorAction SilentlyContinue')

def launch(devmode=False):
    if not ps('Get-Process steam -ErrorAction SilentlyContinue | Select-Object -First 1 Id'):
        subprocess.Popen([r'c:\games\steam\steam.exe', '-silent']); time.sleep(25)
    args = ['-devmode'] if devmode else []
    subprocess.Popen([os.path.join(G, 'Bin', 'Win64', 'KingdomCome.exe')] + args, cwd=G)
    t0 = time.time()
    while time.time() - t0 < 120:
        if log_has(r'KCDUS\|HELLO'): break
        time.sleep(2)
    t0 = time.time()
    while time.time() - t0 < 90:
        if ps('(Get-NetTCPConnection -State Listen -LocalPort 4600 -ErrorAction SilentlyContinue | Measure-Object).Count') not in ('', '0'):
            break
        time.sleep(2)
    time.sleep(24)

def cont():
    fg(); time.sleep(0.5)
    key('Enter', 4.0)                               # Continue
    shot(r'C:\Users\seths\Projects\kcd1-coop\_work\cont1.png')
    t0 = time.time()
    while time.time() - t0 < 300:
        if log_has(r'KCDUS\|ST\|'): break
        time.sleep(2)
    else:
        return 'TIMEOUT'
    m = re.search(r'KCDUS\|ST\|[^|]*\|[^|]*\|[^|]*\|\d+\|\d+\|\d+\|[^|]*\|(\d+)', log_text())
    wt = int(m.group(1)) if m else -1
    if wt < 0 or wt > 200000:
        kill()
        return 'GUARD: loaded world time %d is not the day-0 throwaway: game killed' % wt
    return 'in world, world time %d' % wt

if __name__ == '__main__':
    c = sys.argv[1]
    if c == 'launch': launch('-devmode' in sys.argv)
    elif c == 'cont': print(cont())
    elif c == 'shot': fg(); time.sleep(0.4); shot(sys.argv[2])
    elif c == 'kill': kill()
    elif c == 'state': print(pid(), log_has(r'KCDUS\|READY\|1'))
