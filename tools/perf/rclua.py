# Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# Unofficial, free, not affiliated with Warhorse Studios or Deep Silver.
"""Send a Lua file (or inline string) to the KCD1 remote console as one '#' line, then print the new kcd.log lines.
usage: rcl.py file.lua [--wait 1.5]   |   rcl.py -e 'lua code'"""
import socket, sys, time, os, re
LOG = r'F:\SteamLibrary\steamapps\common\KingdomComeDeliverance\kcd.log'

def send(lines, wait=1.5):
    size0 = os.path.getsize(LOG)
    s = socket.create_connection(('127.0.0.1', 4600), timeout=3)
    s.settimeout(0.3)
    t0 = time.time()
    while time.time() - t0 < 0.5:
        try: s.recv(65536)
        except socket.timeout: pass
    for ln in lines:
        s.sendall(b'5' + ln.encode('utf-8') + b'\0')
        time.sleep(0.15)
    t0 = time.time()
    while time.time() - t0 < wait:
        try: s.recv(65536)
        except socket.timeout: pass
    s.close()
    with open(LOG, 'rb') as f:
        f.seek(size0)
        return f.read().decode('utf-8', 'replace')

def lua_lines(src):
    out = []
    for ln in src.splitlines():
        ln = re.sub(r'^\s*--.*$', '', ln)
        if ln.strip():
            out.append(ln.strip())
    code = ' '.join(out)
    return ['#' + code]

if __name__ == '__main__':
    a = sys.argv[1:]
    wait = 1.5
    if '--wait' in a:
        i = a.index('--wait'); wait = float(a[i + 1]); del a[i:i + 2]
    if a[0] == '-e':
        lines = ['#' + a[1]]
    else:
        lines = lua_lines(open(a[0], encoding='utf-8').read())
    res = send(lines, wait)
    for ln in res.splitlines():
        if 'pros_motd_banner' in ln or 'CryGFxFileOpener' in ln:
            continue
        print(ln)
