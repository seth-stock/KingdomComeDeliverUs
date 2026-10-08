-- Private cloned-world probe (engine_session.py guards). Does anything of Lua still run while t_scale is 0?
-- The freeze is released from OUTSIDE (the harness sends t_scale 1 over the remote console) so a failure here can never leave the private game stuck for good.
if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
local function L(s) System.LogAlways('KCDUS|ENGINE|'..s) end
KCDUS.pp = KCDUS.pp or {}; local KCDP = KCDUS.pp
KCDP.t0 = os.time(); KCDP.f0 = System.GetFrameID(); KCDP.wt0 = Calendar.GetWorldTime()
L('pause-set|t_scale before='..tostring(System.GetCVar('t_scale'))..' clock='..KCDP.t0..' world='..KCDP.wt0)
System.SetCVar('t_scale', 0)
L('pause-set|t_scale now='..tostring(System.GetCVar('t_scale')))
-- 1: a Script timer
Script.SetTimer(1000, function() L('pause-timer-fired|real_s='..(os.time()-KCDP.t0)..' frames='..(System.GetFrameID()-KCDP.f0)..' t_scale='..tostring(System.GetCVar('t_scale'))) end)
-- 2: the mod's own once-a-frame-ish callbacks: UI audio events, entity update
KCDP.ticks = 0
local ok,e=pcall(function()
    local old = player.OnUpdate
    player.OnUpdate = function(self, dt, ...) KCDP.ticks = KCDP.ticks + 1; if old then return old(self, dt, ...) end end
end)
L('pause-hook-player-OnUpdate|'..tostring(ok)..'|'..tostring(e))
