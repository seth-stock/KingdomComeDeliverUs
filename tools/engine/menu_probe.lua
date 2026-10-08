-- Private cloned-world probe (engine_session.py guards). Does the ESC (pause) menu raise UI events a listener can see, and does it pause the world?
if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
local function L(s) System.LogAlways('KCDUS|ENGINE|'..s) end
KCDUS.mp = KCDUS.mp or {}
local P = KCDUS.mp
P.events = 0
P.f0 = System.GetFrameID(); P.w0 = Calendar.GetWorldTime()
local names = { 'Menu', 'IngameMenu', 'InGameMenu', 'PauseMenu', 'HUD', 'MainMenu', 'Pause' }
local evs = { 'OnShow', 'OnHide', 'OnPlayAudio', 'OnButton', 'OnPause', 'OnResume', 'OnOpen', 'OnClose' }
P.handler = {}
for _, n in ipairs(names) do
    for _, ev in ipairs(evs) do
        local key = n..'.'..ev
        P.handler[key] = function() P.events = P.events + 1; L('menu-event|'..key..'|frames='..(System.GetFrameID()-P.f0)..' world='..(Calendar.GetWorldTime()-P.w0)) end
        local ok, e = pcall(UIAction.RegisterElementListener, P.handler, n, -1, ev, key)
        -- the listener's callback name is looked up on the table: provide it under that name
        P.handler[key] = P.handler[key]
        if ok then L('menu-listen|'..key..'|'..tostring(e)) end
    end
end
-- a game-time timer chain: does it keep firing while the pause menu is open?
P.fires = 0
local function tick() P.fires = P.fires + 1; Script.SetTimer(500, tick) end
Script.SetTimer(500, tick)
L('menu-ready')
