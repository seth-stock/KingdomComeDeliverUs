-- combat: shared OUTCOMES of fights (docs/CAPABILITIES.md, "Combat"). Proved in the private engine (tools/engine/damage_probe2.lua, damage_probe3.lua):
--   * soul:DealDamage(stamina, HEALTH, attackerId, suppressHitreaction) lowers an NPC's health through the ordinary damage path (read back: -5 for 5);
--   * a lethal one kills it (health 0, entity:IsDead() true) and its inventory stays readable as a corpse.
-- (The first probe called DealDamage(3, 0, ...): that is 3 STAMINA and 0 HEALTH, which is why "damage has no effect" was recorded wrongly.)
--
-- Every player's game keeps its own copy of the world (the same save, so the same named NPCs). While a shared world is in play each machine
--   * watches the health of the NPCs near ITS player, and tells the others when one lost health or died (KCDUS|CMB|...);
--   * applies what the others report to its own copy of that NPC: the damage is dealt the ordinary way with no attacker, so nobody is credited with
--     a kill they did not make, no crime is raised and the NPC's own AI is not provoked by it.
-- So an enemy two players fight loses the health of BOTH players' blows in BOTH copies, and dies in both. What is NOT shared: where the NPCs walk, who they
-- target, whether they notice a player, or fights between NPCs that no player is near. Damage is additive and order-free, so no player is "the" authority over health.
--
-- agent -> game:  CMBMODE|1|<scope>   shared outcomes on for this world (the agent only does this when both players are in the same world); CMBMODE|0 off
--                 CMBAPPLY|<scope>|<npc>|<damage>|<dead>   a friend's damage (or death) to <npc>
-- game -> agent:  KCDUS|CMB|<npc>|<damage>|<health now>|<dead>     what the local player's side did to an NPC
--                 KCDUS|CMBAPPLIED|<npc>|<health before>|<health after>|<note>
local K = KCDUS
System.LogAlways("KCDUS|LOAD|combat")
local C = { enabled = false, scope = '', hp = {}, dead = {}, near = 18, radius = 70, minDelta = 0.5, reported = 0, applied = 0, refused = 0 }
K.Combat = C

local function fmt(n) return string.format('%.2f', n) end

local function validName(n)
    return type(n) == 'string' and #n >= 3 and #n <= 80 and not string.find(n, '[^%w_%.%-%:@]') and not string.find(n, 'kcdus_', 1, true)
end

-- people: the engine's classes are NPC and NPC_Female (and a horse is not a person)
local function isPerson(e)
    local ok, cls = pcall(function() return e.class end)
    return ok and type(cls) == 'string' and string.sub(cls, 1, 3) == 'NPC'
end
C.isPerson = isPerson

local function health(e)
    local ok, v = pcall(function() return e.soul:GetState('health') end)
    if ok and type(v) == 'number' and v==v and math.abs(v)<math.huge then return v end
    return nil
end

local function isDead(e)
    local ok, v = pcall(function() return e:IsDead() end)
    return ok and v == true
end

local function nearPeople(pos, radius)
    local out = {}
    local ok, list = pcall(System.GetEntitiesInSphere, pos, radius)
    if not ok or type(list) ~= 'table' then return out end
    for _, e in pairs(list) do
        if e ~= player and isPerson(e) and e.soul then out[#out + 1] = e end
    end
    return out
end

local function flat(a, b) local dx, dy = a.x - b.x, a.y - b.y; return math.sqrt(dx * dx + dy * dy) end

function C.scan()
    if not C.enabled or not K.inWorldRaw() then return end
    local pp = player:GetWorldPos()
    local seen = {}
    for _, e in ipairs(nearPeople(pp, C.radius)) do
        local name = e:GetName()
        if validName(name) then
            local hp, dead = health(e), isDead(e)
            if hp then
                seen[name] = true
                local last = C.hp[name]
                if last == nil then
                    C.hp[name] = hp
                    if dead then C.dead[name] = true end
                else
                    local d = last - hp
                    local close = flat(pp, e:GetWorldPos()) <= C.near
                    if close then
                        if dead and not C.dead[name] then
                            C.reported = C.reported + 1
                            K.out('CMB', name, fmt(math.max(d, 0)), fmt(hp), 1)
                        elseif not dead and d >= C.minDelta then
                            C.reported = C.reported + 1
                            K.out('CMB', name, fmt(d), fmt(hp), 0)
                        end
                    end
                    C.hp[name] = hp
                    if dead then C.dead[name] = true end
                end
            end
        end
    end
    -- an NPC that left the watched radius forgets its baseline: damage it takes out of sight (the NPCs' own fights) must not be mistaken for the player's
    for name in pairs(C.hp) do
        if not seen[name] then C.hp[name] = nil; C.dead[name] = nil end
    end
end

K.handlers['CMBMODE'] = function(f)
    C.enabled = f[2] == '1'
    C.scope = f[3] or ''
    C.hp = {}; C.dead = {}
    K.out('CMBMODE', C.enabled and 'on' or 'off', C.scope)
end

K.handlers['CMBAPPLY'] = function(f)
    local scope, name, dmg, dead = f[2], f[3], tonumber(f[4]), f[5]
    if not C.enabled or not K.inWorldRaw() or scope ~= C.scope or scope == '' or not validName(name) or not dmg or dmg < 0 or dmg > 5000 then
        C.refused = C.refused + 1
        K.out('CMBAPPLIED', tostring(name), 0, 0, 'refused'); return
    end
    local e = System.GetEntityByName(name)
    if not e or e == player or not isPerson(e) or not e.soul then K.out('CMBAPPLIED', name, 0, 0, 'no-such-npc'); return end
    if isDead(e) then C.dead[name] = true; K.out('CMBAPPLIED', name, 0, 0, 'already-dead'); return end
    local before = health(e)
    if not before then K.out('CMBAPPLIED', name, 0, 0, 'unreadable'); return end
    local d = dmg
    if dead == '1' then d = before + 1000 end                     -- the friend's copy of this NPC died: so does this one
    if d <= 0 then K.out('CMBAPPLIED', name, before, before, 'nothing'); return end
    local ok = pcall(function() e.soul:DealDamage(0, d, nil, true) end)
    local after = health(e)
    local nowDead=isDead(e)
    -- Suppress echo even if the engine partially mutated before raising an
    -- error. A successful binding call alone is never an accepted outcome.
    C.hp[name] = after
    if nowDead then C.dead[name] = true end
    local expected=math.max(0,before-d)
    local verified=ok and after~=nil and math.abs(after-expected)<=0.02 and (dead~='1' or nowDead)
    if verified then C.applied=C.applied+1 else C.refused=C.refused+1 end
    local note=verified and 'applied' or not ok and 'call-failed' or 'readback-unverified'
    K.out('CMBAPPLIED', name, fmt(before), after and fmt(after) or '?', note)
end

K.every(0.2, 'combat-shared', function() K.try('combat', C.scan) end)

function C.onWorldReset() C.hp = {}; C.dead = {} end
