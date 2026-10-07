if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local rows={}
for _,id in pairs(player.inventory:GetInventoryTable()) do
    local it=ItemManager.GetItem(id)
    rows[#rows+1]=it.class..','..string.format('%.5f',it.health)..','..it.amount..','..tostring(it.kcdusEquipped)
end
table.sort(rows)
System.LogAlways('KCDUS|ENGINE|inventory-count|'..#rows)
for _,row in ipairs(rows) do System.LogAlways('KCDUS|ENGINE|inventory-readback|'..row) end
System.LogAlways('KCDUS|ENGINE|inventory-world-time|'..Calendar.GetWorldTime())
