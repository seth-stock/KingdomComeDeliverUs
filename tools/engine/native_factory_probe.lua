-- Native action factory proof in an owned disposable profile. Not in the pak.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local p=player:GetWorldPos()
local name='kcdus_action_factory_probe'
if System.GetEntityByName(name) then error('Previous test body still exists') end
local existing=KCDUS_FACTORY_ENTITY and System.GetEntityByName(KCDUS_FACTORY_ENTITY)
local e=existing or System.SpawnEntity({class='NPC',name=name,position={x=p.x+2,y=p.y,z=p.z}})
if not e or not e.human then error('Ordinary NPC spawn failed') end
if not existing then
    e:SetFlags(ENTITY_FLAG_NO_SAVE,0);e:DisableBehaviorTreeEvaluation();e:Event_MakeInvulnerable()
    e.actor:MakeLookAsActor(player.id,true)
end
local hero,heroSoul=player,player.soul:GetId()
local function sample(phase)
    local l=e:GetBonePos('LeftFoot',{});local r=e:GetBonePos('RightFoot',{})
    local lh=e:GetBonePos('LeftForearm',{});local rh=e:GetBonePos('RightForearm',{})
    System.LogAlways('KCDUS|S7|factory|'..phase..'|state='..tostring(e.actor:GetCurrentAnimationState())..'|channel='..tostring(e.actor:GetChannel()))
    local a=e.human:PlayAnim('@kcdus/action/inspect','')
    if type(a)=='table' then System.LogAlways(string.format('KCDUS|S7|action|%s|status=%d|elapsed=%d|refs=%d|reason=%d|active=%d|requested=%d|priority=%d',phase,a.status,a.elapsedMs,a.references,a.reason,a.activeScopes,a.requestedScopes,a.maxPriority)) end
    if l and r then System.LogAlways(string.format('KCDUS|S7|feet|%s|%.5f,%.5f,%.5f',phase,l.x-r.x,l.y-r.y,l.z-r.z)) end
    if lh and rh then System.LogAlways(string.format('KCDUS|S7|arms|%s|%.5f,%.5f,%.5f',phase,lh.x-rh.x,lh.y-rh.y,lh.z-rh.z)) end
end
Script.SetTimer(700,function()
    sample('before')
    local result=e.human:PlayAnim('@kcdus/action/'..(KCDUS_FACTORY_FRAGMENT or 'MotionIdle'),KCDUS_FACTORY_TAGS or '')
    if type(result)~='table' then error('Action factory bridge missing') end
    System.LogAlways(string.format('KCDUS|S7|factory|submit|%d|reason=%d|fragment=%d|refs=%d',result.submitted,result.reason,result.fragment,result.references))
    Script.SetTimer(200,function() sample('200ms') end)
    Script.SetTimer(700,function() sample('700ms') end)
    Script.SetTimer(1400,function()
        sample('1400ms');e.human:PlayAnim('@kcdus/action/release','');if not existing then System.RemoveEntity(e.id) end
        Script.SetTimer(300,function()
            System.LogAlways('KCDUS|S7|factory|cleanup|'..tostring(existing and System.GetEntity(existing.id)==existing or not existing and System.GetEntityByName(name)==nil)..'|hero='..tostring(player==hero and player.soul:GetId()==heroSoul)..'|world='..tostring(KCDUS.inWorldRaw()))
        end)
    end)
end)
