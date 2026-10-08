"""Live check of the shared pause, fights and loot against ONE private real game (engine_session.py guards: disposable profile, owned PID, real saves unchanged).
The second player is synthetic: the harness sends the agent's lines (CMBAPPLY, LOOTRES, ...) exactly as the agent would. Nothing here touches a real save.

Run after `engine_session.py launch ...` and a loaded world (world_readback.lua says world-ready|true|false):
    python tools/engine/shared_outcomes_live.py
"""
import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import engine_session as E  # noqa: E402

results = []


def lua(code, wait=1.2):
    return E.send(code, wait=wait)


def check(name, ok, detail=''):
    results.append((name, bool(ok), detail))
    print(('PASS ' if ok else 'FAIL ') + name + (('  [' + detail + ']') if detail else ''))


def lines(out, tag):
    return [l for l in out.splitlines() if tag in l]


def num(out, key):
    m = re.search(key + r'=(-?[0-9.e+]+)', out)
    return float(m.group(1)) if m else None


# ---------------------------------------------------------------- the modules loaded at startup
out = lua("System.LogAlways('KCDUS|T|mods|'..type(KCDUS.Pause)..' '..type(KCDUS.Combat)..' '..type(KCDUS.Loot))")
check('the pause, combat and loot modules loaded at game start', 'table table table' in out, out.strip()[-80:])

# ---------------------------------------------------------------- pause: freeze, lease, release
lua("KCDUS_T0=Calendar.GetWorldTime() KCDUS_F0=System.GetFrameID() KCDUS_In('1~FREEZE|1|6000')")
time.sleep(3.0)
out = lua("System.LogAlways('KCDUS|T|frozen|dworld='..(Calendar.GetWorldTime()-KCDUS_T0)..' dframes='..(System.GetFrameID()-KCDUS_F0)..' scale='..tostring(System.GetCVar('t_scale')))")
dworld, scale = num(out, 'dworld'), num(out, 'scale')
check('FREEZE holds the world (world time stands, frames go on, t_scale 0.001)', dworld is not None and dworld < 2 and scale == 0.001, out.strip()[-90:])
out = lua("KCDUS_T1=Calendar.GetWorldTime() KCDUS_In('2~FREEZE|0') System.LogAlways('KCDUS|T|released|scale='..tostring(System.GetCVar('t_scale')))")
check('FREEZE|0 puts the scale back at once', num(out, 'scale') == 1, out.strip()[-60:])
time.sleep(2.0)
out = lua("System.LogAlways('KCDUS|T|running|dworld='..(Calendar.GetWorldTime()-KCDUS_T1))")
check('the world runs again after the release', (num(out, 'dworld') or 0) > 20, out.strip()[-60:])
# the lease: a short one lets go by itself
lua("KCDUS_In('3~FREEZE|1|240')")
time.sleep(1.0)
out = lua("System.LogAlways('KCDUS|T|lease|frozen='..tostring(KCDUS.Pause.frozen)..' scale='..tostring(System.GetCVar('t_scale')))")
if 'frozen=true' in out:
    time.sleep(8.0)
    out = lua("System.LogAlways('KCDUS|T|lease2|frozen='..tostring(KCDUS.Pause.frozen)..' scale='..tostring(System.GetCVar('t_scale')))")
check('a lease nobody renews lets go by itself (no agent needed)', 'frozen=false' in out and 'scale=1' in out, out.strip()[-70:])

