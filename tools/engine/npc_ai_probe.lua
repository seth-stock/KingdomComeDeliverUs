-- Read-only first look at the AI targeting API on the NPCs near the player (private engine only; not in the mod pak).
local p = player:GetWorldPos()
local function has(t, k) local ok, v = pcall(function() return t[k] end); return ok and v ~= nil end
local names = { 'GetAttentionTargetEntity', 'GetAttentionTargetOf', 'AddPersonallyHostile', 'RemovePersonallyHostile', 'ResetPersonallyHostiles',
    'IsPersonallyHostile', 'SetAttentiontarget', 'DropTarget', 'Hostile', 'GetFactionOf', 'GetAttentionTargetType', 'GetGroupTarget', 'IsInCombatDanger' }
local s = ''
for _, n in ipairs(names) do s = s .. n .. '=' .. tostring(has(AI, n)) .. ' ' end
System.LogAlways('KCDUS|PROBE|ai-api|' .. s)
local n = 0
for _, e in pairs(System.GetEntitiesInSphere(p, 40)) do
    if e ~= player and KCDUS.Combat.isPerson(e) and e:GetName() ~= '' and not e:IsDead() then
        n = n + 1
        if n <= 8 then
            local q = e:GetWorldPos()
            local d = math.sqrt((p.x - q.x) ^ 2 + (p.y - q.y) ^ 2)
            local ok1, tgt = pcall(AI.GetAttentionTargetEntity, e.id)
            local ok2, hos = pcall(AI.Hostile, e.id, player.id)
            local ok3, fac = pcall(AI.GetFactionOf, e.id)
            local ok4, ph = pcall(AI.IsPersonallyHostile, e.id, player.id)
            local ok5, of = pcall(AI.GetAttentionTargetOf, e.id)
            System.LogAlways(string.format('KCDUS|PROBE|npc|%s|%.1f|tgt=%s|%s|hostile=%s|faction=%s|ph=%s|of=%s|%s', e:GetName(), d,
                tostring(ok1), tostring(tgt and (type(tgt) == 'table' and tgt.GetName and tgt:GetName() or tostring(tgt))), tostring(hos), tostring(fac), tostring(ph), tostring(ok5), tostring(of)))
        end
    end
end
System.LogAlways('KCDUS|PROBE|npcs|' .. n)
