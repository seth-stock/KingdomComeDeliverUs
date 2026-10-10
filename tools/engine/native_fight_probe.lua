-- True combat scheduler test, disposable ordinary NPCs, not a network test.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local p=player:GetWorldPos();local view=player.actor:GetHeadDir();local bodies={}
for i=1,2 do
    local name='kcdus_native_fight_'..i
    if System.GetEntityByName(name) then error('Previous test body exists') end
    local e=System.SpawnEntity({class='NPC',name=name,position={x=p.x+view.x*3.5+(i-1)*1.1,y=p.y+view.y*3.5,z=p.z},
        properties=KCDUS_FIGHT_TEMPLATE and {sharedSoulGuid=KCDUS_FIGHT_TEMPLATE} or nil})
    if not e or not e.human or not e.soul then error('Ordinary NPC creation failed') end
    e:SetFlags(ENTITY_FLAG_NO_SAVE,0);e:DisableBehaviorTreeEvaluation()
    e.actor:MakeLookAsActor(player.id,true)
    local outfit=KCDUS.Outfits and KCDUS.Outfits.capture(player)
    if i==2 and KCDUS_FIGHT_NAKED then e.inventory:RemoveAllItems()
    elseif outfit and not KCDUS.Outfits.apply(e,outfit) then error('Test equipment clone failed') end
    if i==1 and KCDUS_FIGHT_WEAPON then e.actor:EquipWeaponPreset(KCDUS_FIGHT_WEAPON) end
    e:SetAngles({x=0,y=0,z=i==1 and -math.pi/2 or math.pi/2})
    bodies[i]=e
end
local function read(e,command,target)
    local id=target and tostring(target.id):match('([0-9A-Fa-f]+)$') or ''
    local r=e.human:PlayAnim('@kcdus/fight/'..command,id)
    if type(r)~='table' then error('Private native fight entry unavailable') end
    System.LogAlways(string.format('KCDUS|S8|fight|%s|%s|reason=%d|target=%d|match=%d|combat=%d|can=%d|made=%d|submitted=%d|active=%d|slot=%d|mode=%s|prepared=%s|running=%s',
        e:GetName(),command,r.reason,r.target,r.targetMatched,r.active,r.canAttack,r.constructed,r.submitted,r.actionActive,r.actionSlot,tostring(r.mode),tostring(r.prepared),tostring(r.runningSlot)))
    return r
end
local baseline={}
Script.SetTimer(2200,function()
    for i,e in ipairs(bodies) do
        baseline[i]=e.soul:GetState('health')
        e.human:DrawWeapon()
        read(e,'inspect');read(e,'target',bodies[3-i])
    end
    read(bodies[1],'target',bodies[2]);read(bodies[1],'attack')
    for i=1,12 do Script.SetTimer(i*500,function()
        for j,e in ipairs(bodies) do
            local d=e.actor:GetHeadDir();local q=e:GetWorldPos()
            read(e,'inspect')
            System.LogAlways('KCDUS|S8|fight-state|'..j..'|hp='..tostring(e.soul:GetState('health'))..'|clip='..tostring(e.actor:GetCurrentAnimationState())..'|head='..d.x..','..d.y..'|pos='..q.x..','..q.y)
        end
    end) end
    Script.SetTimer(6500,function()
        for i,e in ipairs(bodies) do
            local cleared=read(e,'clear')
            if cleared.reason~=0 or cleared.targetMatched~=1 then error('Native target cleanup unverified; stop the owned process') end
        end
        Script.SetTimer(500,function()
            for _,e in ipairs(bodies) do System.RemoveEntity(e.id) end
            Script.SetTimer(200,function()
                if System.GetEntityByName('kcdus_native_fight_1') or System.GetEntityByName('kcdus_native_fight_2') then error('Body cleanup failed') end
                System.LogAlways('KCDUS|S8|fight-cleanup|PASS')
            end)
        end)
    end)
end)
