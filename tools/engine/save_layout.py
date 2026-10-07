"""Read-only KCD1 framing probe. Prints sizes and tags, never player/debug strings."""
from pathlib import Path
import struct
import sys
import zlib

data = Path(sys.argv[1]).read_bytes()
offset = 0
raw = bytearray()
while offset < len(data) - 64:
    compressed, length = struct.unpack_from('<ii', data, offset)
    if not 0 <= length <= 32768:
        raise ValueError('Unexpected raw block size')
    size = length if compressed == -1 else compressed
    if not 0 <= size <= 32768 or offset + 8 + size > len(data) - 64:
        raise ValueError('Unexpected block bounds')
    block = data[offset+8:offset+8+size]
    if compressed != -1:
        block = zlib.decompress(block)
    if len(block) != length:
        raise ValueError('Inflated size differs')
    raw.extend(block)
    offset += 8 + size
print('Raw bytes:', len(raw), 'Prefix:', raw[:12].hex(), 'Footer:', data[-64:-60].hex())
def walk(begin, end, depth):
    parts = []
    pos = begin
    while pos + 6 <= end:
        tag, length = struct.unpack_from('<HI', raw, pos)
        if pos + 6 + length > end:
            if depth == 0: print('Top-level tail:', pos, end-pos, raw[pos:pos+12].hex())
            return
        parts.append((tag, pos, length))
        pos += 6 + length
    if pos != end:
        if depth == 0: print('Top-level remainder:', end-pos)
        return
    for tag, pos, length in parts:
        print('  ' * depth + f'{tag:04x} @{pos} bytes={length}')
        if tag in (0x730c,0x3529): print('Module prefix:',hex(tag),raw[pos+6:pos+70].hex())
        if depth < 3: walk(pos+6, pos+6+length, depth+1)
walk(4, len(raw)-1, 0)
