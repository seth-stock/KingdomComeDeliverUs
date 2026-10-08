-- Private cloned-world probe, invoked only through engine_session.py ownership guards.
-- The first damage probe (npc_damage_probe.lua) called soul:DealDamage(3, 0, ...): the signature is
-- soul:DealDamage(stamina, health, attackerId, suppressHitreaction, bodyPart), so that probe dealt 3 STAMINA and 0 HEALTH.
-- This one deals HEALTH damage to one disposable NPC, reads it back, and restores it.
if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
KCDUS.kick()
System.LogAlways('KCDUS|ENGINE|api|Game.PauseGame='..type(Game.PauseGame)
    ..' SetCVar='..type(System.SetCVar)..' GetCVar='..type(System.GetCVar)..' t_scale='..tostring(System.GetCVar and System.GetCVar('t_scale')))
local selected, distance
local p=player:GetWorldPos()
for _,e in pairs(System.GetEntitiesByClass('NPC')) do
    if e.soul and e:GetName() and not string.find(e:GetName(),'kcdus_',1,true) then
        local hp=e.soul:GetState('health')
        if hp and hp>10 then
            local q=e:GetWorldPos();local d=(p.x-q.x)^2+(p.y-q.y)^2+(p.z-q.z)^2
            if not distance or d<distance then selected=e;distance=d end
        end
    end
end
if not selected then error('No living NPC found') end
local id=selected.id
local hp0=selected.soul:GetState('health')
local hits=0
local old=BasicActor and BasicActor.Server and BasicActor.Server.OnHit
if old then BasicActor.Server.OnHit=function(self,hit,...) hits=hits+1;return old(self,hit,...) end end
System.LogAlways('KCDUS|ENGINE|damage2-before|'..selected:GetName()..'|hp='..hp0..'|dist='..math.sqrt(distance))
local ok,err=pcall(function() selected.soul:DealDamage(0,5,player.soul:GetId(),true) end)
System.LogAlways('KCDUS|ENGINE|damage2-call|'..tostring(ok)..'|'..tostring(err))
Script.SetTimer(1000,function()
    if old then BasicActor.Server.OnHit=old end
    KCDUS.try('damage2-readback',function()
        local e=System.GetEntity(id)
        local after=e.soul:GetState('health')
        System.LogAlways('KCDUS|ENGINE|damage2-after|hp='..after..'|delta='..(after-hp0)..'|lua-OnHit-calls='..hits)
        e.soul:SetState('health',hp0)
        System.LogAlways('KCDUS|ENGINE|damage2-restored|'..tostring(e.soul:GetState('health')))
    end)
end)
