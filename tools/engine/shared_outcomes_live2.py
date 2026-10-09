"""Second live check (same private game, after shared_outcomes_live.py): the pause menu watcher, stash loot, and the host's side of loot decisions.
Run:  python tools/engine/shared_outcomes_live2.py
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


def log():
    return Path(E.log_path()).read_bytes().decode('utf-8', 'replace')


def check(name, ok, detail=''):
    results.append((name, bool(ok), detail))
    print(('PASS ' if ok else 'FAIL ') + name + (('  [' + detail + ']') if detail else ''))


def num(out, key):
    m = re.search(key + r'=(-?[0-9.e+]+)', out)
    return float(m.group(1)) if m else None


SC = 'live1'

# ---------------------------------------------------------------- the pause menu watcher (the installed build, started the way the agent starts it)
lua("KCDUS_In('1~PAUSEWATCH')")
time.sleep(1.0)
check('the watcher graph reports it is running', 'KCDUS|PAUSE|menu|2' in log())
E.key('Escape')
time.sleep(2.0)
out = lua("System.LogAlways('KCDUS|T|menu|open='..tostring(KCDUS.Pause.menuOpen))")
check("opening the game's pause menu is reported to Lua (menuOpen)", 'open=true' in out and 'KCDUS|PAUSE|menu|1' in log(), out.strip()[-40:])
lua("KCDUS_T9=Calendar.GetWorldTime()")
time.sleep(3.0)
out = lua("System.LogAlways('KCDUS|T|menuworld|dworld='..(Calendar.GetWorldTime()-KCDUS_T9))")
check('the pause menu really pauses this world (so the others must be told)', (num(out, 'dworld') if num(out, 'dworld') is not None else 99) < 1, out.strip()[-40:])
E.key('Escape')
time.sleep(2.0)
out = lua("System.LogAlways('KCDUS|T|menu2|open='..tostring(KCDUS.Pause.menuOpen))")
check('closing it is reported too', 'open=false' in out and 'KCDUS|PAUSE|menu|0' in log(), out.strip()[-40:])

# ---------------------------------------------------------------- stash loot, as a guest
lua("KCDUS_In('2~LOOTMODE|1|%s|0')" % SC)
time.sleep(1.2)
out = lua("""
local p=player:GetWorldPos(); local best,bd
for _,e in pairs(System.GetEntitiesInSphere(p, 8)) do
  if tostring(e.class)=='Stash' and e.inventory and e.inventory:GetCount()>0 then
    local q=e:GetWorldPos(); local d=math.sqrt((p.x-q.x)^2+(p.y-q.y)^2)
    if not bd or d<bd then best=e; bd=d end
  end
end
if best then
  local q=best:GetWorldPos()
  KCDUS_T_STASH=best
  KCDUS_T_STASH_ID=string.format('s@%d_%d_%d', math.floor(q.x*10), math.floor(q.y*10), math.floor(q.z*10))
  System.LogAlways('KCDUS|T|stash|'..KCDUS_T_STASH_ID..' count='..best.inventory:GetCount()..' d='..bd)
else System.LogAlways('KCDUS|T|stash|none') end
""")
m = re.search(r'KCDUS\|T\|stash\|(s@[^ ]+) count=(\d+)', out)
check('a stash with items is within reach', bool(m), out.strip()[-60:])
if m:
    sid = m.group(1)
    lua("""
local e=KCDUS_T_STASH
local t=e.inventory:GetInventoryTable()
local pick
for _,h in pairs(t) do local it=ItemManager.GetItem(h) if it and it.amount and it.amount>=1 then pick=it break end end
KCDUS_T_PICK2=pick
KCDUS_T_BASE2=player.inventory:GetCountOfClass(pick.class)
e.inventory:RemoveItem(pick.id)
player.inventory:AddItem(pick.id)
System.LogAlways('KCDUS|T|stashtake|class='..pick.class..' amount='..pick.amount)
""")
    time.sleep(1.5)
    asks = [l for l in log().splitlines() if l.startswith('KCDUS|LOOT|ask|') and '|' + sid + '|' in l]
    check("a guest's take from a stash is reported by position (stashes have no names)", bool(asks), asks[-1] if asks else '')
    if asks:
        f = asks[-1].split('|')
        tok, cls, n = f[4], f[6], f[7]
        lua("KCDUS_In('3~LOOTRES|%s|%s|ok|%s|%s|%s')" % (SC, tok, sid, cls, n))
        time.sleep(1.0)
        check('an ok answer keeps the item and says confirmed', 'KCDUS|LOOT|confirmed|' + sid in log(), '')

# ---------------------------------------------------------------- the host's side: a guest's ask is decided against the host's copy
lua("KCDUS_In('4~LOOTMODE|1|%s|1')" % SC)
time.sleep(1.2)
out = lua("""
local p=player:GetWorldPos(); local best,bd
for _,e in pairs(System.GetEntitiesInSphere(p, 12)) do
  if tostring(e.class)=='Stash' and e.inventory and e.inventory:GetCount()>0 and e ~= KCDUS_T_STASH then
    local q=e:GetWorldPos(); local d=math.sqrt((p.x-q.x)^2+(p.y-q.y)^2)
    if not bd or d<bd then best=e; bd=d end
  end
