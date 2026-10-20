-- Prints the names of the script API tables a ghost, a horse and cutscene code could use (private disposable world only): KCDUS|API|<table>|name,name,...
local function names(t)
    local out = {}
    if type(t) ~= 'table' then return out end
    for k, v in pairs(t) do if type(k) == 'string' then out[#out + 1] = k .. (type(v) == 'function' and '()' or '') end end
    local mt = getmetatable(t)
    if mt and type(mt.__index) == 'table' then for k, v in pairs(mt.__index) do if type(k) == 'string' then out[#out + 1] = k .. (type(v) == 'function' and '()' or '') end end end
    table.sort(out)
    return out
end
local function dump(label, t)
    local n = names(t)
    local line = ''
    for _, k in ipairs(n) do
        if #line + #k > 400 then System.LogAlways('KCDUS|API|' .. label .. '|' .. line); line = '' end
        line = line .. k .. ','
    end
    System.LogAlways('KCDUS|API|' .. label .. '|' .. line)
end
dump('player.actor', player.actor)
dump('player.human', player.human)
dump('player.soul', player.soul)
dump('player.inventory', player.inventory)
dump('Game', Game)
dump('AI', AI)
dump('System', System)
dump('QuestSystem', QuestSystem)
dump('Cutscene', rawget(_G, 'Cutscene') or rawget(_G, 'CutScene') or rawget(_G, 'Movie') or {})
dump('Movie', rawget(_G, 'Movie') or {})
dump('CryAction', CryAction)
dump('XGenAIModule', rawget(_G, 'XGenAIModule') or {})
