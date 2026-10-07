if not KCDUS.inWorldRaw() then error('Loaded private world required') end
System.LogAlways('KCDUS|ENGINE|inventory-id|'..tostring(player.inventory:GetId()))
local n=0
for key,id in pairs(player.inventory:GetInventoryTable()) do
    n=n+1
    if n<=4 then
        local item=ItemManager.GetItem(id)
        System.LogAlways('KCDUS|ENGINE|inventory-sample|'..tostring(key)..'|'..tostring(id)..'|'..tostring(item.class)..'|'..tostring(ItemManager.GetItemOwner(id)))
    end
end
System.LogAlways('KCDUS|ENGINE|inventory-count|'..n)
System.LogAlways('KCDUS|ENGINE|hand0|'..tostring(player.human:GetItemInHand(0)))
System.LogAlways('KCDUS|ENGINE|hand1|'..tostring(player.human:GetItemInHand(1)))
