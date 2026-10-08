-- Private cloned-world probe (engine_session.py guards). t_scale 0.001 instead of 0: do game-time timers still fire (about once a real second for a 1 ms timer)?
-- A frame-counted lease (System.GetFrameID keeps counting) releases the freeze from INSIDE the game. The harness also sends t_scale 1 from outside as a backstop.
if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
local function L(s) System.LogAlways('KCDUS|ENGINE|'..s) end
KCDUS.pp = {}
local P = KCDUS.pp
P.f0 = System.GetFrameID(); P.wt0 = Calendar.GetWorldTime(); P.fires = 0; P.leaseFrames = 900; P.on = true
L('pause2-set|before='..tostring(System.GetCVar('t_scale')))
System.SetCVar('t_scale', 0.001)
L('pause2-set|now='..tostring(System.GetCVar('t_scale')))
local function check()
    if not P.on then return end
    P.fires = P.fires + 1
    local used = System.GetFrameID() - P.f0
    if P.fires <= 6 or P.fires % 10 == 0 then L('pause2-timer|fire='..P.fires..' frames='..used) end
    if used >= P.leaseFrames then
        System.SetCVar('t_scale', 1)
        P.on = false
        L('pause2-lease-expired-released|fire='..P.fires..' frames='..used..' t_scale='..tostring(System.GetCVar('t_scale')))
        return
    end
    Script.SetTimer(1, check)
end
Script.SetTimer(1, check)
