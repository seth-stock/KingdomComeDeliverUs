"""Recent KCDUS lines of a rig game's log: python tools/engine/riglog.py a|b [pattern] [n]"""
import re, sys, json
from pathlib import Path
WORK = Path(__file__).resolve().parents[2] / '_work'
side = sys.argv[1]
pat = re.compile(sys.argv[2]) if len(sys.argv) > 2 else re.compile('KCDUS')
n = int(sys.argv[3]) if len(sys.argv) > 3 else 20
r = json.loads((WORK / ('engine-current%s.json' % ('-b' if side == 'b' else ''))).read_text(encoding='utf-8-sig'))
root = Path(r['gameRoot'])
logs = [p for p in (root / 'kcd.log', root / 'kcd(1).log') if p.exists()]
log = max(logs, key=lambda p: p.stat().st_mtime)
lines = [l for l in log.read_bytes().decode('utf-8', 'replace').splitlines() if pat.search(l)]
print('\n'.join(l[:230] for l in lines[-n:]))
