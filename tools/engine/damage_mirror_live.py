"""Live check, ONE private real game (disposable profile, synthetic owner): the damage mirror's receiving half (npcs.lua N.takeRemoteHit).
Facts established here (the stubbed tests can only assume them):
  * soul:DealDamage(stamina, HEALTH, attacker, suppress) lowers HENRY's health, read back;
  * the engine's forward direction for an NPC set to yaw z (is it (-sin z, cos z)?);
  * a friend's blow reaches Henry only inside the puppet's reach and in front of it, and applies once.
Run after a loaded private world:   python tools/engine/damage_mirror_live.py
Spawns one disposable NPC (never saved) and removes it. Nothing here touches a real save.
"""
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import engine_session as E  # noqa: E402

import time
results, seq = [], [int(time.time() * 100)]   # the game drops a sequence number it has already seen: a fresh range every run


def lua(code, wait=1.2):
    return E.send(code, wait=wait)


def agent(line, wait=1.2):
    seq[0] += 1
    return lua("KCDUS_In('%d~%s')" % (seq[0], line), wait=wait)


def check(name, ok, detail=''):
    results.append((name, bool(ok), detail))
    print(('PASS ' if ok else 'FAIL ') + name + (('  [' + detail + ']') if detail else ''))


def val(out, tag):
    m = re.findall(r'KCDUS\|T\|' + tag + r'\|([^\r\n]*)', out)
    return m[-1] if m else ''


def log_lines(prefix):
    return [l for l in Path(E.log_path()).read_bytes().decode('utf-8', 'replace').splitlines() if l.startswith(prefix)]


def health():
    return float(val(lua("System.LogAlways('KCDUS|T|hp|'..tostring(player.soul:GetState('health')))"), 'hp') or 'nan')


# 1. the engine primitive on Henry
h0 = health()
lua("player.soul:DealDamage(0, 5, nil, true)")
h1 = health()
check("soul:DealDamage(0, 5, nil, true) lowers HENRY's health by 5 (read back)", abs((h0 - h1) - 5) < 0.6, '%.2f -> %.2f' % (h0, h1))

# 2. a disposable NPC in front of Henry (any left over from an aborted run goes first)
lua("""
for _,x in pairs(System.GetEntitiesInSphere(player:GetWorldPos(), 60) or {}) do
    local ok,n=pcall(function() return x:GetName() end)
    if ok and type(n)=='string' and string.sub(n,1,8)=='dmprobe_' then System.RemoveEntity(x.id) end
end
""", wait=1.0)
lua("""
local p=player:GetWorldPos(); local v=player.actor:GetHeadDir()
KCDUS_DM={px=p.x,py=p.y,pz=p.z}
local e=System.SpawnEntity({class='NPC',name='dmprobe_1',position={x=p.x+v.x*2.0,y=p.y+v.y*2.0,z=p.z}})
if not e or not e.soul then error('NPC spawn failed') end
e:SetFlags(ENTITY_FLAG_NO_SAVE,0); e:DisableBehaviorTreeEvaluation()
KCDUS_DM.e=e
""", wait=2.0)
# the engine's forward for an entity at yaw z is GetDirectionVector(1) = (-sin z, cos z); the player's head direction is the same vector
out = lua("""
local a=player:GetWorldAngles(); local dv=player:GetDirectionVector(1); local hd=player.actor:GetHeadDir()
System.LogAlways(string.format('KCDUS|T|fwd|%.4f|%.4f|%.4f|%.4f|%.4f', a.z, dv.x, dv.y, hd.x, hd.y))
""")
z, dx, dy, hx, hy = [float(x) for x in val(out, 'fwd').split('|')]
import math
check("forward at yaw z is (-sin z, cos z): the convention npcs.lua uses", abs(dx + math.sin(z)) < 0.01 and abs(dy - math.cos(z)) < 0.01 and abs(dx - hx) < 0.02, val(out, 'fwd'))

agent('NPCMODE|1|w1|1')
agent('NPCOWN|w1|dmprobe_1:2')


LAST = ['0,0,0,0']


def place(dist, face_henry=True, name='dmprobe_1'):
    """the owner's sample: the NPC `dist` m in front of Henry, facing him (or away)"""
    o = lua("""
local p=player:GetWorldPos(); local v=player:GetDirectionVector(1)
local x,y,zz=p.x+v.x*%f, p.y+v.y*%f, p.z
local yaw=math.atan2(v.x, -v.y)%s
KCDUS_DM.e:SetWorldPos({x=x,y=y,z=zz})
System.LogAlways(string.format('KCDUS|T|spl|%%.3f,%%.3f,%%.3f,%%.4f', x, y, zz, yaw))
""" % (dist, dist, '' if face_henry else ' + math.pi'), wait=1.0)
    x, y, zz, yaw = val(o, 'spl').split(',')
    LAST[0] = '%s,%s,%s,%s' % (x, y, zz, yaw)



def burst(lines, wait=1.2):
    """agent lines delivered in ONE console call: each harness call takes seconds, an ownership entry lives 3 s"""
    code = ' '.join("KCDUS_In('%d~%s')" % (seq.__setitem__(0, seq[0] + 1) or seq[0], l) for l in lines)
    return lua(code, wait=wait)


def blow(n, hp):
    before = health()
    burst(['NPCOWN|w1|dmprobe_1:2', 'NPCSET|w1|2|dmprobe_1,%s,0.00,0.00,1' % LAST[0], 'NPCHITIN|w1|2|dmprobe_1|%d|%s' % (n, hp)])
    return before, health()


place(1.5, True)
b, a = blow(1, '12.0')
check("a friend's blow takes 12 health from Henry standing 1.5 m in front of the puppet", abs((b - a) - 12) < 0.8, '%.2f -> %.2f' % (b, a))
ap = log_lines('KCDUS|NPCHITAPPLIED|')
check('... with a read-back line (asked 12.0, lost ~12, ok 1)', bool(ap) and ap[-1].startswith('KCDUS|NPCHITAPPLIED|dmprobe_1|1|12.0|') and ap[-1].endswith('|1'), ap[-1] if ap else '')
b, a = blow(1, '12.0')
check('the same blow again changes nothing', abs(b - a) < 0.3, '%.2f -> %.2f' % (b, a))
place(1.5, False)
b, a = blow(2, '12.0')
sk = log_lines('KCDUS|NPCHITSKIP|')
check("a blow from a puppet facing AWAY does not hurt him", abs(b - a) < 0.3 and sk and sk[-1].endswith('|2|behind'), '%.2f -> %.2f %s' % (b, a, sk[-1] if sk else ''))
place(6.0, True)
b, a = blow(3, '12.0')
sk = log_lines('KCDUS|NPCHITSKIP|')
check("a blow from a puppet 6 m away does not hurt him", abs(b - a) < 0.3 and sk and sk[-1].endswith('|3|out-of-reach'), '%.2f -> %.2f %s' % (b, a, sk[-1] if sk else ''))
place(1.5, True); agent('NPCDMGMIRROR|0')
b, a = blow(4, '12.0')
check("with the mirror switched off nothing happens", abs(b - a) < 0.3, '%.2f -> %.2f' % (b, a))
agent('NPCDMGMIRROR|1')

agent('NPCMODE|0')
lua("if KCDUS_DM and KCDUS_DM.e then System.RemoveEntity(KCDUS_DM.e.id) end KCDUS_DM=nil", wait=1.0)
print()
bad = [r for r in results if not r[1]]
print('%d/%d passed' % (len(results) - len(bad), len(results)))
sys.exit(1 if bad else 0)
