-- Private engine only, read-only. The Shops API, the shopkeepers near the player and what their shop inventory looks like.
local s = ''
for k, v in pairs(Shops) do s = s .. k .. ',' end
System.LogAlways('KCDUS|PROBE|shop|api|' .. s)
local p = player:GetWorldPos()
local n = 0
for _, e in pairs(System.GetEntitiesInSphere(p, tonumber(KCDUS_SP_R) or 600)) do
    local okc, c = pcall(function() return e.class end)
    if okc and (c == 'NPC' or c == 'NPC_Female' or c == 'Stash') then
        local ok1, db = pcall(Shops.GetShopDBIdByKeeper, e.id)
        local ok2, link = pcall(Shops.IsLinkedWithShop, e.id)
        local linked = ok2 and link ~= nil and Framework.IsValidWUID(link)
        if (ok1 and db and db >= 0) or linked then
            n = n + 1
            if n <= 12 then
                local q = e:GetWorldPos()
                local okm, money = pcall(Shops.GetShopMoney, e.id)
                local inv = e.inventory and e.inventory:GetCount() or -1
                System.LogAlways(string.format('KCDUS|PROBE|shop|%s|%s|%.0f,%.0f,%.0f|d=%.0f|db=%s|linked=%s|money=%s|inv=%s', c, tostring(e:GetName()), q.x, q.y, q.z,
                    math.sqrt((p.x - q.x) ^ 2 + (p.y - q.y) ^ 2), tostring(db), tostring(linked and tostring(link)), tostring(okm and money), tostring(inv)))
            end
        end
    end
end
System.LogAlways('KCDUS|PROBE|shop|count|' .. n)
