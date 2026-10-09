"""Live check of shared enemies, ground items, saddlebags, shops, quest rewards and the own-menu option against ONE private real game
(engine_session.py guards: disposable profile, owned PID, real saves unchanged). The second player is synthetic: the harness sends the agent's lines exactly as
the agent would. A pick-up, a drop, a purchase are simulated by script (the item moved as the game's own screens move it): the harness may press only menu keys.

Run after `engine_session.py launch ...` and a loaded world (world_readback.lua says world-ready|true|false), with the build under test installed:
    python tools/engine/shared_items_live.py
"""
import re
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import engine_session as E  # noqa: E402

results = []
SC = 'live4'
seq = [100]


def lua(code, wait=1.2):
    return E.send(code, wait=wait)


def agent(line, wait=1.0):
    seq[0] += 1
    return lua("KCDUS_In('%d~%s')" % (seq[0], line), wait=wait)


def check(name, ok, detail=''):
    results.append((name, bool(ok), detail))
    print(('PASS ' if ok else 'FAIL ') + name + (('  [' + detail + ']') if detail else ''))


def log_lines(prefix):
    log = Path(E.log_path()).read_bytes().decode('utf-8', 'replace')
    return [l for l in log.splitlines() if l.startswith(prefix)]


def val(out, tag):
    m = re.findall(r'KCDUS\|T\|' + tag + r'\|([^\r\n]*)', out)
    return m[-1] if m else ''


# ---------------------------------------------------------------- loaded
out = lua("System.LogAlways('KCDUS|T|mods|'..type(KCDUS.Npcs)..' '..type(KCDUS.Rewards)..' '..type(KCDUS.Loot.gainLog))")
check('the npcs and rewards modules loaded with the new loot', val(out, 'mods') == 'table table table', val(out, 'mods'))

# ---------------------------------------------------------------- own menu: the inventory does not slow time
out = lua("System.LogAlways('KCDUS|T|inv0|'..tostring(System.GetCVar('wh_ui_InventoryPauseEnabled')))")
inv0 = val(out, 'inv0')
agent('OWNPAUSE|0')
out = lua("System.LogAlways('KCDUS|T|inv1|'..tostring(System.GetCVar('wh_ui_InventoryPauseEnabled')))")
check("OWNPAUSE|0 switches the inventory's slow-down off", inv0 not in ('', 'nil') and val(out, 'inv1') in ('0', '0.0'), inv0 + ' -> ' + val(out, 'inv1'))
agent('OWNPAUSE|1')
out = lua("System.LogAlways('KCDUS|T|inv2|'..tostring(System.GetCVar('wh_ui_InventoryPauseEnabled')))")
check("OWNPAUSE|1 puts the game's value back", val(out, 'inv2') == inv0, val(out, 'inv2'))

