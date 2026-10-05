-- ghosts: every other player is a plain NPC body that is moved by script, smoothed between the 10 Hz updates.
local K = KCDUS
K.Ghosts = { list = {}, spawnedN = 0, spawnFailN = 0 }
local G = K.Ghosts

G.SNAP_DISTANCE = 8.0      -- farther than this from where it should be: jump, do not glide
G.EXTRAPOLATE_MAX = 0.4    -- seconds of velocity carried past the last update
G.SMOOTH_RATE = 12.0
G.RESPAWN_BACKOFF = 1.0
G.CLOTHES = "40f91af6-8aa6-fa06-c171-785c048b0893"   -- Henry's own clothing preset in the base game

local function entityOf(g)
    if g.id == nil then
        return nil
    end
    return System.GetEntity(g.id)
end

local function configure(ent)
    K.try("ghost:invulnerable", function() ent:Event_MakeInvulnerable() end)
    K.try("ghost:bt-off", function() ent:DisableBehaviorTreeEvaluation() end)
    K.try("ghost:bt-off2", function() AI.SetBehaviorTreeEvaluationEnabled(ent.id, false) end)
    K.try("ghost:no-dialog", function() ent.soul:RestrictDialog(true) end)
    if AIPARAM_INVISIBLE ~= nil then
        K.try("ghost:ai-invisible", function() AI.ChangeParameter(ent.id, AIPARAM_INVISIBLE, 1) end)
    end
    K.try("ghost:clothes", function() ent.actor:EquipClothingPreset(G.CLOTHES) end)
end

local function spawn(g, now)
    if g.nextSpawn and now < g.nextSpawn then
        return nil
    end
    g.nextSpawn = now + G.RESPAWN_BACKOFF
    local ok, ent = pcall(System.SpawnEntity, {
        class = "NPC",
        name = "kcdus_ghost_" .. g.key,
        position = { x = g.cx, y = g.cy, z = g.cz },
    })
    if not ok or ent == nil then
        G.spawnFailN = G.spawnFailN + 1
        if G.spawnFailN <= 5 then
            K.out("ERR", "ghost:spawn", g.key, K.clean(ent))
        end
        return nil
    end
    g.id = ent.id
    G.spawnedN = G.spawnedN + 1
    configure(ent)
    K.out("GHOST", "spawned", g.key, g.name)
    return ent
end

local function angleDelta(a, b)
    local d = (b - a) % (2 * math.pi)
    if d > math.pi then
        d = d - 2 * math.pi
    end
    return d
end

-- P|key|name|x,y,z|yaw|vx,vy,vz|flags|hp|stam|anim
K.handlers["P"] = function(f)
    local key = f[2]
    if key == nil or key == "" then
        return
    end
    local g = G.list[key]
    local pos = K.split(f[4] or "0,0,0", ",")
    local vel = K.split(f[6] or "0,0,0", ",")
    local x, y, z = K.num(pos[1]), K.num(pos[2]), K.num(pos[3])
    if g == nil then
        g = { key = key, cx = x, cy = y, cz = z, cyaw = K.num(f[5]) }
        G.list[key] = g
    end
    g.name = f[3] or key
    g.tx, g.ty, g.tz = x, y, z
    g.tyaw = K.num(f[5])
    g.vx, g.vy, g.vz = K.num(vel[1]), K.num(vel[2]), K.num(vel[3])
    g.flags = K.num(f[7])
    g.hp = K.num(f[8])
    g.anim = f[10] or ""
    g.stamp = K.now()
end

function G.remove(key)
    local g = G.list[key]
    if g == nil then
        return
    end
    local ent = entityOf(g)
    if ent ~= nil then
        K.try("ghost:remove", function() System.RemoveEntity(ent.id) end)
    end
    G.list[key] = nil
    K.out("GHOST", "removed", key, g.name or "")
end

function G.removeAll()
    local keys = {}
    for key in pairs(G.list) do
        keys[#keys + 1] = key
    end
    for _, key in ipairs(keys) do
        G.remove(key)
    end
end

K.handlers["PD"] = function(f)
    G.remove(f[2])
end

K.handlers["PA"] = function(f)
    G.removeAll()
end

function G.update(now)
    local dt = G.lastUpdate and (now - G.lastUpdate) or 0.033
    G.lastUpdate = now
    if dt > 0.25 then
        dt = 0.25
    end
    for key, g in pairs(G.list) do
        if g.tx ~= nil then
            local age = now - (g.stamp or now)
            local ext = age
            if ext > G.EXTRAPOLATE_MAX then
                ext = G.EXTRAPOLATE_MAX
            end
            local gx = g.tx + (g.vx or 0) * ext
            local gy = g.ty + (g.vy or 0) * ext
            local gz = g.tz
            local dx, dy, dz = gx - g.cx, gy - g.cy, gz - g.cz
            local dist = math.sqrt(dx * dx + dy * dy + dz * dz)
            if dist > G.SNAP_DISTANCE then
                g.cx, g.cy, g.cz = gx, gy, gz
            else
                local a = 1 - math.exp(-dt * G.SMOOTH_RATE)
                g.cx = g.cx + dx * a
                g.cy = g.cy + dy * a
                g.cz = g.cz + dz * a
            end
            g.cyaw = (g.cyaw or g.tyaw) + angleDelta(g.cyaw or g.tyaw, g.tyaw) * (1 - math.exp(-dt * G.SMOOTH_RATE))
            local ent = entityOf(g)
            if ent == nil then
                ent = spawn(g, now)
            end
            if ent ~= nil then
                K.try("ghost:move", function()
                    ent:SetWorldPos({ x = g.cx, y = g.cy, z = g.cz })
                    ent:SetWorldAngles({ x = 0, y = 0, z = g.cyaw })
                end)
            end
        end
    end
end

K.every(0.033, "ghosts", G.update)

function G.count()
    local n = 0
    for _ in pairs(G.list) do
        n = n + 1
    end
    return n
end
