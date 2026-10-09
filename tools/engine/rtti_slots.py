"""Read-only MSVC x64 RTTI/vtable inspection of a locally installed PE.

Prints addresses, RTTI names and requested slot targets, never binary assets.
Usage: rtti_slots.py WHGame.dll CActor 460
"""
import argparse
import struct
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('binary', type=Path)
p.add_argument('name')
p.add_argument('slots', nargs='*', type=lambda s: int(s, 16))
a = p.parse_args()
d = a.binary.read_bytes()
pe = struct.unpack_from('<I', d, 0x3c)[0]
count = struct.unpack_from('<H', d, pe + 6)[0]
opt = pe + 24
base = struct.unpack_from('<Q', d, opt + 24)[0]
sh = opt + struct.unpack_from('<H', d, pe + 20)[0]
sections = []
executable = []
for n in range(count):
    at = sh + n * 40
    _, rva, size, off = struct.unpack_from('<IIII', d, at + 8)
    sections.append((rva, size, off))
    if struct.unpack_from('<I', d, at + 36)[0] & 0x20000000:
        executable.append((base + rva, base + rva + size))

def rva_of(off):
    for rva, size, start in sections:
        if start <= off < start + size:
            return rva + off - start

def offset(rva):
    for start, size, off in sections:
        if start <= rva < start + size:
            return off + rva - start

pos = 0
while True:
    pos = d.find(b'.?AV', pos)
    if pos < 0: break
    end = d.find(b'\0', pos, pos + 512)
    name = d[pos:end].decode('ascii', errors='replace') if end >= 0 else ''
    pos += 1
    if a.name not in name: continue
    td = rva_of(pos - 1 - 16)
    if td is None: continue
    at = 0
    while True:
        at = d.find(struct.pack('<I', td), at)
        if at < 0: break
        col_off = at - 12
        at += 1
        col = rva_of(col_off)
        if col is None or col_off < 0 or col_off + 24 > len(d): continue
        sig, object_off, cd, _, hierarchy, self_rva = struct.unpack_from('<IIIIII', d, col_off)
        if sig != 1 or self_rva != col or offset(hierarchy) is None: continue
        vp = 0
        while True:
            vp = d.find(struct.pack('<Q', base + col), vp)
            if vp < 0: break
            vtable = rva_of(vp + 8)
            vp += 1
            if vtable is None: continue
            print(f'{name} base-offset=0x{object_off:x} vtable=0x{base + vtable:x}')
            for slot in a.slots:
                if slot < 0 or slot % 8: raise ValueError('Slots must be nonnegative multiples of eight')
                # Do not print nearby data/RTTI as fictitious methods once a
                # small vtable has ended. Every preceding entry must be code.
                valid = True
                for preceding in range(0, slot + 1, 8):
                    off = offset(vtable + preceding)
                    target = struct.unpack_from('<Q', d, off)[0] if off is not None and off + 8 <= len(d) else 0
                    if not any(lo <= target < hi for lo, hi in executable): valid = False;break
                if not valid:
                    print(f'  slot +0x{slot:x}: outside verified function-pointer span')
                    continue
                off = offset(vtable + slot)
                if off is not None and off + 8 <= len(d):
                    target = struct.unpack_from('<Q', d, off)[0]
                    print(f'  slot +0x{slot:x}: 0x{target:x}')
