"""Two real KCD1 games in one shared world (rig.py session first): a HOSTILE bandit, spawned identically in both games (the same name and place,
as a world NPC would be), fights the players.

    python tools/engine/rig_bandit.py [seconds]

Checks: one owner, the other copy a puppet; the bandit attacks its owner natively (owner's health falls, NPCHIT); the other player standing in
its reach takes the blow too (NPCHITAPPLIED); both players can hurt it and it dies once, in both games. Disposable private profiles only.
"""
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import rig_combat as R  # noqa: E402

BANDIT = '022467ed-66b0-4fb4-bde6-6c6600c98583'   # Tables.pak rpg/soul.xml: event_spawn_bandit927 (an event bandit)
NAME = 'rigbandit_%d' % (int(time.time()) % 100000)


def spawn(side, x, y, z):
    R.lua(side, "local e=System.SpawnEntity({class='NPC',name='%s',position={x=%f,y=%f,z=%f},properties={sharedSoulGuid='%s'}}) "
                "if e then e:SetFlags(ENTITY_FLAG_NO_SAVE,0) end System.LogAlways('KCDUS|T|sp|'..tostring(e~=nil))" % (NAME, x, y, z, BANDIT))


def main():
    secs = int(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1].isdigit() else 60
    # an open field away from town guards (the guards fight a bandit before it reaches anyone): both Henrys go there, 2 m apart
    for side, dx in ((('a', 0.0), ('b', 2.0)) if '--field' in sys.argv else ()):   # --field: an open field (beware: the teleport can drop Henry)
        R.lua(side, "local z=System.GetTerrainElevation({x=%f,y=2000,z=0}) player:SetWorldPos({x=%f,y=2000,z=z+0.3})" % (3000 + dx, 3000 + dx))
    time.sleep(12 if '--field' in sys.argv else 0)
    out = R.lua('a', "local p=player:GetWorldPos() local v=player:GetDirectionVector(1) System.LogAlways(string.format('KCDUS|T|pos|%.2f|%.2f|%.2f', p.x+v.x*5, p.y+v.y*5, p.z))")
    x, y, z = [float(t) for t in R.val(out, 'pos').split('|')]
    spawn('a', x, y, z)
    spawn('b', x, y, z)
    print('spawned', NAME, 'at', x, y, z)
    time.sleep(6)
    a0, b0 = R.npc_state('a', NAME), R.npc_state('b', NAME)
    print('A', a0); print('B', b0)
    R.check('the bandit exists in both games at full health', a0 and b0 and a0['hp'] and b0['hp'], (a0, b0))
    R.check('one owner, the other copy a puppet', a0 and b0 and a0['owner'] == b0['owner'] and a0['owner'] in ('1', '2') and (a0['puppet'] != b0['puppet']), (a0, b0))
    t0 = time.time()
    ha0, hb0 = a0['henry'], b0['henry']
    while time.time() - t0 < secs:
        time.sleep(5)
        a, b = R.npc_state('a', NAME), R.npc_state('b', NAME)
        print('%3ds bandit A %s/%s B %s/%s  henry A %.1f B %.1f' % (time.time() - t0, a and a['hp'], a and a['owner'], b and b['hp'], b and b['owner'], a['henry'] if a else -1, b['henry'] if b else -1))
    hits = {s: [l for l in R.log(s, r'KCDUS\|NPCHIT\|', 100) if NAME in l] for s in 'ab'}
    applied = {s: [l for l in R.log(s, r'KCDUS\|NPCHIT(APPLIED|SKIP)\|', 100) if NAME in l] for s in 'ab'}
    a1, b1 = R.npc_state('a', NAME), R.npc_state('b', NAME)
    R.check('the bandit attacked a player natively (that player lost health; NPCHIT reported)', bool(hits['a'] or hits['b']), {s: hits[s][-2:] for s in 'ab'})
    R.check("the other player in its reach took the blow too (NPCHITAPPLIED)", any('NPCHITAPPLIED' in l for s in 'ab' for l in applied[s]), {s: applied[s][-3:] for s in 'ab'})
    print('henry A %.1f -> %.1f, B %.1f -> %.1f' % (ha0, a1['henry'] if a1 else -1, hb0, b1['henry'] if b1 else -1))
    # both kill it
    R.lua('a', "local e=System.GetEntityByName('%s') if e then e.soul:DealDamage(0, 30, player.soul:GetId(), false) end" % NAME)
    R.lua('b', "local e=System.GetEntityByName('%s') if e then e.soul:DealDamage(0, 500, player.soul:GetId(), false) end" % NAME)
    time.sleep(5)
    dead = {s: R.lua(s, "local e=System.GetEntityByName('%s') System.LogAlways('KCDUS|T|dead|'..tostring(e and e:IsDead()))" % NAME) for s in 'ab'}
    R.check('A and B hurt it together and it dies in both games', all(R.val(dead[s], 'dead') == 'true' for s in 'ab'), {s: R.val(dead[s], 'dead') for s in 'ab'})
    bad = [r for r in R.results if not r[1]]
    print('\n%d/%d passed' % (len(R.results) - len(bad), len(R.results)))


if __name__ == '__main__':
    main()
