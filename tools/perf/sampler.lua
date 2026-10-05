-- Frame-rate sampler for the soak: once a second logs SOAK|<fps>|<frames>|<seconds> from the engine's own frame counter.
-- Injected through the remote console (needs -devmode), so the same sampler measures the game with and without the mod.
SOAK_LAST_F = System.GetFrameID()
SOAK_LAST_T = System.GetCurrAsyncTime()
SOAK_GEN = (SOAK_GEN or 0) + 1
local gen = SOAK_GEN
function SOAK_TICK()
  if gen ~= SOAK_GEN then return end
  local f, t = System.GetFrameID(), System.GetCurrAsyncTime()
  local df, dt = f - SOAK_LAST_F, t - SOAK_LAST_T
  if dt > 0 then System.LogAlways(string.format("SOAK|%.1f|%d|%.2f", df / dt, df, dt)) end
  SOAK_LAST_F, SOAK_LAST_T = f, t
  Script.SetTimer(1000, SOAK_TICK)
end
Script.SetTimer(1000, SOAK_TICK)
