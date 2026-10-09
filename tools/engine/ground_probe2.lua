-- Private engine only. Spawn an item on the ground, pick it up by script, remove one. KCDUS_G2_STEP: 'spawn' | 'scan' | 'pick' | 'remove'
local step = KCDUS_G2_STEP or 'scan'
local CLASS = KCDUS_G2_CLASS or '1e7932a3-736a-49c6-baba-8ff7a0a86850'
local p = player:GetWorldPos()
local function mine()
    local out = {}
    for _, e in pairs(System.GetEntitiesInSphere(p, 4)) do
        if e.class == 'PickableItem' and not e.npcOnly then out[#out + 1] = e end
    end
    return out
end
local function describe(e)
    local okI, iid = pcall(function() return e.item:GetId() end)
    local it = okI and ItemManager.GetItem(iid)
    local q = e:GetWorldPos()
    return string.format('%s|%.2f,%.2f,%.2f|id=%s|class=%s|amount=%s|hp=%s|shop=%s|dead=%s|pick=%s|hidden=%s', tostring(e:GetName()), q.x, q.y, q.z, tostring(iid),
        tostring(it and it.class), tostring(it and it.amount), tostring(it and it.health), tostring(e.item:IsFromShop()), tostring(e.item:BelongsToDeadBody()),
        tostring(e.item:CanPickUp(player.id)), tostring(e:IsHidden()))
end
if step == 'spawn' then
    local ok, ent = pcall(System.SpawnEntity, { class = 'PickableItem', name = 'kcdus_probe_item', position = { x = p.x + 1.0, y = p.y, z = p.z + 0.3 },
        properties = { guidItemClassId = CLASS, fHealth = 1, nAmount = 1, bOnlyNPC = 0 } })
    System.LogAlways('KCDUS|PROBE|g2|spawn|' .. tostring(ok) .. '|' .. tostring(ent and ent.id))
elseif step == 'scan' then
    for _, e in ipairs(mine()) do System.LogAlways('KCDUS|PROBE|g2|item|' .. describe(e)) end
elseif step == 'pick' then
    local e = mine()[1]
    local before = player.inventory:GetCountOfClass(CLASS)
    local ok, r = pcall(function() return e.item:OnUsed(player.id) end)
    System.LogAlways('KCDUS|PROBE|g2|pick|' .. tostring(ok) .. '|' .. tostring(r) .. '|before=' .. before .. '|after=' .. player.inventory:GetCountOfClass(CLASS))
elseif step == 'remove' then
    local e = mine()[1]
    local ok, r = pcall(System.RemoveEntity, e.id)
    System.LogAlways('KCDUS|PROBE|g2|remove|' .. tostring(ok) .. '|' .. tostring(r) .. '|left=' .. #mine())
end
