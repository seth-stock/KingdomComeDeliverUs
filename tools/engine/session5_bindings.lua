-- Read-only engine capability discovery, run only through the owned private harness.
for _, group in ipairs({'actor','human','inventory','soul'}) do
    local value=player and player[group]
    local mt=value and getmetatable(value)
    local methods=mt and mt.__index
    if type(methods)=='table' then
        for key,fn in pairs(methods) do
            if type(fn)=='function' then System.LogAlways('KCDUS|S5|binding|'..group..'.'..key) end
        end
    end
end
for _, group in ipairs({'EnvironmentModule','XGenAIModule','QuestSystem','ItemManager','AI'}) do
    local value=_G[group]
    if type(value)=='table' then
        for key,fn in pairs(value) do
            if type(fn)=='function' then System.LogAlways('KCDUS|S5|binding|'..group..'.'..key) end
        end
    end
end
