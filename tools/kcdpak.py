# Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
# belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
"""Tolerant reader/extractor for CryEngine .pak files (zip archives whose local headers use backslashes, which
Python's zipfile rejects). Reads the central directory, then the local header by offset, ignoring the name mismatch.
usage: kcdpak.py <pak> <dest> [substring filter...]   |   import: read_pak(path) -> (file, entries), read_entry(file, entry)"""
import struct, sys, zlib, os

def read_pak(path):
    f = open(path, 'rb')
    f.seek(0, 2)
    size = f.tell()
    tail = min(size, 70000)
    f.seek(size - tail)
    buf = f.read()
    i = buf.rfind(b'PK\x05\x06')
    eocd = buf[i:i + 22]
    _, _, _, _, n, csize, coff, _ = struct.unpack('<IHHHHIIH', eocd)
    # zip64?
    if coff == 0xFFFFFFFF or n == 0xFFFF:
        j = buf.rfind(b'PK\x06\x07')
        _, _, off64, _ = struct.unpack('<IIQI', buf[j:j + 20])
        f.seek(off64)
        r = f.read(56)
        _, _, _, _, _, _, n, _, csize, coff = struct.unpack('<IQHHIIQQQQ', r)
    f.seek(coff)
    cd = f.read(csize)
    ents = []
    p = 0
    for _ in range(n):
        sig, = struct.unpack('<I', cd[p:p + 4])
        if sig != 0x02014b50:
            break
        (_, _, _, flags, method, _, _, crc, csz, usz, nl, el, cl, _, _, _, lho) = struct.unpack('<IHHHHHHIIIHHHHHII', cd[p:p + 46])
        name = cd[p + 46:p + 46 + nl].decode('utf-8', 'replace')
        extra = cd[p + 46 + nl:p + 46 + nl + el]
        if csz == 0xFFFFFFFF or usz == 0xFFFFFFFF or lho == 0xFFFFFFFF:
            q = 0
            while q + 4 <= len(extra):
                tag, sz = struct.unpack('<HH', extra[q:q + 4])
                if tag == 1:
                    vals = extra[q + 4:q + 4 + sz]
                    k = 0
                    if usz == 0xFFFFFFFF:
                        usz, = struct.unpack('<Q', vals[k:k + 8]); k += 8
                    if csz == 0xFFFFFFFF:
                        csz, = struct.unpack('<Q', vals[k:k + 8]); k += 8
                    if lho == 0xFFFFFFFF:
                        lho, = struct.unpack('<Q', vals[k:k + 8]); k += 8
                    break
                q += 4 + sz
        ents.append((name.replace('\\', '/'), method, csz, usz, lho, flags))
        p += 46 + nl + el + cl
    return f, ents

def read_entry(f, e):
    name, method, csz, usz, lho, flags = e
    f.seek(lho)
    h = f.read(30)
    sig, = struct.unpack('<I', h[:4])
    assert sig == 0x04034b50, 'bad local header'
    nl, el = struct.unpack('<HH', h[26:30])
    f.seek(lho + 30 + nl + el)
    data = f.read(csz)
    if method == 0:
        return data
    if method == 8:
        return zlib.decompress(data, -15)
    raise ValueError('method %d (encrypted or unsupported) for %s' % (method, name))

def main():
    pak, dest = sys.argv[1], sys.argv[2]
    filt = [s.lower() for s in sys.argv[3:]]
    f, ents = read_pak(pak)
    n = skipped = 0
    for e in ents:
        name = e[0]
        if name.endswith('/'):
            continue
        if filt and not any(s in name.lower() for s in filt):
            continue
        try:
            data = read_entry(f, e)
        except Exception as ex:
            skipped += 1
            print('SKIP', name, ex)
            continue
        out = os.path.join(dest, name)
        os.makedirs(os.path.dirname(out), exist_ok=True)
        open(out, 'wb').write(data)
        n += 1
    print('%s: %d entries, extracted %d, skipped %d' % (os.path.basename(pak), len(ents), n, skipped))

if __name__ == '__main__':
    main()
