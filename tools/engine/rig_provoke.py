"""Two real KCD1 games in one shared world (rig.py session first): a GUARD provoked by player A (a crime: blows with A as the attacker) fights back.

    python tools/engine/rig_provoke.py [guard] [seconds]

Checks: the guard's running copy fights A in A's world (A's Henry loses health natively, NPCHIT is reported with the health lost); B's copy is a
puppet; when B's Henry stands in the guard's reach, the same blow reaches B (NPCHITAPPLIED), otherwise it is skipped with the reason
(NPCHITSKIP); the guard's health stays one shared value. Disposable private profiles only.
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import rig_combat as R  # noqa: E402


def main():
    name = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].isdigit() else 'rat_guard23'
    secs = int(sys.argv[-1]) if sys.argv[-1].isdigit() else 60
    # B's Henry steps next to A's (so the guard's blows at A can reach B too)
    out = R.lua('a', "local p=player:GetWorldPos() System.LogAlways(string.format('KCDUS|T|pa|%.2f|%.2f|%.2f', p.x, p.y, p.z))")
    x, y, z = [float(t) for t in R.val(out, 'pa').split('|')]
    R.lua('b', "player:SetWorldPos({x=%f,y=%f,z=%f})" % (x + 0.8, y, z))
    time.sleep(3)
    a0, b0 = R.npc_state('a', name), R.npc_state('b', name)
    print('A', a0); print('B', b0)
    for _ in range(3):        # A provokes it: blows with A's soul as the attacker, marked as A's own fight
        R.lua('a', "KCDUS.Combat.mineOverride['%s']=true local e=System.GetEntityByName('%s') if e then e.soul:DealDamage(0, 4, player.soul:GetId(), false) end" % (name, name))
        time.sleep(1.5)
    t0 = time.time()
    while time.time() - t0 < secs:
        time.sleep(6)
        a, b = R.npc_state('a', name), R.npc_state('b', name)
        print('%3ds guard A %s/%s B %s/%s  henry A %.1f B %.1f' % (time.time() - t0, a and a['hp'], a and a['owner'], b and b['hp'], b and b['owner'],
                                                                   a['henry'] if a else -1, b['henry'] if b else -1))
    hits = {s: [l for l in R.log(s, r'KCDUS\|NPCHIT\|', 100) if '|' + name + '|' in l] for s in 'ab'}
    applied = {s: [l for l in R.log(s, r'KCDUS\|NPCHIT(APPLIED|SKIP)\|', 100) if '|' + name + '|' in l] for s in 'ab'}
    a1, b1 = R.npc_state('a', name), R.npc_state('b', name)
    R.check("the guard fought a player in that player's own world (health lost natively, NPCHIT)", bool(hits['a'] or hits['b']), {s: hits[s][-3:] for s in 'ab'})
    R.check("the other player in its reach took its blow too (NPCHITAPPLIED)", any('APPLIED' in l for s in 'ab' for l in applied[s]), {s: applied[s][-4:] for s in 'ab'})
    R.check('the guard keeps one shared health', a1 and b1 and abs(a1['hp'] - b1['hp']) < 1.0, (a1 and a1['hp'], b1 and b1['hp']))
    print('henry A %.1f -> %.1f, B %.1f -> %.1f' % (a0['henry'], a1['henry'] if a1 else -1, b0['henry'], b1['henry'] if b1 else -1))
    bad = [r for r in R.results if not r[1]]
    print('\n%d/%d passed' % (len(R.results) - len(bad), len(R.results)))


if __name__ == '__main__':
    main()