# ---------------------------------------------------------------- shared enemies
agent('NPCMODE|1|%s|1' % SC)
sel = lua("""
local p=player:GetWorldPos(); local best,bd
for _,e in pairs(System.GetEntitiesInSphere(p, 30)) do
  if e ~= player and KCDUS.Combat.isPerson(e) and e.soul and not e:IsDead() and e:GetName() ~= '' and not string.find(e:GetName(),'kcdus_',1,true) then
    local q=e:GetWorldPos(); local d=math.sqrt((p.x-q.x)^2+(p.y-q.y)^2)
    if string.find(e:GetName(),'guard') then d=d-1000 end   -- an armed guard first (the weapon is mirrored)
    if d>2-1000 and math.abs(d)>2 and (not bd or d<bd) then best=e; bd=d end
  end
end
local q=best and best:GetWorldPos()
System.LogAlways('KCDUS|T|npc|'..tostring(best and best:GetName())..'|'..(q and string.format('%.2f,%.2f,%.2f',q.x,q.y,q.z) or ''))
""")
npc, pos = (val(sel, 'npc').split('|') + [''])[:2]
check('an NPC is near the player', npc not in ('', 'nil'), npc)
time.sleep(1.2)
check('the game reports the NPCs near the player (NPCNEAR)', any(npc in l for l in log_lines('KCDUS|NPCNEAR|')), (log_lines('KCDUS|NPCNEAR|') or [''])[-1][:120])
if npc and npc != 'nil':
    x, y, z = [float(v) for v in pos.split(',')]
    tx, ty = x + 3.0, y + 1.0
    # the agent's cadence, inside the game (one harness call takes seconds): ownership every second, the owner's samples five times a second
    lua("""
function KCDUS_T_FEED(owner, secs, sample, sampleSecs)
  local n = 0
  local function tick()
    n = n + 1
    if n > secs * 5 then return end
    if n %% 5 == 1 then KCDUS_In((5000 + n) .. '~NPCOWN|%s|%s:' .. owner) end
    if sample and n <= (sampleSecs or secs) * 5 then KCDUS_In((6000 + n) .. '~NPCSET|%s|2|' .. sample) end
    Script.SetTimer(200, tick)
  end
  tick()
end
""" % (SC, npc, SC))
    sample = '%s,%.2f,%.2f,%.2f,1.200,0.00,0.00,1' % (npc, tx, ty, z)
    lua("KCDUS_T_FEED('2', 8, '%s')" % sample, wait=2.0)
    out = lua("local e=System.GetEntityByName('%s') local q=e:GetWorldPos() local a=e:GetWorldAngles() local okw,w=pcall(function() return e.human:IsWeaponDrawn() end) "
              "System.LogAlways(string.format('KCDUS|T|pup|%%.2f|%%.2f|%%.3f|%%s|%%s', q.x, q.y, a.z, tostring(KCDUS.Npcs.puppets['%s'] ~= nil), tostring(okw and w)))" % (npc, npc))
    v = val(out, 'pup').split('|')
    check("a friend's samples make this copy a puppet (brain off) placed where the owner's copy is",
          len(v) == 5 and v[3] == 'true' and abs(float(v[0]) - tx) < 0.8 and abs(float(v[1]) - ty) < 0.8, val(out, 'pup'))
    check("... facing the owner's way", len(v) == 5 and abs(float(v[2]) - 1.2) < 0.05, val(out, 'pup'))
    check('the puppet was announced (NPCPUP on)', any(l.startswith('KCDUS|NPCPUP|%s|on|2' % npc) for l in log_lines('KCDUS|NPCPUP|')), '')
    out = lua("local q=System.GetEntityByName('%s'):GetWorldPos() System.LogAlways(string.format('KCDUS|T|held|%%.2f|%%.2f', q.x, q.y))" % npc)
    h = val(out, 'held').split('|')
    check('... and stays there while the owner keeps it there (seconds later)', len(h) == 2 and abs(float(h[0]) - tx) < 0.8 and abs(float(h[1]) - ty) < 0.8, val(out, 'held'))
    out = lua("local okw,w=pcall(function() return System.GetEntityByName('%s').human:IsWeaponDrawn() end) System.LogAlways('KCDUS|T|wpn|'..tostring(okw and w))" % npc)
    if val(out, 'wpn') == 'true':
        check("... with its weapon drawn as the owner's is (an armed NPC)", True, npc)
    else:
        # the control: can this NPC draw by script at all, with its own brain on? (some cannot: then the mirror has nothing to show)
        lua("local g=System.GetEntityByName('%s') g:EnableBehaviorTreeEvaluation() g.human:DrawWeapon()" % npc, wait=4.0)
        ctl = lua("System.LogAlways('KCDUS|T|ctl|'..tostring(System.GetEntityByName('%s').human:IsWeaponDrawn()))" % npc)
        if val(ctl, 'ctl') == 'true':
            check("... with its weapon drawn as the owner's is (an armed NPC)", False, npc + ' draws with its brain on but not as a puppet')
        else:
            print('SKIP the weapon check: %s cannot draw a weapon by script even with its own brain (the control)' % npc)
        lua("System.GetEntityByName('%s'):DisableBehaviorTreeEvaluation()" % npc)
    time.sleep(4.0)                                          # the feed ends
    # it comes nearest to me: mine again
    lua("KCDUS_T_FEED('1', 4, nil)", wait=1.5)
    out = lua("System.LogAlways('KCDUS|T|mine|'..tostring(KCDUS.Npcs.puppets['%s'] ~= nil))" % npc)
    check('when it becomes mine the brain is given back (no puppet)', val(out, 'mine') == 'false' and any(l.startswith('KCDUS|NPCPUP|%s|off|' % npc) for l in log_lines('KCDUS|NPCPUP|')), val(out, 'mine'))
    check('an NPC this game owns is reported for the friends (NPCST)', any(l.startswith('KCDUS|NPCST|') and (npc + ',') in l for l in log_lines('KCDUS|NPCST|')), '')
    time.sleep(4.0)
    # the owner's samples stop while it still owns it: the brain comes back by itself
    lua("KCDUS_T_FEED('2', 9, '%s', 1)" % sample, wait=0.5)
    out = lua("System.LogAlways('KCDUS|T|on2|'..tostring(KCDUS.Npcs.puppets['%s'] ~= nil))" % npc)
    time.sleep(4.0)
    out2 = lua("System.LogAlways('KCDUS|T|stale|'..tostring(KCDUS.Npcs.puppets['%s'] ~= nil))" % npc)
    check('a puppet without samples for 4 s gets its brain back by itself (ownership still held)',
          val(out2, 'stale') == 'false' and any(l.startswith('KCDUS|NPCPUP|%s|off|stale' % npc) for l in log_lines('KCDUS|NPCPUP|')), val(out, 'on2') + ' -> ' + val(out2, 'stale'))
