for k, v in pairs(ItemManager) do
    if type(v) == 'function' then System.LogAlways('KCDUS|ENGINE|itemapi|' .. k) end
end
local n = 0
for _, id in pairs(player.inventory:GetInventoryTable()) do
    n = n + 1
    if n <= 3 then
        for k, v in pairs(ItemManager.GetItem(id) or {}) do
            System.LogAlways('KCDUS|ENGINE|itemfield|' .. tostring(k) .. '=' .. tostring(v))
        end
    end
end
