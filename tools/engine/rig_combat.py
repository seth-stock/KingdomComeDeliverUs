"""Two real KCD1 games in one shared world (tools/engine/rig.py session first): the shared-enemy checks.

    python tools/engine/rig_combat.py [npc-name]

A = host, B = guest. Picks a guard both games can see (or the one named), then:
  1. B hurts it as B's player would (soul:DealDamage with B's soul as the attacker): the damage must reach A's copy (one shared health).
  2. the host must give the guard to B (it turns on its attacker): B's copy runs it (no puppet there), A's copy becomes a puppet of B.
  3. A hurts it too: its health must drop in both games again.
  4. for 40 s: does the guard fight B in B's world (its blows on B: NPCHIT), and does a blow reach A when A's Henry stands in its reach (NPCHITAPPLIED)?
Disposable private profiles only. Prints PASS/FAIL lines and the evidence.
"""
import json
import re
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
results = []


def lua(side, code):
    r = subprocess.run([sys.executable, str(HERE / 'rig.py'), 'lua', side, code], cwd=ROOT, capture_output=True, text=True)
    return r.stdout


def val(out, tag):
    m = re.findall(r'KCDUS\|T\|' + tag + r'\|([^\r\n]*)', out)
    return m[-1] if m else ''


def log(side, pat, n=200):
    r = subprocess.run([sys.executable, str(HERE / 'riglog.py'), side, pat, str(n)], cwd=ROOT, capture_output=True, text=True)
    return [l for l in r.stdout.splitlines() if l.strip()]


def agent_log(side):
    p = ROOT / '_work' / 'rig' / ('agent-%s.log' % side)
    return p.read_bytes().decode('utf-8', 'replace').splitlines() if p.exists() else []


def check(name, ok, detail=''):
    results.append((name, bool(ok)))
    print(('PASS ' if ok else 'FAIL ') + name + (('  [' + str(detail)[:300] + ']') if detail else ''))


def npc_state(side, name):
    out = lua(side, "local e=System.GetEntityByName('%s') local N=KCDUS.Npcs local o=N.own['%s'] "
                    "local pp=player:GetWorldPos() local q=e and e:GetWorldPos() "
                    "System.LogAlways('KCDUS|T|st|'..(e and string.format('%%.2f',e.soul:GetState('health')) or 'none')..'|'..tostring(o and o.owner)..'|'..tostring(N.puppets['%s']~=nil)..'|'..tostring(N.me)..'|'..(q and string.format('%%.1f',math.sqrt((pp.x-q.x)^2+(pp.y-q.y)^2)) or '-')..'|'..string.format('%%.1f',player.soul:GetState('health')))"
               % (name, name, name))
    f = val(out, 'st').split('|')
    if len(f) < 6:
        return None
    return {'hp': None if f[0] == 'none' else float(f[0]), 'owner': f[1], 'puppet': f[2] == 'true', 'me': f[3], 'dist': f[4], 'henry': float(f[5])}


def main():
    name = sys.argv[1] if len(sys.argv) > 1 else None
    if not name:
        near = log('b', r'KCDUS\|NPCNEAR\|', 1)
        cands = [e.split(':')[0] for e in near[-1].split('|', 2)[2].split(';')] if near else []
        name = next((c for c in cands if 'guard' in c), cands[0] if cands else None)
    print('target:', name)
    a0, b0 = npc_state('a', name), npc_state('b', name)
    print('A before', a0); print('B before', b0)
    check('both games have the guard, at the same health', a0 and b0 and a0['hp'] is not None and abs(a0['hp'] - b0['hp']) < 0.6, (a0, b0))

    # 1. B hurts it
    lua('b', "KCDUS.Combat.mineOverride['%s']=true local e=System.GetEntityByName('%s') e.soul:DealDamage(0, 15, player.soul:GetId(), false)" % (name, name))   # a console blow sets no native target: marked as B's own fight
    time.sleep(4)
    a1, b1 = npc_state('a', name), npc_state('b', name)
    check("B's blow (15) lowers the guard's health in B's game", b1 and b0 and abs((b0['hp'] - b1['hp']) - 15) < 1.0, (b0 and b0['hp'], b1 and b1['hp']))
    check("... and in A's game: one shared health", a1 and a0 and abs((a0['hp'] - a1['hp']) - 15) < 1.0, (a0 and a0['hp'], a1 and a1['hp']))
    time.sleep(3)
    a2, b2 = npc_state('a', name), npc_state('b', name)
    owned = [l for l in agent_log('a') if 'shared enemies:' in l]
    check('the host turns the guard on its attacker: B owns it', a2 and b2 and a2['owner'] == '2' and b2['owner'] == '2', (a2, b2, owned[-1:] if owned else ''))
    check("... B's copy is not a puppet (B's game runs it) and A's copy is a puppet of B", b2 and a2 and not b2['puppet'] and a2['puppet'], (a2, b2))

    # 3. A hurts it too
    lua('a', "KCDUS.Combat.mineOverride['%s']=true local e=System.GetEntityByName('%s') e.soul:DealDamage(0, 10, player.soul:GetId(), false)" % (name, name))
    time.sleep(4)
    a3, b3 = npc_state('a', name), npc_state('b', name)
    check("A's blow (10) lands in both games", a3 and b3 and a2 and b2 and abs((a2['hp'] - a3['hp']) - 10) < 1.0 and abs((b2['hp'] - b3['hp']) - 10) < 1.0,
          (a2 and a2['hp'], a3 and a3['hp'], b2 and b2['hp'], b3 and b3['hp']))

    # 4. the guard fights back
    hb0, ha0 = b3['henry'] if b3 else None, a3['henry'] if a3 else None
    t0 = time.time()
    hits_b, applied_a = [], []
    while time.time() - t0 < 40:
        time.sleep(5)
        hits_b = [l for l in log('b', r'KCDUS\|NPCHIT\|', 50) if '|' + name + '|' in l]
        applied_a = [l for l in log('a', r'KCDUS\|NPCHIT(APPLIED|SKIP)\|', 50) if '|' + name + '|' in l]
        if hits_b and applied_a:
            break
    a4, b4 = npc_state('a', name), npc_state('b', name)
    check("the guard fights B in B's world and its blows hurt B (NPCHIT)", bool(hits_b) and b4 and hb0 is not None and b4['henry'] < hb0, (hits_b[-2:], hb0, b4 and b4['henry']))
    check("a blow reaches A's Henry when A stands in its reach (or is skipped with the reason)", bool(applied_a), applied_a[-3:])
    print('A after', a4); print('B after', b4)
    bad = [r for r in results if not r[1]]
    print('\n%d/%d passed' % (len(results) - len(bad), len(results)))


if __name__ == '__main__':
    main()
