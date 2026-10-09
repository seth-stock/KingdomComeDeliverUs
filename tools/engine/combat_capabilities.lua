-- Read-only combat discovery in an owned disposable session. Not packaged.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
for name,value in pairs(_G) do
    if type(name)=='string' and type(value)=='table' and
        (string.find(string.lower(name),'combat',1,true) or string.find(string.lower(name),'skirmish',1,true)) then
        for key,fn in pairs(value) do
            if type(fn)=='function' then System.LogAlways('KCDUS|S6|binding|'..name..'.'..key) end
        end
    end
end
for _,group in ipairs({'human','actor','soul'}) do
    local value=player[group];local mt=value and getmetatable(value)
    for key,fn in pairs(mt and mt.__index or {}) do
        if type(fn)=='function' and (string.find(string.lower(key),'combat',1,true) or
            string.find(string.lower(key),'attack',1,true) or string.find(string.lower(key),'weapon',1,true) or
            string.find(string.lower(key),'block',1,true)) then
            System.LogAlways('KCDUS|S6|binding|'..group..'.'..key)
        end
    end
end
