-- Read-only enumeration from the loaded retail engine, one binding per log record.
for _, group in ipairs({ 'actor', 'human', 'inventory' }) do
    local value = player[group]
    local mt = value and getmetatable(value)
    local methods = mt and mt.__index
    if type(methods) == 'table' then
        for key, fn in pairs(methods) do
            if type(fn) == 'function' then System.LogAlways('KCDUS|ENGINE|binding|' .. group .. '.' .. key) end
        end
    end
end