# ---------------------------------------------------------------- combat
SC = 'live1'
lua("KCDUS_In('4~CMBMODE|1|%s')" % SC)
time.sleep(1.0)
sel = lua("""
local p=player:GetWorldPos(); local best,bd
for _,e in pairs(System.GetEntitiesInSphere(p, 14)) do
  if e ~= player and KCDUS.Combat.isPerson(e) and e.soul and not e:IsDead() and e:GetName() ~= '' then
    local q=e:GetWorldPos(); local d=math.sqrt((p.x-q.x)^2+(p.y-q.y)^2)
    if d>1.0 and (not bd or d<bd) then best=e; bd=d end
  end
end
System.LogAlways('KCDUS|T|npc|'..tostring(best and best:GetName())..'|'..tostring(bd))
""")
m = re.search(r'KCDUS\|T\|npc\|([^|]+)\|', sel)
npc = m.group(1) if m else None
check('an NPC is near the player to fight', npc not in (None, 'nil'), sel.strip()[-60:])
if npc and npc != 'nil':
    # the local player's blow: the ordinary damage path with the player as attacker
    out = lua("local e=System.GetEntityByName('%s') e.soul:DealDamage(0,6,player.soul:GetId(),false)" % npc, wait=1.5)
    time.sleep(0.6)
    out = lua("System.LogAlways('KCDUS|T|sync')", wait=0.8)
    allout = out
    log = Path(E.log_path()).read_bytes().decode('utf-8', 'replace')
    hit = [l for l in log.splitlines() if l.startswith('KCDUS|CMB|' + npc + '|')]
    check("the local player's blow is reported (CMB line with the damage)", bool(hit), hit[-1] if hit else '')
    # a friend's blow applied here: dealt the ordinary way, and not echoed back
    before = len([l for l in log.splitlines() if l.startswith('KCDUS|CMB|' + npc + '|')])
    lua("KCDUS_In('5~CMBAPPLY|%s|%s|10|0')" % (SC, npc))
    time.sleep(1.2)
    log = Path(E.log_path()).read_bytes().decode('utf-8', 'replace')
    applied = [l for l in log.splitlines() if l.startswith('KCDUS|CMBAPPLIED|' + npc + '|')]
    check("a friend's blow is applied to this copy with readback", bool(applied) and applied[-1].endswith('|applied'), applied[-1] if applied else '')
    if applied:
        parts = applied[-1].split('|')
        check('... and it lowered the health by about 10', abs((float(parts[3]) - float(parts[4])) - 10) < 1.5, applied[-1])
    after = len([l for l in log.splitlines() if l.startswith('KCDUS|CMB|' + npc + '|')])
    check('... and the local watcher does not echo it back', after == before, f'{before}->{after}')
    # a friend's kill
    lua("KCDUS_In('6~CMBAPPLY|%s|%s|1|1')" % (SC, npc))
    time.sleep(1.5)
    out = lua("local e=System.GetEntityByName('%s') System.LogAlways('KCDUS|T|dead|'..tostring(e:IsDead())..' hp='..tostring(e.soul:GetState('health'))..' inv='..tostring(e.inventory:GetCount()))" % npc)
    check("a friend's kill kills this copy too (IsDead, health 0, corpse inventory readable)", 'dead|true' in out, out.strip()[-70:])
    time.sleep(1.0)
    log = Path(E.log_path()).read_bytes().decode('utf-8', 'replace')
    check('... and the kill is not reported back as the local player\'s', not [l for l in log.splitlines() if l.startswith('KCDUS|CMB|' + npc + '|') and l.endswith('|1')], '')

    # ---------------------------------------------------------------- loot of that corpse
    lua("KCDUS_In('7~LOOTMODE|1|%s|0')" % SC)           # as a guest
    time.sleep(1.2)
    lua("""
local e=System.GetEntityByName('%s')
local t=e.inventory:GetInventoryTable()
local pick
for _,h in pairs(t) do local it=ItemManager.GetItem(h) if it and it.amount and it.amount>=1 then pick=it break end end
KCDUS_T_PICK=pick
KCDUS_T_BASE=player.inventory:GetCountOfClass(pick.class)
local ok1,e1=pcall(function() e.inventory:RemoveItem(pick.id) end)
local ok2,e2=pcall(function() player.inventory:AddItem(pick.id) end)
System.LogAlways('KCDUS|T|take|class='..pick.class..' amount='..pick.amount..' remove='..tostring(ok1)..' add='..tostring(ok2)..' playerHas='..player.inventory:GetCountOfClass(pick.class)..' base='..KCDUS_T_BASE)
""" % npc)
    time.sleep(1.5)
    log = Path(E.log_path()).read_bytes().decode('utf-8', 'replace')
    asks = [l for l in log.splitlines() if l.startswith('KCDUS|LOOT|ask|')]
    check("a guest's take from a corpse is reported as an ask for the host to decide", bool(asks), asks[-1] if asks else [l for l in log.splitlines() if 'T|take' in l][-1:] and 'see take line')
    if asks:
        tok = asks[-1].split('|')[4]
        cls = asks[-1].split('|')[6]
        n = asks[-1].split('|')[7]
        lua("KCDUS_In('8~LOOTRES|%s|%s|gone|%s|%s|%s')" % (SC, tok, npc, cls, n))
        time.sleep(1.0)
        out = lua("System.LogAlways('KCDUS|T|after-gone|playerHas='..player.inventory:GetCountOfClass(KCDUS_T_PICK.class)..' base='..KCDUS_T_BASE)")
        check('when the host says gone the item is taken back off Henry', num(out, 'playerHas') == num(out, 'base'), out.strip()[-70:])

print()
bad = [r for r in results if not r[1]]
print('%d checks, %d failed' % (len(results), len(bad)))
sys.exit(1 if bad else 0)