end
if best then
  local q=best:GetWorldPos()
  local t=best.inventory:GetInventoryTable()
  local pick
  for _,h in pairs(t) do local it=ItemManager.GetItem(h) if it and it.amount and it.amount>=1 then pick=it break end end
  KCDUS_T_STASH3=best; KCDUS_T_PICK3=pick
  System.LogAlways('KCDUS|T|stash3|'..string.format('s@%d_%d_%d', math.floor(q.x*10), math.floor(q.y*10), math.floor(q.z*10))..'|'..pick.class..'|'..pick.amount..'|have='..best.inventory:GetCountOfClass(pick.class))
else System.LogAlways('KCDUS|T|stash3|none') end
""")
m = re.search(r'KCDUS\|T\|stash3\|(s@[^|]+)\|([^|]+)\|([0-9.]+)\|have=(\d+)', out)
check('a second stash with items is within reach', bool(m), out.strip()[-80:])
if m:
    sid3, cls3, have3 = m.group(1), m.group(2), int(m.group(4))
    lua("KCDUS_In('5~LOOTASK|%s|2|abcd1234|1|%s|%s|1')" % (SC, sid3, cls3))
    time.sleep(1.2)
    res = [l for l in log().splitlines() if l.startswith('KCDUS|LOOT|res|2|1|ok|' + sid3)]
    out = lua("System.LogAlways('KCDUS|T|have3|'..KCDUS_T_STASH3.inventory:GetCountOfClass(KCDUS_T_PICK3.class))")
    check("the host decides a guest's ask: ok, and the item leaves the HOST's copy", bool(res) and int(out.strip().split('|')[-1].strip() or -1) == have3 - 1, (res[-1] if res else '') + ' have ' + str(have3) + '->' + out.strip()[-4:])
    check("... and the other guests are told (took)", 'KCDUS|LOOT|took|' + sid3 in log(), '')
    lua("KCDUS_In('6~LOOTASK|%s|3|ffff0000|1|%s|%s|%d')" % (SC, sid3, cls3, have3 + 50))
    time.sleep(1.2)
    check('a second ask for more than is left is answered gone', bool([l for l in log().splitlines() if l.startswith('KCDUS|LOOT|res|3|1|gone|' + sid3)]), '')

# ---------------------------------------------------------------- a guest loses what the host took
lua("KCDUS_In('7~LOOTMODE|1|%s|0')" % SC)
time.sleep(1.2)
if m:
    lua("KCDUS_T_BEFORE=KCDUS_T_STASH3.inventory:GetCountOfClass(KCDUS_T_PICK3.class)")
    lua("KCDUS_In('8~LOOTTOOK|%s|%s|%s|1')" % (SC, sid3, cls3))
    time.sleep(1.2)
    out = lua("System.LogAlways('KCDUS|T|took-after|'..KCDUS_T_BEFORE..'|'..KCDUS_T_STASH3.inventory:GetCountOfClass(KCDUS_T_PICK3.class))")
    mm = re.findall(r'KCDUS\|T\|took-after\|(\d+)\|(\d+)', out)          # only this probe's line (other lines can follow it)
    parts = list(mm[-1]) if mm else []
    ok = False
    try:
        b, a = int(parts[-2]), int(parts[-1])
        ok = a == max(b - 1, 0)
    except ValueError:
        pass
    check("a guest loses what the host took from its copy (LOOTTOOK)", ok and 'KCDUS|LOOTAPPLIED|' + sid3 in log(), '|'.join(parts))

lua("KCDUS_In('9~LOOTMODE|0') KCDUS_In('10~CMBMODE|0')")
print()
bad = [r for r in results if not r[1]]
print('%d checks, %d failed' % (len(results), len(bad)))
sys.exit(1 if bad else 0)
