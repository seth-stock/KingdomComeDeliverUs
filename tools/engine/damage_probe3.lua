-- Private cloned-world probe (engine_session.py guards). Lethal health damage to ONE disposable NPC 15-80 m away, then what death looks like to Lua.
if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
local function L(s) System.LogAlways('KCDUS|ENGINE|'..s) end
local p=player:GetWorldPos()
local target, best
for _,e in pairs(System.GetEntitiesByClass('NPC')) do
    local ok=pcall(function()
        if e.soul and e:GetName() and not string.find(e:GetName(),'kcdus_',1,true) then
            local hp=e.soul:GetState('health')
            local q=e:GetWorldPos();local d=math.sqrt((p.x-q.x)^2+(p.y-q.y)^2)
            if hp and hp>20 and d>15 and d<80 and (not best or d<best) then target=e;best=d end
        end
    end)
end
if not target then error('no NPC 15-80 m away') end
local id=target.id
L('kill-target|'..target:GetName()..'|d='..best)
local function state(tag)
    local e=System.GetEntity(id)
    local parts={}
    local function add(k,f) local ok,v=pcall(f); parts[#parts+1]=k..'='..tostring(ok and v or ('ERR')) end
    add('health',function() return e.soul:GetState('health') end)
    add('actorDead',function() return e.actor:IsDead() end)
    add('soulDead',function() return e.soul:IsDead() end)
    add('isDead',function() return e:IsDead() end)
    add('invCount',function() local n=0 for _ in pairs(e.inventory:GetInventoryTable() or {}) do n=n+1 end return n end)
    add('hidden',function() return e:IsHidden() end)
    L('kill-'..tag..'|'..table.concat(parts,' '))
end
state('before')
local ok,err=pcall(function() target.soul:DealDamage(0,100000,nil,true) end)
L('kill-call|'..tostring(ok)..'|'..tostring(err))
Script.SetTimer(1500,function() KCDUS.try('kill-read1',function() state('after1.5s') end) end)
Script.SetTimer(6000,function() KCDUS.try('kill-read2',function() state('after6s') end) end)