agent('NPCMODE|0')

# ---------------------------------------------------------------- the ground
CLS = '1e7932a3-736a-49c6-baba-8ff7a0a86850'
agent('LOOTMODE|1|%s|0' % SC)                    # a guest
lua("local p=player:GetWorldPos() KCDUS_T_G=System.SpawnEntity({class='PickableItem',name='kcdus_t_ground',position={x=p.x+1.5,y=p.y,z=p.z+0.2},"
    "properties={guidItemClassId='%s',fHealth=1,nAmount=1,bOnlyNPC=0}}) KCDUS_T_BASE=player.inventory:GetCountOfClass('%s')" % (CLS, CLS))
time.sleep(2.0)                                   # it settles and is seen
asks0 = len(log_lines('KCDUS|LOOT|ask|'))
lua("System.RemoveEntity(KCDUS_T_G.id) player.inventory:AddItem(ItemManager.CreateItem('%s',1,1))" % CLS)   # what picking it up does
time.sleep(1.5)
asks = log_lines('KCDUS|LOOT|ask|')[asks0:]
check("a guest's pick-up from the ground is an ask with the ground's id", any('|g@' in a and CLS in a for a in asks), asks[-1] if asks else '')
if asks:
    f = asks[-1].split('|')
    agent('LOOTRES|%s|%s|gone|%s|%s|1' % (SC, f[4], f[5], CLS))
    out = lua("System.LogAlways('KCDUS|T|gback|'..player.inventory:GetCountOfClass('%s')..'|'..KCDUS_T_BASE)" % CLS)
    g = val(out, 'gback').split('|')
    check('... and "gone" takes it back off Henry', len(g) == 2 and g[0] == g[1], val(out, 'gback'))

# a friend's drop lies here too
out = lua("local p=player:GetWorldPos() System.LogAlways(string.format('KCDUS|T|gid|g@%d_%d_%d', math.floor((p.x+2)*10), math.floor(p.y*10), math.floor(p.z*10)))")
gid = val(out, 'gid')
out = lua("local n=0 local a for _,e in pairs(System.GetEntitiesInSphere(player:GetWorldPos(),5)) do if e.class=='PickableItem' and not e.npcOnly then local it=ItemManager.GetItem(e.item:GetId()) "
          "if it and it.class=='%s' then n=n+1 a=it.amount end end end System.LogAlways('KCDUS|T|dropped|'..n..'|'..tostring(a))" % CLS)
n0 = int(val(out, 'dropped').split('|')[0] or 0)
agent('LOOTDROP|%s|%s|%s|2|0.5000' % (SC, gid, CLS), wait=1.5)
out = lua("local n=0 local a for _,e in pairs(System.GetEntitiesInSphere(player:GetWorldPos(),5)) do if e.class=='PickableItem' and not e.npcOnly then local it=ItemManager.GetItem(e.item:GetId()) "
          "if it and it.class=='%s' then n=n+1 a=it.amount end end end System.LogAlways('KCDUS|T|dropped|'..n..'|'..tostring(a))" % CLS)
d = val(out, 'dropped').split('|')
check("a friend's drop appears on this ground with its amount", len(d) == 2 and int(d[0]) == n0 + 1 and d[1] == '2', '%d -> %s' % (n0, val(out, 'dropped')))
time.sleep(1.2)
check('... and is not reported back as this player\'s drop', not [l for l in log_lines('KCDUS|LOOT|drop|') if gid in l], '')

