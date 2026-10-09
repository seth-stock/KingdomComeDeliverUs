-- pause: the shared pause (docs/CAPABILITIES.md, "Pausing"). Two halves, both proved in the private engine (tools/engine/pause_probe2.lua, menu_probe*.lua):
--
--   * FREEZE: another player's ESC menu is open, so this world stands too. The world is slowed to t_scale 0.001 (the scale the game's own inventory uses),
--     NOT stopped: at 0.001 a 1 ms game-time timer still fires about once a real second, and System.GetFrameID() keeps counting, so the freeze holds its own
--     LEASE inside the game: it lets go by itself after P.lease frames unless the agent keeps asking (FREEZE|1|<frames> every two seconds). A lost agent, a
--     lost link or a crash can therefore never leave the world frozen for good. (At t_scale 0 no timer fires and os.time() stands still: there could be no lease.)
--   * WATCH: the game's pause menu raises UI events (the MP_MenuWatch graph, MenuUi.cs): KCDUS_MenuEvent(1|0) tells the agent when this player's menu opens and
--     closes, so the others can be held.
--   * OWN MENU (the option "off", while a friend is in the world): the game's own ESC menu pauses natively (CCryAction::PauseGame, source 7); the UI's
--     ResumeGame node does not undo it and the console's wh_game_unpause exists only in developer mode. The engine adapter declines that pause instead
--     (native/KcdUs.EngineBridge, driven by the agent). This file only stops the inventory from slowing time meanwhile (OWNPAUSE|0), as KCD2 does.
--
-- agent -> game:  PAUSEWATCH            start the watcher graph (once per loaded world)
--                 FREEZE|1|<frames>     start or refresh the freeze (the lease, in frames)
--                 FREEZE|0              let go now
--                 OWNPAUSE|0|1          0: the inventory does not slow time (the option off with a friend here); 1: as the game had it
-- game -> agent:  KCDUS|PAUSE|menu|0|1|2   this player's menu closed / opened / the watcher is running
--                 KCDUS|PAUSE|frozen|<lease>   the freeze started (or was refreshed with a new lease)
--                 KCDUS|PAUSE|released|<why>   the freeze ended: agent | lease | reset
--                 KCDUS|PAUSE|own|<0|1>|<inventory pause cvar>   readback of OWNPAUSE
local K = KCDUS
System.LogAlways("KCDUS|LOAD|pause")
local P = { frozen = false, menuOpen = false, lease = 900, f0 = 0, prevScale = 1, chainId = 0, watcher = false, freezes = 0, ownPauses = true, invPrev = nil }
K.Pause = P

local FREEZE_SCALE = 0.001


function P.release(why)
    if not P.frozen then return end
    P.frozen = false
    P.chainId = P.chainId + 1                       -- the running chain sees a newer id and ends
    pcall(System.SetCVar, "t_scale", P.prevScale)
    K.out("PAUSE", "released", why or "agent")
end

local function chain(id)
    if id ~= P.chainId or not P.frozen then return end
    local used = System.GetFrameID() - P.f0
    if used >= P.lease then P.release("lease"); return end
    pcall(Script.SetTimer, 1, function() chain(id) end)
end

function P.freeze(lease)
    P.lease = math.max(240, math.min(6000, math.floor(lease or 900)))
    P.f0 = System.GetFrameID()                      -- every ask restarts the lease
    if P.frozen then return end
    -- the scale goes back to 1, never to what it was: a sleep or a wait raises it, and restoring a raised scale after that sleep ended would leave the world running fast
    P.prevScale = 1
    P.frozen = true
    P.freezes = P.freezes + 1
    P.chainId = P.chainId + 1
    pcall(System.SetCVar, "t_scale", FREEZE_SCALE)
    K.out("PAUSE", "frozen", P.lease)
    local id = P.chainId
    pcall(Script.SetTimer, 1, function() chain(id) end)
end

K.handlers["FREEZE"] = function(f)
    if f[2] == "1" then P.freeze(K.num(f[3], 900)) else P.release("agent") end
end

-- the pause menu's UI events (called from the MP_MenuWatch flow graph)
function KCDUS_MenuEvent(n)
    n = tonumber(n) or -1
    if n == 1 then P.menuOpen = true elseif n == 0 then P.menuOpen = false end
    K.out("PAUSE", "menu", n)
end

K.handlers["PAUSEWATCH"] = function(f)
    local ok, err = pcall(UIAction.StartAction, "MP_MenuWatch", {})
    P.watcher = ok
    if not ok then K.out("PAUSE", "watch-failed", K.clean(err)) end
end

-- the inventory's slow-down (wh_ui_InventoryPauseEnabled) while this player's own menus do not pause; the game's value comes back afterwards
local INV_CVAR = "wh_ui_InventoryPauseEnabled"
function P.ownPause(pauses)
    if pauses == P.ownPauses then return end
    P.ownPauses = pauses
    if not pauses then
        local ok, v = pcall(System.GetCVar, INV_CVAR)
        P.invPrev = ok and v or nil
        if P.invPrev ~= nil then pcall(System.SetCVar, INV_CVAR, 0) end
    elseif P.invPrev ~= nil then
        pcall(System.SetCVar, INV_CVAR, P.invPrev)
        P.invPrev = nil
    end
    local ok, now = pcall(System.GetCVar, INV_CVAR)
    K.out("PAUSE", "own", pauses and 1 or 0, ok and tostring(now) or "none")
end

K.handlers["OWNPAUSE"] = function(f) P.ownPause(f[2] ~= "0") end

-- a loaded world forgets the freeze's timer chain: the scale goes back at once, and the watcher is asked for again by the agent
function P.onWorldReset()
    if P.frozen then P.release("reset") end
    P.ownPause(true)
    P.menuOpen = false
    P.watcher = false
end
