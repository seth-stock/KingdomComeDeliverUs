-- core: logging out to the agent, the safe-call wrapper, the 30 Hz tick, the command the agent talks to.
local K = KCDUS

K.running = false
K.tickN = 0
K.errN = 0
K.tickMs = 33
K.lastSeq = 0
K.handlers = {}
K.inWorldNow = false
K.fps = 0
K.tasks = {}

-- seconds, real time (the game's own clock keeps going in a loading screen; this one is monotonic)
function K.now()
    return System.GetCurrAsyncTime()
end

-- text that is safe to put inside a "..." console argument and a | separated record
function K.clean(s)
    s = tostring(s == nil and "" or s)
    s = string.gsub(s, '[%c"' .. string.char(92) .. '|~]', " ")
    return s
end

-- game -> agent: one line in kcd.log, KCDUS|KIND|field|field
function K.out(kind, ...)
    local n = select("#", ...)
    local line = "KCDUS|" .. kind
    for i = 1, n do
        line = line .. "|" .. tostring((select(i, ...)))
    end
    System.LogAlways(line)
end

function K.log(msg)
    K.out("LOG", K.clean(msg))
end

function K.try(label, f, ...)
    local ok, e = pcall(f, ...)
    if not ok then
        K.errN = K.errN + 1
        if K.errN <= 30 or K.errN % 200 == 0 then
            K.out("ERR", label, K.clean(e))
        end
    end
    return ok, e
end

function K.split(s, sep)
    local out, n, from = {}, 0, 1
    while true do
        local i, j = string.find(s, sep, from, true)
        if not i then
            n = n + 1
            out[n] = string.sub(s, from)
            return out
        end
        n = n + 1
        out[n] = string.sub(s, from, i - 1)
        from = j + 1
    end
end

function K.num(s, default)
    local v = tonumber(s)
    if v == nil then
        return default or 0
    end
    return v
end

-- The main menu is a live scene with a player entity in it (frozen clock, no quests), so "a game is started and a player exists"
-- is true in the menu too. What the menu does not have is a camera near that player: it films the village from a spot hundreds
-- of metres away (observed 280 m), while in the world the camera is on or behind the player.
K.MENU_CAMERA_M = 200
K.CUTSCENE_GRACE = 1.5   -- a cutscene camera can be far from Henry for a moment; the menu is far for good

function K.inWorldRaw()
    if not CryAction.IsGameStarted() then
        return false
    end
    if player == nil or player.soul == nil then
        return false
    end
    if Game.IsLoadingEngineSaveGame() then
        return false
    end
    local c = System.GetViewCameraPos()
    local p = player:GetWorldPos()
    if c ~= nil and p ~= nil then
        local dx, dy = c.x - p.x, c.y - p.y
        if dx * dx + dy * dy > K.MENU_CAMERA_M * K.MENU_CAMERA_M then
            return false
        end
    end
    return true
end

-- the player is in a loaded game (not the main menu, not a loading screen)
K.WORLD_CHECK_S = 0.25   -- the full check (camera, player position) is 4 a second, not 30

function K.inWorld()
    local now = K.now()
    if Game.IsLoadingEngineSaveGame() then
        K.lastWorldOk = nil   -- a load is a hard stop: no grace
        K.worldCheckedAt = nil
        return false
    end
    if K.worldCheckedAt ~= nil and now - K.worldCheckedAt < K.WORLD_CHECK_S then
        return K.worldCached
    end
    K.worldCheckedAt = now
    local r
    if K.inWorldRaw() then
        K.lastWorldOk = now
        r = true
    elseif K.lastWorldOk ~= nil and now - K.lastWorldOk < K.CUTSCENE_GRACE then
        r = true
    else
        r = false
    end
    K.worldCached = r
    return r
end

-- agent -> game. The agent calls: kcdus "<seq>~REC|f|f~REC|f"
function KCDUS_In(line)
    K.kick()
    local recs = K.split(tostring(line), "~")
    local seq = tonumber(recs[1])
    if seq then
        K.lastSeq = seq
    end
    for i = 2, #recs do
        local rec = recs[i]
        if rec ~= "" then
            local f = K.split(rec, "|")
            local h = K.handlers[f[1]]
            if h then
                K.try("in:" .. f[1], h, f)
            end
        end
    end
end

-- scheduled tasks: K.every(seconds, name, fn)
function K.every(seconds, name, fn)
    K.tasks[#K.tasks + 1] = { period = seconds, name = name, fn = fn, due = 0 }
end

function K.step(now)
    local w = K.inWorld()
    if w ~= K.inWorldNow then
        K.inWorldNow = w
        K.out("READY", w and 1 or 0)
    end
    for i = 1, #K.tasks do
        local t = K.tasks[i]
        if now >= t.due then
            -- fixed rate (so a 10 Hz task really is 10 Hz on a 30 Hz tick), but never a burst to catch up after a stall
            t.due = t.due + t.period
            if t.due < now then
                t.due = now + t.period
            end
            if w or t.always then
                K.try(t.name, t.fn, now)
            end
        end
    end
end

-- The engine drops every script timer when a save is loaded, so the update loop is a chain of one-shot timers that
-- anything can re-arm: K.kick() starts a new chain when the last tick is stale, and a new chain retires the old one.
function K.arm()
    K.chain = (K.chain or 0) + 1
    local id = K.chain
    local function tick()
        if id ~= K.chain or not K.running then
            return
        end
        K.lastTick = K.now()
        K.tickN = K.tickN + 1
        K.try("tick", K.step, K.lastTick)
        Script.SetTimer(K.tickMs, tick)
    end
    K.lastTick = K.now()
    Script.SetTimer(K.tickMs, tick)
end

function K.kick()
    if not K.running then
        return
    end
    if K.lastTick == nil or K.now() - K.lastTick > 0.5 then
        K.armN = (K.armN or 0) + 1
        K.arm()
    end
end

function K.start()
    K.running = true
    K.arm()
end

function K.stop()
    K.running = false
end

-- Re-arm when the player entity is (re)built by a load or a reset: wrap the class's callbacks once.
function K.hookPlayer()
    if K.hooked or Player == nil then
        return
    end
    K.hooked = true
    for _, name in ipairs({ "OnLoad", "OnReset", "OnSpawn", "OnInit", "OnResetLoad" }) do
        local callbackName=name
        local old = Player[name]
        if type(old) == "function" then
            Player[name] = function(...)
                if K.Ghosts and (callbackName=='OnLoad' or callbackName=='OnResetLoad') then K.Ghosts.onWorldReset() end
                if K.NativeIdentity and (callbackName=='OnLoad' or callbackName=='OnResetLoad') then K.NativeIdentity.clear() end
                K.try("hook:" .. name, K.kick)
                return old(...)
            end
        end
    end
end

K.handlers["PING"] = function(f)
    K.out("PONG", f[2] or "0", K.lastSeq)
end

K.every(1.0, "heartbeat", function(now)
    local dt = System.GetFrameTime()
    if dt and dt > 0 then
        K.fps = math.floor(1 / dt + 0.5)
    end
    K.out("HB", K.lastSeq, K.fps, K.tickN, K.errN)
end)
K.tasks[#K.tasks].always = true
