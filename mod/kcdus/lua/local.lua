-- local: sample this player 10 times a second and tell the agent; apply the shared world time the agent sends.
local K = KCDUS
K.Local = {}
local L = K.Local

local function safe(f, default)
    local ok, v = pcall(f)
    if ok and v ~= nil then
        return v
    end
    return default
end

L.F_WEAPON = 1
L.F_MOUNTED = 2
L.F_DIALOG = 4
L.F_DEAD = 8
L.F_DANGER = 16

function L.sample()
    local p = player
    local pos = p:GetWorldPos()
    local ang = p:GetWorldAngles()
    local vel = { x = 0, y = 0, z = 0 }
    safe(function() p:GetVelocity(vel) end)
    local human = p.human
    local flags = 0
    if human then
        if safe(function() return human:IsWeaponDrawn() end, false) then flags = flags + L.F_WEAPON end
        if safe(function() return human:IsMounted() end, false) then flags = flags + L.F_MOUNTED end
        if safe(function() return human:IsInDialog() end, false) then flags = flags + L.F_DIALOG end
    end
    if safe(function() return p:IsDead() end, false) then flags = flags + L.F_DEAD end
    if safe(function() return p.soul:IsInCombatDanger() end, false) then flags = flags + L.F_DANGER end
    local hp = safe(function() return p.soul:GetState("health") end, 0)
    local st = safe(function() return p.soul:GetState("stamina") end, 0)
    local anim = safe(function() return p.actor:GetCurrentAnimationState() end, "")
    local wt = safe(function() return Calendar.GetWorldTime() end, 0)
    return {
        x = pos.x, y = pos.y, z = pos.z, yaw = ang.z,
        vx = vel.x, vy = vel.y, vz = vel.z,
        flags = flags, hp = hp, st = st, anim = anim, wt = wt,
    }
end

function L.emit(now)
    local s = L.sample()
    L.last = s
    K.out("ST",
        string.format("%.2f,%.2f,%.2f", s.x, s.y, s.z),
        string.format("%.3f", s.yaw),
        string.format("%.2f,%.2f,%.2f", s.vx, s.vy, s.vz),
        s.flags,
        string.format("%.0f", s.hp),
        string.format("%.0f", s.st),
        K.clean(s.anim),
        string.format("%.0f", s.wt))
end

K.every(0.1, "sample", L.emit)

-- TIME|worldTimeSeconds: the shared clock. The agent only sends it when this machine has drifted.
K.handlers["TIME"] = function(f)
    local t = tonumber(f[2])
    if t then
        Calendar.SetWorldTime(t)
    end
end

-- TP|x,y,z|yaw : bring the player somewhere (the join-or-stay choice brings a friend beside the host; the tether pulls one back)
K.handlers["TP"] = function(f)
    local pos = K.split(f[2] or "", ",")
    local x, y, z = tonumber(pos[1]), tonumber(pos[2]), tonumber(pos[3])
    if x == nil or y == nil or z == nil then
        return
    end
    player:SetWorldPos({ x = x, y = y, z = z })
    local yaw = tonumber(f[3])
    if yaw ~= nil then
        K.try("tp:angles", function() player:SetWorldAngles({ x = 0, y = 0, z = yaw }) end)
    end
    K.out("TELEPORTED", string.format("%.1f,%.1f,%.1f", x, y, z))
end
