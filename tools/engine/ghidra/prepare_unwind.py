"""Export primary x64 unwind entries and their chained code ranges.

Does not modify code or automatically merge functions. Chained metadata is
evidence for reviewing noncontiguous functions, not an invented ABI signature.
Format: https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64
"""
from pathlib import Path
import argparse
import hashlib
import json
import struct


def inspect(binary):
    data = binary.read_bytes()
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    if data[:2] != b'MZ' or data[pe:pe+4] != b'PE\0\0':
        raise ValueError('Not a PE image')
    if struct.unpack_from('<H', data, pe+4)[0] != 0x8664:
        raise ValueError('x64 image required')
    opt = pe+24
    if struct.unpack_from('<H', data, opt)[0] != 0x20b:
        raise ValueError('PE32+ image required')
    base = struct.unpack_from('<Q', data, opt+24)[0]
    table = opt+struct.unpack_from('<H', data, pe+20)[0]
    sections = []
    for i in range(struct.unpack_from('<H', data, pe+6)[0]):
        at = table+i*40
        virtual, rva, length, offset = struct.unpack_from('<IIII', data, at+8)
        flags = struct.unpack_from('<I', data, at+36)[0]
        if offset+length > len(data):
            raise ValueError('Section extends beyond input')
        sections.append((rva, length, offset, flags))
    def position(rva, count):
        for start, length, offset, flags in sections:
            if start <= rva and rva+count <= start+length:
                return offset+rva-start
        raise ValueError(f'Unmapped RVA {rva:x} size {count}')
    def executable(start, end):
        return any(flags & 0x20000000 and rva <= start < end <= rva+length
                   for rva, length, offset, flags in sections)
    rva, length = struct.unpack_from('<II', data, opt+112+3*8)
    if length % 12:
        raise ValueError('Invalid runtime-function directory length')
    at = position(rva, length)
    rows = [struct.unpack_from('<III', data, p) for p in range(at, at+length, 12)]
    groups = {}
    unsupported = []
    for row in rows:
        begin, end, unwind = row
        if not executable(begin, end):
            raise ValueError(f'Nonexecutable runtime range {begin:x}..{end:x}')
        seen = set()
        primary = row
        for depth in range(32):
            pbegin, pend, punwind = primary
            if primary in seen:
                raise ValueError('Unwind chain cycle')
            seen.add(primary)
            header = position(punwind, 4)
            version = data[header] & 7
            flags = data[header] >> 3
            if version != 1:
                unsupported.append(dict(begin=hex(begin), version=version))
                primary = None
                break
            if not flags & 4:
                break
            if flags & 3:
                raise ValueError('Chained and handler flags are mutually exclusive')
            count = data[header+2]
            chain = position(punwind+4+((count+1)//2)*4, 12)
            primary = struct.unpack_from('<III', data, chain)
            if not executable(primary[0], primary[1]):
                raise ValueError('Invalid primary code range')
        else:
            raise ValueError('Unwind chain exceeds depth budget')
        if primary is None:
            continue
        root = primary[0]
        group = groups.setdefault(root, dict(primary_begin=hex(base+root), primary_end=hex(base+primary[1]), ranges=[]))
        group['ranges'].append(dict(begin=hex(base+begin), end=hex(base+end), chained=bool(depth)))
    return dict(binary=str(binary.resolve()), sha256=hashlib.sha256(data).hexdigest(), image_base=hex(base),
                runtime_ranges=len(rows), primary_groups=len(groups), unsupported=unsupported,
                groups=[groups[k] for k in sorted(groups)])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('binary', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    if '_work' not in args.output.resolve().parts:
        parser.error('Private metadata output must stay under ignored _work')
    value = inspect(args.binary)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(value), encoding='utf-8')
    print(f"Unwind metadata: {value['runtime_ranges']} ranges, {value['primary_groups']} primary groups, "
          f"{len(value['unsupported'])} unsupported records; {args.output}")


if __name__ == '__main__':
    main()
