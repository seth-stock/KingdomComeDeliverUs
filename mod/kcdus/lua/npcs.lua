-- npcs: shared enemies (docs/CAPABILITIES.md, "Shared enemies"). Where an NPC walks and whom it fights is decided by ONE game: its owner.
--   * The HOST's agent gives every NPC near the players to the player nearest to it (with hysteresis, docs/CAPABILITIES.md). That player's game runs the NPC
--     as usual: its brain walks it, notices, picks a fight with that player. This file reports where its owned NPCs are (NPCST, five times a second).
--   * Every OTHER game makes its copy of that NPC a puppet: the brain is switched off (DisableBehaviorTreeEvaluation, proved in the private engine: the NPC
--     stands still and can be placed) and the body follows the owner's samples, with the walk/run animation of the presence bodies and the weapon drawn when
--     the owner's is. So in every world the same bandit chases the same player. When the NPC comes nearest to ME it becomes mine: the brain is switched back on
--     and it fights me. Health and death are shared separately (combat.lua).
--   * A puppet is never kept without news: no sample for 4 s, the owner gone, the mode off, a loaded world or a save (menu.lua) gives the brain back at once.
--   * Combat research: the owner's game reads its NPC's native combat state (the engine adapter's read-only @kcdus/combat-state:
--     in combat, combat mode, current target). An NPC engaged with its owner stays with that owner (NPCNEAR's engaged flag: the host does not hand it to a
--     nearer player mid-fight), and each sample carries mode/engaged. The owner is hit by its own NPCs natively, in its own world. A puppet repeats the
--     owner's attacks: when the owner's NPC starts an attack (mode 4) at its owner, the puppet makes the engine's own attack (@kcdus/mirror/target+attack)
--     at the owner's presence body in an owned private probe. This cosmetic experiment retains the native action-probe gate; it does not prove independent
--     damaging combat. Normal play puppets walk, face and hold their weapons only.
--
--   * Damage mirror (an APPROXIMATION, not independent combat): when a blow of an NPC this game owns lowers MY health (it was attacking and engaged on me,
--     within 4 m), the owner reports the health actually lost (NPCHIT). The other games hear it (NPCHITIN) and, if their copy of that NPC is a current puppet
--     of that owner AND their Henry stands inside its reach (2.8 m, +-75 degrees in front, 1.6 m up/down), take that same health loss through the engine's
--     own soul:DealDamage, once per blow, read back (NPCHITAPPLIED). So one swing can hurt every player standing in front of it. Block, parry and armour of
--     the receiving Henry are NOT applied (the owner's measured loss is what travels), a puppet's brain is off so it never starts a fight of its own, and a
--     blow that missed the owner reports nothing. Switch: NPCDMGMIRROR|0.
-- agent -> game:  NPCMODE|1|<scope>|<my id>   on for this shared world;  NPCMODE|0  off (every puppet gets its brain back)
--                 NPCOWN|<scope>|name:owner;name:owner...      the host's decision (an entry lives 3 s unless repeated)
--                 NPCSET|<scope>|<from>|name,x,y,z,yaw,vx,vy,w;...  the owner's samples of its NPCs
-- game -> agent:  KCDUS|NPCNEAR|name:dist[:1];...             the living NPCs near this player (once a second, nearest 24); :1 = in combat with this player
--                 KCDUS|NPCST|name,x,y,z,yaw,vx,vy,w[,mode,active,engaged];...   the NPCs this game owns (5 a second, 12 a line)
--                 KCDUS|NPCMIRROR|<name>|<owner>|<ok>          a puppet repeated its owner's attack (readback)
--                 KCDUS|NPCPUP|<name>|<on|off>|<why>           a puppet taken or given back (readback)
--                 KCDUS|NPCHIT|<name>|<seq>|<health lost>      a blow of my NPC hurt me (for the other players' copies)
--                 KCDUS|NPCHITAPPLIED|<name>|<seq>|<asked>|<lost>|<ok>   a friend's NPC's blow, taken by my Henry (health read back)
--                 KCDUS|NPCHITSKIP|<name>|<seq>|<why>          a friend's blow that did not reach me (out of reach, behind, dead, ...)
-- agent -> game:  NPCHITIN|<scope>|<from>|<name>|<seq>|<health>   a friend's blow (the agent checked it came from the NPC's owner)
local K = KCDUS
System.LogAlways("KCDUS|LOAD|npcs")
local N = { enabled = false, scope = '', me = '', own = {}, puppets = {}, last = {}, reach = 40, maxNear = 24, ownTtl = 3, staleS = 4, snapM = 6, taken = 0, released = 0,
    hitReach = 2.8, hitArc = 75, hitMax = 60, hitSeq = 0, hits = 0, hitsApplied = 0, attackAt = {}, hitAt = {}, hitSeen = {}, damageMirror = true }
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

-- ---------------------------------------------------------------- combat (the engine adapter; nil without it)
function N.combatState(e)
    local ok, s = pcall(function() return e.human:PlayAnim('@kcdus/combat-state', '') end)
    if ok and type(s) == 'table' and s.present == 1 then return s end
    return nil
end

local function entityNumber(e)
    if e and type(e.id) == 'number' then return e.id end
    local hex = e and string.match(tostring(e.id), '([0-9A-Fa-f]+)$')
    return hex and tonumber(hex, 16) or nil
end
-- The adapter names a fight's target by the actor's own id (human+0x30), which is NOT the Lua entity id (found on the two-game rig: the player is
-- 0x7777 to Lua and a different number to the engine's combat). The adapter reports each actor's own id as `self`: compare targets with that.
function N.myEntity()
    local ok, s = pcall(function() return player.human:PlayAnim('@kcdus/combat-state', '') end)
    if ok and type(s) == 'table' and type(s.self) == 'number' and s.self ~= 0 then return s.self end
    return entityNumber(player)
end

-- the owner's attack, repeated by its puppet at the owner's presence body (never damaging: ghosts are invulnerable)
function N.mirrorAttack(name, p, e)
    local o = N.own[name]
    local g = o and K.Ghosts and K.Ghosts.list[o.owner]
    local ghost = g and g.id and System.GetEntity(g.id)
    if not ghost or ghost:GetName() ~= 'kcdus_ghost_' .. o.owner then return false end
    local entity = entityNumber(ghost)
    if not entity then return false end
    local id = string.format('%08x', entity)
    local okT, t = pcall(function() return e.human:PlayAnim('@kcdus/mirror/target', id) end)
    if not okT or type(t) ~= 'table' or t.reason ~= 0 then return false end
    -- The native target changed even if the following attack factory refuses.
    p.mirroredTarget = true
    local okA, a = pcall(function() return e.human:PlayAnim('@kcdus/mirror/attack', '') end)
    local done = okA and type(a) == 'table' and a.reason == 0 and a.submitted == 1
    N.mirrored = (N.mirrored or 0) + (done and 1 or 0)
    K.out('NPCMIRROR', name, o.owner, done and 1 or 0)
    return done
end

local function mirrorClear(e) pcall(function() e.human:PlayAnim('@kcdus/mirror/clear', '') end) end

-- ---------------------------------------------------------------- the puppets
local function brain(e, on)
    return pcall(function() if on then e:EnableBehaviorTreeEvaluation() else e:DisableBehaviorTreeEvaluation() end end)
end

function N.release(name, why)
    local p = N.puppets[name]
    if not p then return end
    N.puppets[name] = nil
    local e = System.GetEntityByName(name)
    if e and e.id == p.id then
        pcall(function() e:StopAnimation(0, K.Locomotion and K.Locomotion.layer or 0) end)
        if p.mirroredTarget then mirrorClear(e) end
        brain(e, true)
    end
    N.released = N.released + 1
    K.out('NPCPUP', name, 'off', why or '')
end

function N.releaseAll(why) for name in pairs(N.puppets) do N.release(name, why) end end

local function take(name, e, now)
    if not brain(e, false) then return nil end
    local p = { id = e.id, at = now, g = { key = name, flags = 0 } }
    N.puppets[name] = p
    N.taken = N.taken + 1
    K.out('NPCPUP', name, 'on', N.own[name] and N.own[name].owner or '')
    return p
end

local function steer(name, p, now)
    local e = System.GetEntityByName(name)
    if not e or isDead(e) then N.release(name, 'dead'); return end
    if e.id ~= p.id then N.release(name, 'stream-replaced'); return end
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
    -- the owner's attack starts (mode 4, at its owner): the puppet attacks the owner's presence body once per attack
    if p.mode == 4 and p.lastMode ~= 4 and p.active == 1 and p.engaged == 1 then
        if N.mirrorAttack(name, p, e) then p.mirroredTarget = true end
    end
    if p.mirroredTarget and (p.active ~= 1 or p.engaged ~= 1) then mirrorClear(e); p.mirroredTarget = nil end
    p.lastMode = p.mode
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
            local s = N.combatState(e)
            found[#found + 1] = { nameOf(e), math.sqrt((pp.x - q.x) ^ 2 + (pp.y - q.y) ^ 2 + (pp.z - q.z) ^ 2), s and s.active == 1 and s.target == N.myEntity() }
        end
    end
    table.sort(found, function(a, b) return a[2] < b[2] end)
    local parts = {}
    for i = 1, math.min(#found, N.maxNear) do parts[i] = found[i][1] .. ':' .. string.format('%.1f', found[i][2]) .. (found[i][3] and ':1' or '') end
    K.out('NPCNEAR', table.concat(parts, ';'))
end

-- ---------------------------------------------------------------- the damage mirror
local function hypot(a, b) return math.sqrt((a.x - b.x) ^ 2 + (a.y - b.y) ^ 2) end
-- the owner's side: my health fell while an NPC I own was attacking and engaged on me, close by
function N.watchHealth(now)
    local ok, hp = pcall(function() return player.soul:GetState('health') end)
    if not ok or type(hp) ~= 'number' or hp ~= hp then return end
    local prev = N.lastHp
    N.lastHp = hp
    if not N.damageMirror or not prev or hp >= prev - 0.4 then return end
    local pp = player:GetWorldPos()
    local best, bestD
    for name, at in pairs(N.attackAt) do
        local o = N.own[name]
        if o and o.owner == N.me and now - at <= 1.2 and now - (N.hitAt[name] or -9) >= 0.4 then
            local e = System.GetEntityByName(name)
            if e and living(e) then
                local d = hypot(pp, e:GetWorldPos())
                if d <= 4 and (not bestD or d < bestD) then best, bestD = name, d end
            end
        end
    end
    if best then
        N.hitAt[best] = now
        N.hitSeq = N.hitSeq + 1
        N.hits = N.hits + 1
        K.out('NPCHIT', best, N.hitSeq, string.format('%.1f', math.min(prev - hp, N.hitMax)))
    end
end

-- the other side: a friend's NPC's blow, if my Henry stands where the swing would have reached
function N.takeRemoteHit(from, name, seq, hp)
    local now = K.now()
    local o, p = N.own[name], N.puppets[name]
    local function skip(why) K.out('NPCHITSKIP', name, seq, why) end
    if not N.damageMirror then return skip('off') end
    if not (o and o.owner == from and p and now - p.at <= N.staleS) then return skip('no-puppet') end
    local key = from .. ':' .. name
    if (N.hitSeen[key] or 0) >= seq then return end                          -- once per blow, even if the puppet was released and taken again meanwhile
    N.hitSeen[key] = seq
    local e = System.GetEntityByName(name)
    if not e or e.id ~= p.id or isDead(e) then return skip('npc-gone') end
    if isDead(player) or not K.inWorldRaw() then return skip('henry-unavailable') end
    local q, pp = e:GetWorldPos(), player:GetWorldPos()
    local dx, dy = pp.x - q.x, pp.y - q.y
    local d = math.sqrt(dx * dx + dy * dy)
    if d > N.hitReach or math.abs(pp.z - q.z) > 1.6 then return skip('out-of-reach') end
    if d > 0.3 then
        local yaw = p.yaw or 0
        local fx, fy = -math.sin(yaw), math.cos(yaw)                       -- the engine's forward at yaw 0 is +y, counter-clockwise
        local cos = (dx * fx + dy * fy) / d
        if cos < math.cos(math.rad(N.hitArc)) then return skip('behind') end
    end
    local okB, before = pcall(function() return player.soul:GetState('health') end)
    if not okB or type(before) ~= 'number' then return skip('no-health') end
    local attacker = nil
    pcall(function() attacker = e.soul:GetId() end)
    local ok = pcall(function() player.soul:DealDamage(0, hp, attacker, true) end)
    local okA, after = pcall(function() return player.soul:GetState('health') end)
    after = okA and type(after) == 'number' and after or before
    N.lastHp = after                                                         -- my own health watch must not report this as a blow of mine
    N.hitsApplied = N.hitsApplied + 1
    K.out('NPCHITAPPLIED', name, seq, string.format('%.1f', hp), string.format('%.1f', before - after), ok and 1 or 0)
end

-- Which friend's GHOST (if any) this NPC is fighting in my world: the adapter's target id against each ghost's own id. The two-game rig showed an
-- owner's enemy picking the other player's ghost (invulnerable here: its blows land nowhere). The host then gives the NPC to THAT player, whose game
-- runs it against the real Henry.
function N.ghostTarget(s)
    if not s or s.active ~= 1 or not s.target or s.target == 0 or not (K.Ghosts and K.Ghosts.list) then return '0' end
    for key, g in pairs(K.Ghosts.list) do
        local e = g.id and System.GetEntity(g.id)
        if e and e.human then
            local ok, gs = pcall(function() return e.human:PlayAnim('@kcdus/combat-state', '') end)
            if ok and type(gs) == 'table' and gs.self and gs.self ~= 0 and gs.self == s.target and string.match(tostring(key), '^%d+$') then return tostring(key) end
        end
    end
    return '0'
end

function N.report()
    if not N.enabled or not K.inWorldRaw() then return end
    local now = K.now()
    N.watchHealth(now)
    local parts = {}
    for name, o in pairs(N.own) do
        if o.owner == N.me and now - o.at <= N.ownTtl then
            local e = System.GetEntityByName(name)
            if e and living(e) then
                if N.puppets[name] and N.puppets[name].id ~= e.id then N.release(name, 'stream-replaced') end
                local q = e:GetWorldPos()
                local l = N.last[name]
                local vx, vy = 0, 0
                if l and now > l.t then vx, vy = (q.x - l.x) / (now - l.t), (q.y - l.y) / (now - l.t) end
                if math.abs(vx) > 12 or math.abs(vy) > 12 then vx, vy = 0, 0 end            -- a teleport, not a walk
                N.last[name] = { x = q.x, y = q.y, t = now }
                local okA, a = pcall(function() return e:GetWorldAngles() end)
                local okW, w = pcall(function() return e.human:IsWeaponDrawn() end)
                local row = { name, f2(q.x), f2(q.y), f2(q.z), string.format('%.3f', okA and a and a.z or 0), f2(vx), f2(vy), (okW and w) and '1' or '0' }
                local s = N.combatState(e)
                if s then
                    row[9] = tostring(math.max(0, math.min(64, s.mode or 0)))
                    row[10] = s.active == 1 and '1' or '0'
                    row[11] = (s.active == 1 and s.target == N.myEntity()) and '1' or '0'
                    if s.active == 1 and row[11] == '1' then N.attackAt[name] = now end      -- fighting me: a health drop now is its blow
                    row[12] = N.ghostTarget(s)
                end
                parts[#parts + 1] = table.concat(row, ',')
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
        N.lastHp, N.attackAt, N.hitAt, N.hitSeen = nil, {}, {}, {}
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
        local name, x, y, z, yaw, vx, vy, w, rest = string.match(entry, '^([^,]+),(-?[%d%.]+),(-?[%d%.]+),(-?[%d%.]+),(-?[%d%.]+),(-?[%d%.]+),(-?[%d%.]+),([01])(.*)$')
        local mode, active, engaged = 0, 0, 0
        if rest and rest ~= '' then
            local m, a, g = string.match(rest, '^,(%d+),([01]),([01])$')
            if not m then m, a, g = string.match(rest, '^,(%d+),([01]),([01]),%d+$') end
            if not m then name = nil else mode, active, engaged = tonumber(m), tonumber(a), tonumber(g) end
        end
        local o = name and N.own[name]
        if o and o.owner == f[3] and o.owner ~= N.me and now - o.at <= N.ownTtl then          -- only the owner the host named may move it
            local e = System.GetEntityByName(name)
            if e and living(e) then
                local p = N.puppets[name] or take(name, e, now)
                if p then
                    p.x, p.y, p.z, p.yaw = tonumber(x), tonumber(y), tonumber(z), tonumber(yaw)
                    p.vx, p.vy, p.w, p.at = tonumber(vx), tonumber(vy), tonumber(w), now
                    p.mode, p.active, p.engaged = mode, active, engaged
                end
            end
        end
    end
end

local function hitCounter(t) local n = tonumber(t); return n and n >= 1 and n < 1e12 and n == math.floor(n) and n or nil end
K.handlers['NPCHITIN'] = function(f)
    if not N.enabled or f[2] ~= N.scope or N.scope == '' or f[3] == N.me then return end
    local name, seq, hp = f[4], hitCounter(f[5]), tonumber(f[6])
    if not validName(name) or not seq or not hp or hp ~= hp or hp < 0.5 or hp > N.hitMax then return end
    N.takeRemoteHit(f[3], name, seq, hp)
end
K.handlers['NPCDMGMIRROR'] = function(f) N.damageMirror = f[2] ~= '0' end

K.every(0.1, 'npc-drive', function() K.try('npc-drive', N.drive) end)
K.every(0.2, 'npc-report', function() K.try('npc-report', N.report) end)
K.every(1.0, 'npc-near', function() K.try('npc-near', N.near) end)

-- Ownership v2: host incarnation + per-NPC term + monotonic sample sequence.
-- Legacy handlers remain for private historical probes, but cannot bypass an active v2 authority.
local legacyOwn, legacySet = K.handlers.NPCOWN, K.handlers.NPCSET
K.handlers.NPCOWN=function(f) if not N.authority then legacyOwn(f) end end
K.handlers.NPCSET=function(f) if not N.authority then legacySet(f) end end
local function counter(s) local n=tonumber(s); return n and n>0 and n<1000000000000 and n==math.floor(n) and n or nil end
K.handlers.NPCOWN2=function(f)
    if not N.enabled or f[2]~=N.scope or type(f[3])~='string' or #f[3]~=32 or string.find(f[3],'[^0-9a-f]') then return end
    local revision=counter(f[4]); if not revision then return end
    N.retired=N.retired or {}
    if N.retired[f[3]] then return end
    if N.authority~=f[3] then
        N.releaseAll('authority-change')
        if N.authority then N.retired[N.authority]=true end
        N.authority=f[3]; N.ownerRevision=0; N.own={}; N.samples={}
    end
    if revision<(N.ownerRevision or 0) then return end
    local updates={}
    for entry in string.gmatch(f[5] or '', '[^;]+') do
        local name,owner,term=string.match(entry,'^([^:]+):(%d+):(%d+)$')
        term=counter(term)
        if not name or not validName(name) or not term or tonumber(owner)<1 or tonumber(owner)>255 or updates[name] then return end
        local old=N.own[name]
        if old and old.term and (term<old.term or term==old.term and owner~=old.owner) then return end
        updates[name]={owner=owner,term=term,at=K.now()}
    end
    for name,o in pairs(updates) do
        local old=N.own[name]
        if old and (old.owner~=o.owner or old.term~=o.term) then N.release(name,'ownership-term') end
        N.own[name]=o
        if o.owner==N.me then N.release(name,'mine') end
    end
    N.ownerRevision=revision
end
K.handlers.NPCSET2=function(f)
    if not N.enabled or f[2]~=N.scope or not N.authority or f[3]~=N.authority or f[4]==N.me then return end
    local sequence=counter(f[5]); if not sequence or sequence<=((N.samples or {})[f[4]] or 0) then return end
    local rows={}
    for entry in string.gmatch(f[6] or '', '[^;]+') do
        local name,term,body=string.match(entry,'^([^,]+),(%d+),(.+)$')
        local o=name and N.own[name]
        if o and o.owner==f[4] and counter(term)==o.term then rows[#rows+1]=name..','..body end
    end
    if #rows==0 then return end
    N.samples=N.samples or {}; N.samples[f[4]]=sequence
    legacySet({'NPCSET',f[2],f[4],table.concat(rows,';')})
end
local legacyMode=K.handlers.NPCMODE
K.handlers.NPCMODE=function(f)
    if f[2]~='1' or f[3]~=N.scope or f[4]~=N.me then
        N.authority=nil; N.ownerRevision=0; N.samples={}; N.retired={}
    end
    legacyMode(f)
end
function N.onWorldReset()
    N.lastHp=nil; N.attackAt={}; N.hitAt={}; N.hitSeen={}
    N.puppets={}; N.own={}; N.last={}; N.authority=nil; N.ownerRevision=0; N.samples={}; N.retired={}
end
