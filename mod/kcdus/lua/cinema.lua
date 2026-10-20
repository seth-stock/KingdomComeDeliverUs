-- SPDX-License-Identifier: GPL-3.0-only
-- cinema: is a cutscene playing for this player? In KCD1 the camera is on Henry's head in play and in dialogue; a cutscene films from its own camera, a few
-- metres to a few hundred metres away. (The main menu is 280 m away and is handled by inWorldRaw; this is the nearer range, which inWorldRaw calls "in the
-- world".) Why it matters: nothing may move a player who is watching a scene (a teleport to the host used to cut the scene off), and the host's friends are told
-- when the host is in one. The state is a flag in every ST sample (local.lua, F_CINEMA) and an edge line KCDUS|CINE|1|0.
local K = KCDUS
K.Cinema = { on = false, camM = 6.0, onHold = 0.6, offHold = 0.8, since = nil, dist = 0, edges = 0 }
local C = K.Cinema

local function cameraDistance()
    local ok, cam = pcall(System.GetViewCameraPos)
    local ok2, pos = pcall(function() return player:GetWorldPos() end)
    if not ok or not ok2 or cam == nil or pos == nil then return nil end
    local dx, dy, dz = cam.x - pos.x, cam.y - pos.y, cam.z - pos.z
    return math.sqrt(dx * dx + dy * dy + dz * dz)
end

function C.step(now)
    local menu = K.Pause and K.Pause.menuOpen
    local d = (K.inWorldRaw() and not menu) and cameraDistance() or nil
    C.dist = d or 0
    local want = d ~= nil and d > C.camM
    if want == C.on then C.since = nil; return end
    C.since = C.since or now
    if now - C.since >= (want and C.onHold or C.offHold) then
        C.on = want; C.since = nil; C.edges = C.edges + 1
        K.out('CINE', want and 1 or 0)
    end
end

function C.reset() C.on = false; C.since = nil end
K.every(0.2, 'cinema', C.step)
