-- True combat scheduler test, disposable ordinary NPCs, not a network test.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local attackDelay=KCDUS_FIGHT_ATTACK_DELAY or 0
if type(attackDelay)~='number' or attackDelay~=math.floor(attackDelay) or attackDelay<0 or attackDelay>3000 then
    error('Private attack delay must be an integer from 0 to 3000 ms')
end
local p=player:GetWorldPos();local view=player.actor:GetHeadDir();local bodies={}
for i=1,2 do
    local name='kcdus_native_fight_'..i
    if System.GetEntityByName(name) then error('Previous test body exists') end
    local spawn={x=p.x+view.x*3.5+(i-1)*1.1,y=p.y+view.y*3.5,z=p.z}
    if KCDUS_FIGHT_TERRAIN_SPAWN then
        local height=System.GetTerrainElevation(spawn)
        if type(height)~='number' or height~=height then error('Terrain readback unavailable') end
        spawn.z=height+0.2
    end
    local e=System.SpawnEntity({class='NPC',name=name,position=spawn,
        properties=KCDUS_FIGHT_TEMPLATE and {sharedSoulGuid=KCDUS_FIGHT_TEMPLATE} or nil})
    if not e or not e.human or not e.soul then error('Ordinary NPC creation failed') end
    e:SetFlags(ENTITY_FLAG_NO_SAVE,0);e:DisableBehaviorTreeEvaluation()
    e.actor:MakeLookAsActor(player.id,true)
    local outfit=KCDUS.Outfits and KCDUS.Outfits.capture(player)
    if i==2 and KCDUS_FIGHT_NAKED then e.inventory:RemoveAllItems()
    elseif KCDUS_FIGHT_COPY_OUTFIT~=false and outfit and not KCDUS.Outfits.apply(e,outfit) then error('Test equipment clone failed') end
    if i==1 and KCDUS_FIGHT_WEAPON then e.actor:EquipWeaponPreset(KCDUS_FIGHT_WEAPON) end
    e:SetAngles({x=0,y=0,z=i==1 and -math.pi/2 or math.pi/2})
    bodies[i]=e
end
local function read(e,command,target)
    local id=target and tostring(target.id):match('([0-9A-Fa-f]+)$') or ''
    local r=e.human:PlayAnim('@kcdus/fight/'..command,id)
    if type(r)~='table' then error('Private native fight entry unavailable') end
    System.LogAlways(string.format('KCDUS|S9|fight|%s|%s|reason=%d|target=%d|match=%d|combat=%d|can=%d|made=%d|submitted=%d|active=%d|slot=%d|mode=%s|prepared=%s|running=%s|requestedMatch=%s|requestedPresent=%s|guard=%s',
        e:GetName(),command,r.reason,r.target,r.targetMatched,r.active,r.canAttack,r.constructed,r.submitted,r.actionActive,r.actionSlot,tostring(r.mode),tostring(r.prepared),tostring(r.runningSlot),tostring(r.requestedMatched),tostring(r.requestedPresent),tostring(r.guardRequested)))
    return r
end
local baseline={}
local finished=false
Script.SetTimer(2200,function()
    for i,e in ipairs(bodies) do
        baseline[i]=e.soul:GetState('health')
        e.human:DrawWeapon()
        read(e,'inspect');read(e,'target',bodies[3-i])
        if KCDUS_FIGHT_HOLD_GUARD then
            local held=read(e,'guard')
            if held.reason~=0 or held.guardRequested~=1 then error('Native guard request unverified; stop the owned process') end
        end
    end
    local function attack()
        read(bodies[1],'target',bodies[2]);read(bodies[1],'attack')
    end
    if attackDelay==0 then attack() else Script.SetTimer(attackDelay,attack) end
    if KCDUS_FIGHT_REPEAT_TARGET then
        local function refresh()
            if finished then return end
            read(bodies[1],'target',bodies[2])
            Script.SetTimer(150,refresh)
        end
        Script.SetTimer(150,refresh)
    end
    for i=1,12 do Script.SetTimer(i*500,function()
        for j,e in ipairs(bodies) do
            local d=e.actor:GetHeadDir();local q=e:GetWorldPos()
            read(e,'inspect')
            System.LogAlways('KCDUS|S9|fight-state|'..j..'|hp='..tostring(e.soul:GetState('health'))..'|stamina='..tostring(e.soul:GetState('stamina'))..'|weapon='..tostring(e.human:IsWeaponDrawn())..'|clip='..tostring(e.actor:GetCurrentAnimationState())..'|head='..d.x..','..d.y..'|pos='..q.x..','..q.y)
        end
    end) end
    Script.SetTimer(6500,function()
        finished=true
        for i,e in ipairs(bodies) do
            if KCDUS_FIGHT_HOLD_GUARD then
                local released=read(e,'unguard')
                if released.reason~=0 or released.guardRequested~=0 then error('Native guard release unverified; stop the owned process') end
            end
            local cleared=read(e,'clear')
            if cleared.reason~=0 or cleared.targetMatched~=1 or cleared.requestedMatched~=1 or cleared.requestedPresent~=0 then error('Native target cleanup unverified; stop the owned process') end
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