# the host decides a guest's ask for that thing: its copy is removed
agent('LOOTMODE|1|%s|1' % SC)                    # now the host
time.sleep(1.2)
agent('LOOTASK|%s|2|abcdef12|7|%s|%s|2' % (SC, gid, CLS), wait=1.5)
res = [l for l in log_lines('KCDUS|LOOT|res|2|7|') if gid in l]
out = lua("local n=0 for _,e in pairs(System.GetEntitiesInSphere(player:GetWorldPos(),5)) do if e.class=='PickableItem' and not e.npcOnly then local it=ItemManager.GetItem(e.item:GetId()) "
          "if it and it.class=='%s' then n=n+1 end end end System.LogAlways('KCDUS|T|left|'..n)" % CLS)
check("the host grants a guest's ground ask and its own copy is removed", bool(res) and '|ok|' in res[-1] and val(out, 'left') == str(n0), (res[-1] if res else '') + ' left=' + val(out, 'left') + ' before=' + str(n0))

# a drop at Henry's feet is told
lua("player.inventory:AddItem(ItemManager.CreateItem('%s',1,1))" % CLS)
time.sleep(1.2)
drops0 = len(log_lines('KCDUS|LOOT|drop|'))
lua("local p=player:GetWorldPos() player.inventory:DeleteItemOfClass('%s',1) System.SpawnEntity({class='PickableItem',name='kcdus_t_drop',position={x=p.x+0.8,y=p.y,z=p.z+0.2},"
    "properties={guidItemClassId='%s',fHealth=1,nAmount=1,bOnlyNPC=0}})" % (CLS, CLS))
time.sleep(1.5)
check("this player's drop is told to the friends", len(log_lines('KCDUS|LOOT|drop|')) > drops0, (log_lines('KCDUS|LOOT|drop|') or [''])[-1])

# ---------------------------------------------------------------- saddlebags
out = lua("local n,s=0,'' for _,e in pairs(System.GetEntitiesInSphere(player:GetWorldPos(),300)) do if e.class=='Horse' then n=n+1 if n<=3 then "
          "s=s..e:GetName()..'/inv='..tostring(e.inventory~=nil and e.inventory:GetCount())..';' end end end System.LogAlways('KCDUS|T|horses|'..n..'|'..s)")
print('INFO horses near:', val(out, 'horses'))
hz = val(out, 'horses').split('|')
if len(hz) == 2 and int(hz[0]) > 0 and 'inv=' in hz[1] and 'inv=nil' not in hz[1].split(';')[0]:
    hname = hz[1].split('/')[0]
    lua("local h=System.GetEntityByName('%s') KCDUS_T_H=h player:SetWorldPos({x=h:GetWorldPos().x+1.5,y=h:GetWorldPos().y,z=h:GetWorldPos().z})" % hname, wait=2.0)
    agent('LOOTMODE|1|%s|0' % SC)
    time.sleep(1.5)
    out = lua("System.LogAlways('KCDUS|T|hb0|'..KCDUS_T_H.inventory:GetCountOfClass('%s'))" % CLS)
    hb0 = int(val(out, 'hb0') or 0)
    puts0 = len(log_lines('KCDUS|LOOT|put|'))
    lua("player.inventory:AddItem(ItemManager.CreateItem('%s',1,1))" % CLS)
    time.sleep(1.2)
    lua("player.inventory:DeleteItemOfClass('%s',1) KCDUS_T_H.inventory:AddItem(ItemManager.CreateItem('%s',1,1))" % (CLS, CLS))
    time.sleep(1.5)
    puts = log_lines('KCDUS|LOOT|put|')[puts0:]
    check("a put into a horse's saddlebag is told to the friends", any(hname in p for p in puts), puts[-1] if puts else '')
    agent('LOOTPUT|%s|%s|%s|3|1.0000' % (SC, hname, CLS), wait=1.2)
    out = lua("System.LogAlways('KCDUS|T|hbag|'..KCDUS_T_H.inventory:GetCountOfClass('%s'))" % CLS)
    check("a friend's put lands in this copy of the saddlebag", val(out, 'hbag') == str(hb0 + 4), '%d -> %s' % (hb0, val(out, 'hbag')))
else:
    check('a horse with saddlebags within 300 m to test', False, val(out, 'horses'))

