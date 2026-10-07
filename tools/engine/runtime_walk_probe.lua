if not KCDUS.inWorldRaw() then error('Loaded private world required') end
KCDUS.kick()
local pos=player:GetWorldPos()
local function update(speed)
    KCDUS.handlers.P({'P','probe_walk','Probe',pos.x..','..pos.y..','..pos.z,'0','0,'..speed..',0','0','100','100','MotionMovement'})
end
local function pose(phase)
    local record=KCDUS.Ghosts.list.probe_walk
    local ent=record and System.GetEntity(record.id)
    if not ent then error('Runtime ghost absent') end
    local l=ent:GetBonePos('LeftFoot',{});local r=ent:GetBonePos('RightFoot',{})
    System.LogAlways('KCDUS|ENGINE|runtime-clip|'..phase..'|'..tostring(record.clip))
    if l and r then System.LogAlways('KCDUS|ENGINE|runtime-feet|'..phase..'|'..(l.x-r.x)..','..(l.y-r.y)..','..(l.z-r.z)) end
end
update(1.5)
for i=1,12 do Script.SetTimer(i*100,function() update(1.5) end) end
Script.SetTimer(600,function() pose('600ms') end)
Script.SetTimer(900,function() pose('900ms') end)
Script.SetTimer(1800,function() pose('idle') end)
Script.SetTimer(2200,function()
    KCDUS.try('engine:walk-remove',function()
    local record=KCDUS.Ghosts.list.probe_walk;local id=record and record.id
    KCDUS.handlers.PD({'PD','probe_walk'})
    Script.SetTimer(300,function()
        KCDUS.try('engine:walk-removed-readback',function()
            System.LogAlways('KCDUS|ENGINE|runtime-removed|'..tostring(id and System.GetEntity(id))..'|'..tostring(KCDUS.inWorldRaw()))
        end)
    end)
    end)
end)
