-- Private cloned-world probe, invoked only through engine_session.py ownership guards.
if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
KCDUS.kick()
local selected, distance
local p=player:GetWorldPos()
for _,e in pairs(System.GetEntitiesByClass('NPC')) do
    if e.soul and e:GetName() and not string.find(e:GetName(),'kcdus_',1,true) then
        local hp=e.soul:GetState('health')
        local metadata=ItemManager.GetItem(e.soul:GetId())
        if metadata and metadata.kcdusSoul==1 and hp and hp>10 then
            local q=e:GetWorldPos();local d=(p.x-q.x)^2+(p.y-q.y)^2+(p.z-q.z)^2
            if not distance or d<distance then selected=e;distance=d end
        end
    end
end
if not selected then error('No living native NPC found') end
local id=selected.id;local identity=ItemManager.GetItem(selected.soul:GetId()).kcdusPersistent
local hp=selected.soul:GetState('health');local hits=0
local old=BasicActor and BasicActor.Server and BasicActor.Server.OnHit
if old then BasicActor.Server.OnHit=function(self,hit,...) hits=hits+1;return old(self,hit,...) end end
System.LogAlways('KCDUS|ENGINE|damage-before|'..selected:GetName()..'|'..identity..'|'..hp)
local ok,err=pcall(function() selected.soul:DealDamage(3,0,player.soul:GetId(),true) end)
System.LogAlways('KCDUS|ENGINE|damage-call|'..tostring(ok)..'|'..tostring(err))
Script.SetTimer(1000,function()
    if old then BasicActor.Server.OnHit=old end
    KCDUS.try('damage-readback',function()
        local e=System.GetEntity(id)
        if not e or ItemManager.GetItem(e.soul:GetId()).kcdusPersistent~=identity then error('NPC identity changed') end
        local after=e.soul:GetState('health')
        System.LogAlways('KCDUS|ENGINE|damage-after|'..after..'|lua-OnHit-calls='..hits)
        e.soul:SetState('health',hp)
        System.LogAlways('KCDUS|ENGINE|damage-restored|'..tostring(e.soul:GetState('health')))
    end)
end)
