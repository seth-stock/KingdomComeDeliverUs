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
M.swing='combat_lg_z0_natksw_lngsw'
local function clamp(v,a,b) return math.max(a,math.min(b,v)) end

function M.drive(ent,g,now)
    if g.animationReadyAt and now<g.animationReadyAt then return end
    if g.animationRetry and now<g.animationRetry then return end
    if g.swingAt and now-g.swingAt<K.Ghosts.SWING_LEN then
        if g.swungAt~=g.swingAt then
            -- the engine's own long-sword swing (combat_lg_z0_natksw_lngsw, one of the clips of the base game); then the walk/idle clip is started again
            local ok,result=pcall(function() return ent:StartAnimation(0,M.swing,M.layer,0.1,1,false,true) end)
            g.swungAt=g.swingAt;g.clip=nil;g.swingOk=ok and result==true
            K.out('SWING',g.key or '',g.swingOk and 1 or 0)
        end
        return
    end
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
    if g.clip~=clip or (clip==idle and now-(g.clipAt or 0)>5) then   -- an idle body is re-told its idle every few seconds: any engine event that drops its animation is healed, invisibly
        -- idle is a looping clip like the others: stopping the animation altogether leaves a body whose brain is off in its bind pose (arms out, "T-pose")
        local ok,result=pcall(function() return ent:StartAnimation(0,clip,M.layer,0.2,rate,true,true) end)
        if not ok or result~=true then
            M.failN=M.failN+1;g.animationRetry=now+1
            if M.failN<=5 then K.out('ERR','ghost:animation',g.key or '',K.clean(result)) end
            return
        end
        g.clip,g.clipRate,g.clipAt=clip,rate,now;g.animationRetry=nil
    elseif math.abs((g.clipRate or 1)-rate)>0.05 then
        local ok=pcall(function() ent:SetAnimationSpeed(0,M.layer,rate) end)
        if ok then g.clipRate=rate end
    end
end
