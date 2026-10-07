"""Capture only the verified disposable game window while it owns the foreground."""
import ctypes
from ctypes import wintypes as w
import struct, zlib, subprocess
from pathlib import Path
import engine_session
s = engine_session.verified()
user, gdi = ctypes.WinDLL('user32'), ctypes.WinDLL('gdi32')
user.GetForegroundWindow.restype = w.HWND
user.GetWindowThreadProcessId.argtypes = [w.HWND, ctypes.POINTER(w.DWORD)]
hwnd = user.GetForegroundWindow()
pid = w.DWORD()
user.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
if pid.value != s['pid']:
    raise RuntimeError('Owned game is not foreground; no desktop capture.')
rect = w.RECT()
user.GetClientRect(hwnd, ctypes.byref(rect))
width, height = rect.right, rect.bottom
if not (100 <= width <= 4096 and 100 <= height <= 4096):
    raise RuntimeError('Invalid owned client bounds.')
user.GetDC.argtypes = [w.HWND]; user.GetDC.restype = w.HDC
point = w.POINT(0, 0)
user.ClientToScreen(hwnd, ctypes.byref(point))
gdi.CreateCompatibleDC.argtypes = [w.HDC]; gdi.CreateCompatibleDC.restype = w.HDC
gdi.CreateCompatibleBitmap.argtypes = [w.HDC, ctypes.c_int, ctypes.c_int]; gdi.CreateCompatibleBitmap.restype = w.HBITMAP
gdi.SelectObject.argtypes = [w.HDC, w.HGDIOBJ]; gdi.SelectObject.restype = w.HGDIOBJ
gdi.BitBlt.argtypes = [w.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, w.HDC, ctypes.c_int, ctypes.c_int, w.DWORD]
gdi.GetDIBits.argtypes = [w.HDC, w.HBITMAP, w.UINT, w.UINT, ctypes.c_void_p, ctypes.c_void_p, w.UINT]
gdi.DeleteObject.argtypes = [w.HGDIOBJ]; gdi.DeleteDC.argtypes = [w.HDC]
user.ReleaseDC.argtypes = [w.HWND, w.HDC]
dc = user.GetDC(None); memory = gdi.CreateCompatibleDC(dc); bitmap = gdi.CreateCompatibleBitmap(dc, width, height)
old = gdi.SelectObject(memory, bitmap)
try:
    if not gdi.BitBlt(memory, 0, 0, width, height, dc, point.x, point.y, 0x00CC0020): raise ctypes.WinError()
    pixels = ctypes.create_string_buffer(width * height * 4)
    info = ctypes.create_string_buffer(struct.pack('<IiiHHIIiiII', 40, width, -height, 1, 32, 0, width*height*4, 0, 0, 0, 0))
    gdi.SelectObject(memory, old)
    if gdi.GetDIBits(memory, bitmap, 0, height, pixels, info, 0) != height: raise ctypes.WinError()
    data = pixels.raw
    rows = b''.join(b'\0' + b''.join(data[p+2:p+3] + data[p+1:p+2] + data[p:p+1] for p in range(y*width*4, (y+1)*width*4, 4)) for y in range(height))
    def chunk(kind, data): return struct.pack('>I',len(data)) + kind + data + struct.pack('>I',zlib.crc32(kind+data))
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,2,0,0,0)) + chunk(b'IDAT',zlib.compress(rows)) + chunk(b'IEND',b'')
    path = Path(s['work']) / 'owned-window.png'; path.write_bytes(png); print(path)
finally:
    gdi.SelectObject(memory, old); gdi.DeleteObject(bitmap); gdi.DeleteDC(memory); user.ReleaseDC(None, dc)
