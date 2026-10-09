-- Private engine only. What is an item on the ground (PickableItem), and can a script drop / remove / create one? KCDUS_GP_STEP: 'scan' | 'drop' | 'fields' | 'remove' | 'spawn'
local step = KCDUS_GP_STEP or 'scan'
local p = player:GetWorldPos()
local function items(r)
    local out = {}
    for _, e in pairs(System.GetEntitiesInSphere(p, r or 30)) do
        local ok, c = pcall(function() return e.class end)
        if ok and c == 'PickableItem' then out[#out + 1] = e end
    end
    return out
end
if step == 'scan' then
    local l = items(30)
    System.LogAlways('KCDUS|PROBE|gp|scan|' .. #l)
    for i, e in ipairs(l) do
        if i > 6 then break end
        local q = e:GetWorldPos()
        local ok, it = pcall(function() return e.item end)
        local keys = ''
        for k, v in pairs(e) do if type(v) ~= 'function' and type(k) == 'string' then keys = keys .. k .. ',' end end
        System.LogAlways(string.format('KCDUS|PROBE|gp|pi|%s|%s|%.1f,%.1f,%.1f|item=%s|keys=%s', tostring(e:GetName()), tostring(e.id), q.x, q.y, q.z, tostring(ok and it), keys))
    end
elseif step == 'drop' then
    -- the first stackable, unequipped, cheap thing Henry carries
    local t = player.inventory:GetInventoryTable()
    local pick
    for _, h in pairs(t) do
        local it = ItemManager.GetItem(h)
        if it and it.kcdusEquipped ~= 1 then pick = it break end
    end
    KCDUS_GP_ITEM = pick
    local before = player.inventory:GetCountOfClass(pick.class)
    local okA, eA = pcall(function() return player.actor:DropItem(pick.id) end)
    local okB, eB
    if not okA then okB, eB = pcall(function() return player:DropItem(pick.id) end) end
    System.LogAlways('KCDUS|PROBE|gp|drop|class=' .. tostring(pick.class) .. ' before=' .. before .. ' actor=' .. tostring(okA) .. '/' .. tostring(eA) .. ' ent=' .. tostring(okB) .. '/' .. tostring(eB) .. ' after=' .. player.inventory:GetCountOfClass(pick.class))
elseif step == 'fields' then
    for _, e in ipairs(items(6)) do
        local s = ''
        local mt = getmetatable(e)
        for k, v in pairs(e) do if type(k) == 'string' then s = s .. k .. ':' .. type(v) .. ',' end end
        local okI, iid = pcall(function() return e.GetItemId and e:GetItemId() end)
        local okP, props = pcall(function() return e.Properties end)
        local ps = ''
        if okP and type(props) == 'table' then for k, v in pairs(props) do ps = ps .. k .. '=' .. tostring(v) .. ',' end end
        System.LogAlways('KCDUS|PROBE|gp|fields|' .. tostring(e:GetName()) .. '|' .. s .. '|getItemId=' .. tostring(okI) .. ':' .. tostring(iid) .. '|props=' .. ps)
        local okG, it = pcall(function() return ItemManager.GetItem(e.itemId or (e.GetItemId and e:GetItemId())) end)
        if okG and type(it) == 'table' then System.LogAlways('KCDUS|PROBE|gp|item|' .. tostring(it.class) .. '|' .. tostring(it.amount) .. '|' .. tostring(it.health)) end
    end
elseif step == 'remove' then
    for _, e in ipairs(items(6)) do
        local ok, err = pcall(System.RemoveEntity, e.id)
        System.LogAlways('KCDUS|PROBE|gp|remove|' .. tostring(e:GetName()) .. '|' .. tostring(ok) .. '|' .. tostring(err))
        break
    end
    System.LogAlways('KCDUS|PROBE|gp|left|' .. #items(6))
end
