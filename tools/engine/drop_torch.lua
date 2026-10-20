-- Private disposable world only: give this Henry a torch (an ordinary weapon item), then drop it at his feet the way the inventory screen does
-- (item leaves the pack, a PickableItem appears beside him). KCDUS|DT|... then the loot watcher should say KCDUS|LOOT|drop and the friend's copy applies it.
local CLASS = '4cea28a0-0814-405a-bf24-4fd711f7eb63'
local function log(...) System.LogAlways('KCDUS|DT|' .. table.concat({ ... }, '|')) end
local id = ItemManager.CreateItem(CLASS, 1, 1)
player.inventory:AddItem(id)
log('added', tostring(id ~= nil), 'lootEnabled', tostring(KCDUS.Loot and KCDUS.Loot.enabled))
Script.SetTimer(3000, function()
    local p = player:GetWorldPos()
    player.inventory:DeleteItemOfClass(CLASS, 1)
    local ok, ent = pcall(System.SpawnEntity, { class = 'PickableItem', name = 'dt_torch_' .. math.random(1000, 9999),
        position = { x = p.x + 0.6, y = p.y, z = p.z + 0.3 }, properties = { guidItemClassId = CLASS, nAmount = 1, fHealth = 1 } })
    log('dropped', tostring(ok), tostring(ent and ent.id))
end)
