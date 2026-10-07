if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local ok,result=pcall(function() return player.inventory:Dump() end)
System.LogAlways('KCDUS|ENGINE|inventory-dump|'..tostring(ok)..'|'..tostring(result))
for _,key in ipairs({'__inventoryInfo','__bodyInfo','__itemInfo'}) do
    System.LogAlways('KCDUS|ENGINE|global-info|'..key..'|'..tostring(_G[key]))
end
