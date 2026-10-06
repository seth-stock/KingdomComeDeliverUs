#!/usr/bin/env python3
"""Read-only discovery in installed game scripts. No binding is marked proven.
Never extracts or redistributes game assets. Output contains method names,
source paths and content hashes, not game scripts or player saves.
"""
import argparse
import hashlib
import json
import re
import struct
import zipfile
from pathlib import Path


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read_entry(archive, entry):
    # Warhorse normalizes slashes in the central directory but some local
    # headers retain backslashes. Verify equivalence before accommodating it.
    with open(archive.filename, 'rb') as stream:
        stream.seek(entry.header_offset)
        header = stream.read(30)
        if len(header) != 30 or header[:4] != b'PK\x03\x04':
            raise ValueError('Invalid local pak header')
        length = struct.unpack_from('<H', header, 26)[0]
        name = stream.read(length).decode('utf-8' if entry.flag_bits & 0x800 else 'cp437')
    if name.replace('\\', '/') != entry.filename.replace('\\', '/'):
        raise ValueError('Pak entry path mismatch')
    original = entry.orig_filename
    try:
        entry.orig_filename = name
        return archive.read(entry)  # CRC and decompression checks remain enabled
    finally:
        entry.orig_filename = original


def inspect(root):
    calls = re.compile(r'(?:soul|human|actor|inventory)\s*[:.]\s*([A-Za-z_]\w*)\s*\(')
    interesting = re.compile(r'perk|anim|equip|remove|stat|skill|damage|health', re.I)
    found = {}
    hashes = {}
    for name in ('Scripts.pak', 'GameData.pak'):
        path = root / 'Data' / name
        if not path.is_file():
            continue
        hashes[str(path.relative_to(root))] = digest(path)
        with zipfile.ZipFile(path) as archive:
            for entry in archive.infolist():
                if not entry.filename.lower().endswith('.lua') or entry.file_size > 2_000_000:
                    continue
                source = read_entry(archive, entry).decode('utf-8', 'replace')
                for match in calls.finditer(source):
                    method = match.group(1)
                    if interesting.search(method):
                        found.setdefault(method, set()).add(entry.filename.replace('\\', '/'))
    for name in ('KingdomCome.exe', 'WHGame.dll'):
        path = root / 'Bin' / 'Win64' / name
        if path.is_file():
            hashes[str(path.relative_to(root))] = digest(path)
    return {'schema': 1, 'status': 'unproven', 'moduleHashes': hashes,
            'candidates': [{'method': method, 'status': 'unproven', 'scriptReferences': sorted(paths)}
                           for method, paths in sorted(found.items())],
            'requiredProof': ['binding callable on installed build', 'runtime readback',
                              'save/reload persistence', 'repeated application', 'failure recovery']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-dir', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    root = args.game_dir.resolve(strict=True)
    output = args.output.resolve()
    if output == root or root in output.parents:
        parser.error('Write the report outside the game folder')
    if output.exists():
        parser.error('Report exists; choose a new output file')
    report = inspect(root)
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open('x', encoding='utf-8') as stream:
        json.dump(report, stream, indent=2)
        stream.write('\n')
    print(f'{len(report["candidates"])} candidate methods; runtime support remains unproven. Report: {output}')


if __name__ == '__main__':
    main()
