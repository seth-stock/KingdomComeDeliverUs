-- npcs: shared enemies (docs/CAPABILITIES.md, "Shared enemies"). Where an NPC walks and whom it fights is decided by ONE game: its owner.
--   * The HOST's agent gives every NPC near the players to the player nearest to it (with hysteresis, docs/CAPABILITIES.md). That player's game runs the NPC
--     as usual: its brain walks it, notices, picks a fight with that player. This file reports where its owned NPCs are (NPCST, five times a second).
--   * Every OTHER game makes its copy of that NPC a puppet: the brain is switched off (DisableBehaviorTreeEvaluation, proved in the private engine: the NPC
--     stands still and can be placed) and the body follows the owner's samples, with the walk/run animation of the presence bodies and the weapon drawn when
--     the owner's is. So in every world the same bandit chases the same player. When the NPC comes nearest to ME it becomes mine: the brain is switched back on
--     and it fights me. Health and death are shared separately (combat.lua).
--   * A puppet is never kept without news: no sample for 4 s, the owner gone, the mode off, a loaded world or a save (menu.lua) gives the brain back at once.
-- NOT shown on a puppet: its individual sword swings (it walks, faces and holds its weapon as the owner's copy does).
--
-- agent -> game:  NPCMODE|1|<scope>|<my id>   on for this shared world;  NPCMODE|0  off (every puppet gets its brain back)
--                 NPCOWN|<scope>|name:owner;name:owner...      the host's decision (an entry lives 3 s unless repeated)
--                 NPCSET|<scope>|<from>|name,x,y,z,yaw,vx,vy,w;...  the owner's samples of its NPCs
-- game -> agent:  KCDUS|NPCNEAR|name:dist;...                  the living NPCs near this player (once a second, nearest 24)
--                 KCDUS|NPCST|name,x,y,z,yaw,vx,vy,w;...       the NPCs this game owns (5 a second, 12 a line)
--                 KCDUS|NPCPUP|<name>|<on|off>|<why>           a puppet taken or given back (readback)
local K = KCDUS
System.LogAlways("KCDUS|LOAD|npcs")
local N = { enabled = false, scope = '', me = '', own = {}, puppets = {}, last = {}, reach = 40, maxNear = 24, ownTtl = 3, staleS = 4, snapM = 6, taken = 0, released = 0 }
K.Npcs = N

local function validName(n) return type(n) == 'string' and #n >= 3 and #n <= 80 and not string.find(n, '[^%w_%.%-]') and not string.find(n, 'kcdus_', 1, true) end
local function isDead(e) local ok, v = pcall(function() return e:IsDead() end); return ok and v == true end
local function nameOf(e) local ok, n = pcall(function() return e:GetName() end); return ok and n or '' end
local function living(e)
    if e == player or not e.soul then return false end
    local ok, c = pcall(function() return e.class end)
    if not ok or type(c) ~= 'string' or string.sub(c, 1, 3) ~= 'NPC' then return false end
    return not isDead(e) and validName(nameOf(e))
end
local function f2(v) return string.format('%.2f', v) end

-- ---------------------------------------------------------------- the puppets
local function brain(e, on)
    return pcall(function() if on then e:EnableBehaviorTreeEvaluation() else e:DisableBehaviorTreeEvaluation() end end)
end

function N.release(name, why)
    local p = N.puppets[name]
    if not p then return end
    N.puppets[name] = nil
    local e = System.GetEntityByName(name)
    if e then
        pcall(function() e:StopAnimation(0, K.Locomotion and K.Locomotion.layer or 0) end)
        brain(e, true)
    end
    N.released = N.released + 1
    K.out('NPCPUP', name, 'off', why or '')
end

function N.releaseAll(why) for name in pairs(N.puppets) do N.release(name, why) end end

local function take(name, e, now)
    if not brain(e, false) then return nil end
    local p = { at = now, g = { key = name, flags = 0 } }
    N.puppets[name] = p
    N.taken = N.taken + 1
    K.out('NPCPUP', name, 'on', N.own[name] and N.own[name].owner or '')
    return p
end

local function steer(name, p, now)
    local e = System.GetEntityByName(name)
    if not e or isDead(e) then N.release(name, 'dead'); return end
    if now - p.at > N.staleS then N.release(name, 'stale'); return end
    local dt = math.min(0.5, now - p.at)
    local tx, ty, tz = p.x + p.vx * dt, p.y + p.vy * dt, p.z
    local q = e:GetWorldPos()
    local dx, dy = tx - q.x, ty - q.y
    local d = math.sqrt(dx * dx + dy * dy)
    if d > N.snapM then
        e:SetWorldPos({ x = tx, y = ty, z = tz })
    elseif d > 0.05 then
        local k = math.min(1, 0.5)
        e:SetWorldPos({ x = q.x + dx * k, y = q.y + dy * k, z = q.z + (tz - q.z) * k })
    end
    pcall(function() e:SetWorldAngles({ x = 0, y = 0, z = p.yaw }) end)
    p.g.vx, p.g.vy, p.g.stamp = p.vx, p.vy, p.at
    if K.Locomotion then pcall(K.Locomotion.drive, e, p.g, now) end
    -- the weapon as the owner's copy holds it; a draw can be refused mid-animation, so it is tried again every 1.5 s
    if now >= (p.wTry or 0) then
        local okW, drawn = pcall(function() return e.human:IsWeaponDrawn() end)
        if okW and p.w == 1 and not drawn then p.wTry = now + 1.5; pcall(function() e.human:DrawWeapon() end)
        elseif okW and p.w == 0 and drawn then p.wTry = now + 1.5; pcall(function() e.human:HolsterWeapon() end) end
    end
end

function N.drive()
    if not N.enabled then return end
    local now = K.now()
    for name, p in pairs(N.puppets) do
        local o = N.own[name]
        if not o or o.owner == N.me or now - o.at > N.ownTtl then N.release(name, o and o.owner == N.me and 'mine' or 'no-owner')
        else K.try('npc-steer', steer, name, p, now) end
    end
end

-- ---------------------------------------------------------------- what this game reports
function N.near()
    if not N.enabled or not K.inWorldRaw() then return end
    local pp = player:GetWorldPos()
    local ok, list = pcall(System.GetEntitiesInSphere, pp, N.reach)
    if not ok or type(list) ~= 'table' then return end
    local found = {}
    for _, e in pairs(list) do
        if living(e) then
            local q = e:GetWorldPos()
            found[#found + 1] = { nameOf(e), math.sqrt((pp.x - q.x) ^ 2 + (pp.y - q.y) ^ 2 + (pp.z - q.z) ^ 2) }
        end
    end
    table.sort(found, function(a, b) return a[2] < b[2] end)
    local parts = {}
    for i = 1, math.min(#found, N.maxNear) do parts[i] = found[i][1] .. ':' .. string.format('%.1f', found[i][2]) end
    K.out('NPCNEAR', table.concat(parts, ';'))
end

function N.report()
    if not N.enabled or not K.inWorldRaw() then return end
    local now = K.now()
    local parts = {}
    for name, o in pairs(N.own) do
        if o.owner == N.me and now - o.at <= N.ownTtl then
            local e = System.GetEntityByName(name)
            if e and living(e) then
                local q = e:GetWorldPos()
                local l = N.last[name]
                local vx, vy = 0, 0
                if l and now > l.t then vx, vy = (q.x - l.x) / (now - l.t), (q.y - l.y) / (now - l.t) end
                if math.abs(vx) > 12 or math.abs(vy) > 12 then vx, vy = 0, 0 end            -- a teleport, not a walk
                N.last[name] = { x = q.x, y = q.y, t = now }
                local okA, a = pcall(function() return e:GetWorldAngles() end)
                local okW, w = pcall(function() return e.human:IsWeaponDrawn() end)
                parts[#parts + 1] = table.concat({ name, f2(q.x), f2(q.y), f2(q.z), string.format('%.3f', okA and a and a.z or 0), f2(vx), f2(vy), (okW and w) and '1' or '0' }, ',')
                if #parts == 12 then K.out('NPCST', table.concat(parts, ';')); parts = {} end
            end
        end
    end
    if #parts > 0 then K.out('NPCST', table.concat(parts, ';')) end
end

-- ---------------------------------------------------------------- the agent's lines
K.handlers['NPCMODE'] = function(f)
    local on = f[2] == '1'
    if not on then
        if N.enabled then N.releaseAll('mode-off') end
        N.enabled, N.own = false, {}
        return
    end
    if N.enabled and (f[3] ~= N.scope or f[4] ~= N.me) then N.releaseAll('scope') ; N.own = {} end
    N.enabled, N.scope, N.me = true, f[3] or '', f[4] or ''
end

K.handlers['NPCOWN'] = function(f)
    if not N.enabled or f[2] ~= N.scope or N.scope == '' then return end
    local now = K.now()
    for entry in string.gmatch(f[3] or '', '[^;]+') do
        local name, owner = string.match(entry, '^([^:]+):(%d+)$')
        if name and validName(name) then
            N.own[name] = { owner = owner, at = now }
            if owner == N.me and N.puppets[name] then N.release(name, 'mine') end
        end
    end
end

K.handlers['NPCSET'] = function(f)
    if not N.enabled or f[2] ~= N.scope or N.scope == '' or f[3] == N.me then return end
    local now = K.now()
    for entry in string.gmatch(f[4] or '', '[^;]+') do
        local name, x, y, z, yaw, vx, vy, w = string.match(entry, '^([^,]+),(-?[%d%.]+),(-?[%d%.]+),(-?[%d%.]+),(-?[%d%.]+),(-?[%d%.]+),(-?[%d%.]+),([01])$')
        local o = name and N.own[name]
        if o and o.owner == f[3] and o.owner ~= N.me and now - o.at <= N.ownTtl then          -- only the owner the host named may move it
            local e = System.GetEntityByName(name)
            if e and living(e) then
                local p = N.puppets[name] or take(name, e, now)
                if p then
                    p.x, p.y, p.z, p.yaw = tonumber(x), tonumber(y), tonumber(z), tonumber(yaw)
                    p.vx, p.vy, p.w, p.at = tonumber(vx), tonumber(vy), tonumber(w), now
                end
            end
        end
    end
end

K.every(0.1, 'npc-drive', function() K.try('npc-drive', N.drive) end)
K.every(0.2, 'npc-report', function() K.try('npc-report', N.report) end)
K.every(1.0, 'npc-near', function() K.try('npc-near', N.near) end)

function N.onWorldReset() N.puppets = {}; N.own = {}; N.last = {} end
