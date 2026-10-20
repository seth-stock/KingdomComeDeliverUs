-- What the engine reports for Henry's own animation while he draws, swings and blocks (private disposable world only). KCDUS|AP|t|state|flags
local K = KCDUS
local function log(...) System.LogAlways('KCDUS|AP|' .. table.concat({ ... }, '|')) end
local function st()
    local ok, s = pcall(function() return player.actor:GetCurrentAnimationState() end)
    local weapon = pcall(function() return player.human:IsWeaponDrawn() end) and player.human:IsWeaponDrawn() or false
    local mounted = pcall(function() return player.human:IsMounted() end) and player.human:IsMounted() or false
    return tostring(s) .. '|weapon=' .. tostring(weapon) .. '|mounted=' .. tostring(mounted)
end
local steps = {
    { 0.0, function() log('start', st()) end },
    { 0.5, function() pcall(function() player.human:DrawWeapon() end) log('DrawWeapon called') end },
    { 2.0, function() log('after draw', st()) end },
    { 2.2, function() local ok, e = pcall(function() player.actor:SimulateOnAction('attack', 'press', 1) end) log('SimulateOnAction attack', tostring(ok), tostring(e)) end },
    { 2.4, function() log('attack+0.2', st()) end },
    { 2.7, function() log('attack+0.5', st()) end },
    { 3.0, function() log('attack+0.8', st()) end },
    { 3.5, function() log('attack+1.3', st()) end },
    { 4.0, function() local ok, e = pcall(function() player.actor:SimulateOnAction('block', 'press', 1) end) log('SimulateOnAction block', tostring(ok), tostring(e)) end },
    { 4.4, function() log('block+0.4', st()) end },
    { 4.6, function() pcall(function() player.actor:SimulateOnAction('block', 'release', 0) end) end },
    { 5.5, function() log('after block', st()) end },
    { 6.0, function() pcall(function() player.human:HolsterWeapon() end) log('HolsterWeapon called') end },
    { 7.5, function() log('after holster', st()) end },
}
for _, s in ipairs(steps) do
    Script.SetTimer(math.floor(s[1] * 1000) + 10, s[2])
end
