-- Read inventory item state before/after equipping a private cloned character.
-- Native item logging must be enabled only in the disposable engine harness.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local it
for _,id in pairs(player.inventory:GetInventoryTable()) do
    local value=ItemManager.GetItem(id)
    if value and value.class=='1e7932a3-736a-49c6-baba-8ff7a0a86850' then it=value;break end
end
if not it then error('Reference item missing') end
KCDUS_ENGINE_ITEM=it.id
player.actor:UnequipInventoryItem(it.id)
Script.SetTimer(600,function()
    ItemManager.GetItem(it.id)
    System.LogAlways('KCDUS|ENGINE|equipment-probe|unequipped|'..tostring(it.id))
    player.actor:EquipInventoryItem(it.id)
    Script.SetTimer(600,function()
        ItemManager.GetItem(it.id)
        System.LogAlways('KCDUS|ENGINE|equipment-probe|equipped|'..tostring(it.id))
    end)
end)
