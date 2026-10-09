"""The bridge's pause gate (native/KcdUs.EngineBridge, Local\\KcdUsBridgePause.v1), as the agent drives it. Developer tool for the private engine harness.
    python tools/engine/pause_gate.py show
    python tools/engine/pause_gate.py on [mask]      levers on with a fresh heartbeat (repeat within 10 s, or use 'hold <seconds>')
    python tools/engine/pause_gate.py hold <seconds> [mask]
    python tools/engine/pause_gate.py off
"""
import ctypes
import struct
import sys
import time
from ctypes import wintypes

NAME = 'Local\\KcdUsBridgePause.v1'
FMT = '<IIiIqiiii16I'
SIZE = struct.calcsize(FMT)
k = ctypes.WinDLL('kernel32', use_last_error=True)
k.OpenFileMappingW.restype = wintypes.HANDLE
k.OpenFileMappingW.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.LPCWSTR]
k.MapViewOfFile.restype = ctypes.c_void_p
k.MapViewOfFile.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, ctypes.c_size_t]
k.GetTickCount64.restype = ctypes.c_ulonglong


def view():
    h = k.OpenFileMappingW(0x000F001F, False, NAME)
    if not h:
        raise SystemExit('no pause gate: the bridge is not loaded in a running game')
    p = k.MapViewOfFile(h, 0x000F001F, 0, 0, SIZE)
    if not p:
        raise ctypes.WinError(ctypes.get_last_error())
    return p


def read(p):
    v = struct.unpack(FMT, ctypes.string_at(p, SIZE))
    return {'magic': hex(v[0]), 'version': v[1], 'levers': v[2], 'mask': hex(v[3]), 'beatAgeMs': k.GetTickCount64() - v[4] if v[4] else None,
            'armed': v[5], 'calls': v[6], 'declined': v[7], 'hist': [('pause' if h >> 16 else 'resume') + ':' + str(h & 0xffff) for h in
                                                                       (v[9 + (i % 16)] for i in range(v[8] - min(v[8], 16), v[8]))]}


def write(p, levers, mask=None):
    ctypes.c_int.from_address(p + 8).value = levers
    if mask is not None:
        ctypes.c_uint.from_address(p + 12).value = mask
    ctypes.c_longlong.from_address(p + 16).value = k.GetTickCount64()


if __name__ == '__main__':
    p = view()
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'show'
    if cmd == 'on':
        write(p, 1, int(sys.argv[2], 0) if len(sys.argv) > 2 else None)
    elif cmd == 'off':
        write(p, 0)
    elif cmd == 'hold':
        end = time.time() + float(sys.argv[2])
        mask = int(sys.argv[3], 0) if len(sys.argv) > 3 else None
        while time.time() < end:
            write(p, 1, mask); time.sleep(1)
    print(read(p))
