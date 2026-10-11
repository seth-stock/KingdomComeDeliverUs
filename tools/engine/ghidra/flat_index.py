"""Write a private, greppable C index of one full-engine export (one text file per shard, '// @<address> <name>' headers).
Streams one shard at a time (bounded RAM). Output stays under the ignored _work folder; never publish it.
    python tools/engine/ghidra/flat_index.py _work/ghidra-full-session9/kcd1-game
"""
import json
import sys
from pathlib import Path

src = Path(sys.argv[1])
out = src / 'flat'
out.mkdir(exist_ok=True)
n = 0
for shard in sorted((src / 'functions').glob('shard-*.jsonl')):
    target = out / (shard.stem + '.c')
    if target.exists() and target.stat().st_mtime >= shard.stat().st_mtime:
        continue
    with shard.open('r', encoding='utf-8') as f, target.open('w', encoding='utf-8', newline='\n') as o:
        for line in f:
            if not line.strip():
                continue
            r = json.loads(line)
            o.write('// @%s %s %s\n' % (r['address'], r['name'], r['status']))
            o.write((r.get('c') or '').replace('\r\n', '\n'))
            o.write('\n')
            n += 1
print('indexed', n, 'functions into', out)