# ---------------------------------------------------------------- a shop's stock
out = lua("local p=player:GetWorldPos() local best,bd for _,e in pairs(System.GetEntitiesInSphere(p,400)) do if e.class=='Stash' then local ok,l=pcall(Shops.IsLinkedWithShop,e.id) "
          "if ok and l and Framework.IsValidWUID(l) and e.inventory:GetCount()>=3 then local q=e:GetWorldPos() local d=math.sqrt((p.x-q.x)^2+(p.y-q.y)^2) if not bd or d<bd then best,bd=e,d end end end end "
          "KCDUS_T_SHOP=best local q=best:GetWorldPos() System.LogAlways(string.format('KCDUS|T|shop|%.1f|%d', bd, best.inventory:GetCount()))")
check('a shop stash (Stash linked with a shop) with stock exists', val(out, 'shop') != '', val(out, 'shop'))
if val(out, 'shop'):
    lua("local q=KCDUS_T_SHOP:GetWorldPos() player:SetWorldPos({x=q.x+6,y=q.y,z=q.z+0.5})", wait=2.0)
    agent('LOOTMODE|1|%s|0' % SC)
    lua("player.inventory:AddItem(ItemManager.CreateItem('5ef63059-322e-4e1b-abe8-926e100c770e',1,500))")
    time.sleep(1.5)
    out = lua("local it for _,h in pairs(KCDUS_T_SHOP.inventory:GetInventoryTable()) do local r=ItemManager.GetItem(h) if r and r.class and r.class~='5ef63059-322e-4e1b-abe8-926e100c770e' then it=r break end end "
              "KCDUS_T_BUY=it KCDUS_T_M0=player.inventory:GetCountOfClass('5ef63059-322e-4e1b-abe8-926e100c770e') KCDUS_T_C0=player.inventory:GetCountOfClass(it.class) "
              "System.LogAlways('KCDUS|T|buy|'..it.class..'|'..KCDUS_T_M0)")
    asks0 = len(log_lines('KCDUS|LOOT|ask|'))
    lua("KCDUS_T_SHOP.inventory:DeleteItemOfClass(KCDUS_T_BUY.class,1) player.inventory:AddItem(ItemManager.CreateItem(KCDUS_T_BUY.class,KCDUS_T_BUY.health or 1,1)) "
        "player.inventory:DeleteItemOfClass('5ef63059-322e-4e1b-abe8-926e100c770e',120)")
    time.sleep(1.5)
    asks = log_lines('KCDUS|LOOT|ask|')[asks0:]
    check('buying from a shop 6 m from its stock is an ask with the stash id', any('|s@' in a for a in asks), asks[-1] if asks else '')
    if asks:
        f = asks[-1].split('|')
        agent('LOOTRES|%s|%s|gone|%s|%s|1' % (SC, f[4], f[5], f[6]))
        out = lua("System.LogAlways('KCDUS|T|refund|'..player.inventory:GetCountOfClass('5ef63059-322e-4e1b-abe8-926e100c770e')..'|'..KCDUS_T_M0..'|'..player.inventory:GetCountOfClass(KCDUS_T_BUY.class)..'|'..KCDUS_T_C0)")
        r = val(out, 'refund').split('|')
        check('... "gone" takes the goods back and gives the price back', len(r) == 4 and r[0] == r[1] and r[2] == r[3], val(out, 'refund'))

# ---------------------------------------------------------------- a quest reward
agent('LOOTMODE|1|%s|0' % SC)
lua("KCDUS_T_R0=player.inventory:GetCountOfClass('%s')" % CLS)
RKEY = '%08x' % (int(time.time()) & 0xffffffff)
agent('QRGIVE|q_live|%s|%s:2:1.0000' % (RKEY, CLS), wait=1.2)
agent('QRGIVE|q_live|%s|%s:2:1.0000' % (RKEY, CLS), wait=1.2)
out = lua("System.LogAlways('KCDUS|T|rew|'..(player.inventory:GetCountOfClass('%s')-KCDUS_T_R0))" % CLS)
check("a host's quest reward is paid to the guest, once", val(out, 'rew') == '2' and any(l.startswith('KCDUS|QRGIVEN|q_live|%s|2|0' % RKEY) for l in log_lines('KCDUS|QRGIVEN|')), val(out, 'rew'))
agent('LOOTMODE|0')

print()
bad = [r for r in results if not r[1]]
print('%d checks, %d failed' % (len(results), len(bad)))
sys.exit(1 if bad else 0)
