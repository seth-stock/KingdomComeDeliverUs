-- Private engine only: does AI.AddPersonallyHostile make a living NPC target the player, and does the reset calm it? (KCDUS_TP_STEP: 'arm' | 'read' | 'calm')
local step = KCDUS_TP_STEP or 'read'
local name = KCDUS_TP_NAME or 'rat_guard23'
local e = System.GetEntityByName(name)
if not e then System.LogAlways('KCDUS|PROBE|tp|no-such|' .. name) return end
local function tgtName(id)
    local ok, t = pcall(AI.GetAttentionTargetEntity, id)
    if not ok then return 'err:' .. tostring(t) end
    if t == nil then return 'nil' end
    if type(t) == 'table' and t.GetName then return t:GetName() end
    if type(t) == 'userdata' then local te = System.GetEntity(t); return te and te:GetName() or tostring(t) end
    return tostring(t)
end
if step == 'arm' then
    local ok, err = pcall(AI.AddPersonallyHostile, e.id, player.id)
    System.LogAlways('KCDUS|PROBE|tp|arm|' .. tostring(ok) .. '|' .. tostring(err))
elseif step == 'calm' then
    local ok1, e1 = pcall(AI.RemovePersonallyHostile, e.id, player.id)
    local ok2, e2 = pcall(AI.ResetPersonallyHostiles, e.id, false)
    local ok3, e3 = pcall(AI.DropTarget, e.id, player.id)
    System.LogAlways('KCDUS|PROBE|tp|calm|' .. tostring(ok1) .. '|' .. tostring(ok2) .. '|' .. tostring(ok3) .. '|' .. tostring(e1) .. tostring(e2) .. tostring(e3))
elseif step == 'other' then
    local o = System.GetEntityByName(KCDUS_TP_OTHER or 'rat_pirk_guard4')
    local ok1, e1 = pcall(AI.AddPersonallyHostile, e.id, o.id)
    local ok2, e2 = pcall(AI.SetAttentiontarget, e.id, o.id)
    System.LogAlways('KCDUS|PROBE|tp|other|' .. tostring(ok1) .. '|' .. tostring(ok2) .. '|' .. tostring(e1) .. tostring(e2))
end
local p, q = player:GetWorldPos(), e:GetWorldPos()
local ok, ph = pcall(AI.IsPersonallyHostile, e.id, player.id)
local okh, h = pcall(AI.Hostile, e.id, player.id)
local okc, wd = pcall(function() return e.human:IsWeaponDrawn() end)
System.LogAlways(string.format('KCDUS|PROBE|tp|state|%s|d=%.1f|ph=%s|hostile=%s|target=%s|weapon=%s|npcHp=%s|playerHp=%s', name, math.sqrt((p.x - q.x) ^ 2 + (p.y - q.y) ^ 2),
    tostring(ph), tostring(h), tgtName(e.id), tostring(wd), tostring(e.soul:GetState('health')), tostring(player.soul:GetState('health'))))
