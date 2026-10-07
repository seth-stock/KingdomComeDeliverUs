if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local source=KCDUS.Outfits.capture(player)
if not source then error('Native equipment capture unavailable') end
local pos=player:GetWorldPos();local dir=player.actor:GetHeadDir()
local g=System.SpawnEntity({class='NPC',name='kcdus_outfit_probe',position={x=pos.x+dir.x*2.5,y=pos.y+dir.y*2.5,z=pos.z}})
g:DisableBehaviorTreeEvaluation();g:Event_MakeInvulnerable();g:SetFlags(ENTITY_FLAG_NO_SAVE,0)
g.actor:EquipClothingPreset(KCDUS.Ghosts.CLOTHES)
KCDUS_ENGINE_OUTFIT_GHOST=g
Script.SetTimer(800,function()
    KCDUS.try('engine:outfit',function()
        System.LogAlways('KCDUS|ENGINE|outfit-before|'..tostring(KCDUS.Outfits.capture(g)))
        local ok=KCDUS.Outfits.apply(g,source)
        System.LogAlways('KCDUS|ENGINE|outfit-apply|'..tostring(ok))
        System.LogAlways('KCDUS|ENGINE|outfit-matches|'..tostring(KCDUS.Outfits.capture(g)==source))
        Script.SetTimer(1200,function()
            System.LogAlways('KCDUS|ENGINE|outfit-after-matches|'..tostring(KCDUS.Outfits.capture(g)==source))
        end)
    end)
end)
