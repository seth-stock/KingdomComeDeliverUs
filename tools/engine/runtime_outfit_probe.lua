if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local K=KCDUS
K.kick() -- normal agent records re-arm the loop after the engine drops load timers
local source=K.Outfits.capture(player)
if not source then error('Native equipment capture unavailable') end
local p=player:GetWorldPos();local d=player.actor:GetHeadDir()
local position=(p.x+d.x*2.5)..','..(p.y+d.y*2.5)..','..p.z
K.handlers.OUT({'OUT','254',source})
local function state(speed)
    K.handlers.P({'P','254','Outfit probe',position,'0','0,'..speed..',0','0','100','100','MotionMovement'})
end
state(1.5)
for i=1,15 do Script.SetTimer(i*100,function() state(1.5) end) end
Script.SetTimer(1250,function()
    K.try('engine:runtime-outfit',function()
        local record=K.Ghosts.list['254'];local ent=System.GetEntity(record.id)
        K.out('ENGINE','runtime-outfit-match',K.Outfits.capture(ent)==source)
        K.out('ENGINE','runtime-outfit-clip',record.clip)
        KCDUS_ENGINE_OUTFIT_GHOST=ent
    end)
end)
