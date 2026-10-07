-- SPDX-License-Identifier: GPL-3.0-only
-- Render locomotion on an ordinary, removable NPC. Never create a native Player
-- or a fake network channel: their teardown clears state used by the local game.
local K=KCDUS
K.Locomotion={layer=0,failN=0}
local M=K.Locomotion
local idle='relaxed_idle_both'
local walk='relaxed_walk_medium'
local run='relaxed_run_medium'
local sprint='relaxed_sprint_fast'
local function clamp(v,a,b) return math.max(a,math.min(b,v)) end

function M.drive(ent,g,now)
    if g.animationReadyAt and now<g.animationReadyAt then return end
    if g.animationRetry and now<g.animationRetry then return end
    local flags=K.num(g.flags,0)
    local stale=now-(g.stamp or now)>0.4
    local speed=math.sqrt(K.num(g.vx,0)^2+K.num(g.vy,0)^2)
    if speed<0.15 then speed=K.num(g.observedSpeed,0) end
    speed=clamp(speed,0,8)
    if stale or flags%16>=8 or flags%4>=2 then speed=0 end
    local clip,rate=idle,1
    if speed>4.5 then clip,rate=sprint,clamp(speed/5.5,0.75,1.5)
    elseif speed>2.2 then clip,rate=run,clamp(speed/3.5,0.65,1.5)
    elseif speed>0.15 then clip,rate=walk,clamp(speed/1.5,0.65,1.5) end
    if g.clip~=clip then
        if clip==idle then
            local ok=pcall(function() ent:StopAnimation(0,M.layer) end)
            if ok then g.clip,g.clipRate=idle,1;g.animationRetry=nil end
            return
        end
        local ok,result=pcall(function() return ent:StartAnimation(0,clip,M.layer,0.2,rate,true,true) end)
        if not ok or result~=true then
            M.failN=M.failN+1;g.animationRetry=now+1
            if M.failN<=5 then K.out('ERR','ghost:animation',g.key or '',K.clean(result)) end
            return
        end
        g.clip,g.clipRate=clip,rate;g.animationRetry=nil
    elseif math.abs((g.clipRate or 1)-rate)>0.05 then
        local ok=pcall(function() ent:SetAnimationSpeed(0,M.layer,rate) end)
        if ok then g.clipRate=rate end
    end
end
