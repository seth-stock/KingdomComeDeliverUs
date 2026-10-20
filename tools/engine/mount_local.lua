-- Put THIS game's Henry on a horse near him (private disposable world only), then ride it in a line. KCDUS|ML|...
local function log(...) System.LogAlways('KCDUS|ML|' .. table.concat({ ... }, '|')) end
local p = player:GetWorldPos()
local horse
for _, e in pairs(System.GetEntities() or {}) do
    if e.class == 'Horse' and e.soul and not e:IsDead() then
        local q = e:GetWorldPos()
        local d = math.sqrt((q.x - p.x) ^ 2 + (q.y - p.y) ^ 2)
        if d < 300 and (not horse or d < horse.d) then horse = { e = e, d = d } end
    end
end
if not horse then log('no horse'); return end
local h = horse.e
local hp = h:GetWorldPos()
log('horse', h:GetName(), string.format('%.1f m away', horse.d))
player:SetWorldPos({ x = hp.x + 1.2, y = hp.y, z = hp.z })
Script.SetTimer(800, function() local ok, e = pcall(function() return player.human:ForceMount(h.id) end) log('ForceMount', tostring(ok), tostring(e)) end)
Script.SetTimer(2500, function() log('mounted', tostring(player.human:IsMounted()), tostring(player.human:GetHorse() and player.human:GetHorse():GetName())) end)
-- ride: move the horse 1 m every 150 ms along +x for 6 s
for i = 1, 40 do
    Script.SetTimer(3000 + i * 150, function() local q = h:GetWorldPos() h:SetWorldPos({ x = q.x + 1.0, y = q.y, z = q.z }) end)
end
