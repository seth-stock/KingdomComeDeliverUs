-- Four disposable bodies in a row in front of the camera, each dressed a different way (private disposable world only). KCDUS|GV|...
local K = KCDUS
local function log(...) System.LogAlways('KCDUS|GV|' .. table.concat({ ... }, '|')) end
for _, id in ipairs(KCDUS_GV or {}) do pcall(System.RemoveEntity, id) end
KCDUS_GV = {}
local cam, dir = System.GetViewCameraPos(), System.GetViewCameraDir()
local right = { x = dir.y, y = -dir.x, z = 0 }
local function spawn(n, offset)
    local pos = { x = cam.x + dir.x * 4.5 + right.x * offset, y = cam.y + dir.y * 4.5 + right.y * offset, z = player:GetWorldPos().z }
    local ent = System.SpawnEntity({ class = 'NPC', name = 'kcdus_gv_' .. n, position = pos })
    KCDUS_GV[#KCDUS_GV + 1] = ent.id
    pcall(function() ent:Event_MakeInvulnerable() end)
    pcall(function() ent:DisableBehaviorTreeEvaluation() end)
    pcall(function() AI.SetBehaviorTreeEvaluationEnabled(ent.id, false) end)
    pcall(function() ent:SetWorldAngles({ x = 0, y = 0, z = math.atan2(-dir.x, dir.y) + math.pi }) end)
    return ent
end
local a, b, c, d = spawn(1, -4.5), spawn(2, -1.5), spawn(3, 1.5), spawn(4, 4.5)
log('baseline: the current ghost recipe (clothing preset only)')
pcall(function() a.actor:EquipClothingPreset(K.Ghosts.CLOTHES) end)
log('b: MakeLookAsActor(Henry)', tostring(pcall(function() b.actor:MakeLookAsActor(player.id, true) end)))
log('c: MakeLookAsSoul(Henry soul)', tostring(pcall(function() c.actor:MakeLookAsSoul(player.soul:GetId(), true) end)))
log('d: both then the preset', tostring(pcall(function() d.actor:MakeLookAsActor(player.id, true) d.actor:EquipClothingPreset(K.Ghosts.CLOTHES) end)))
log('placed', tostring(cam.x), tostring(cam.y))
