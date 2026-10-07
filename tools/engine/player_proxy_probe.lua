-- Private-world experiment; channel 77 is reserved solely inside this probe process.
local original=player
if not KCDUS.inWorldRaw() then error('A confirmed loaded disposable world is required') end
local pos=original:GetWorldPos()
local originalSoul=original.soul:GetId()
local ok,value=pcall(function() return g_gameRules.game:SpawnPlayer(77,'kcdus_player_probe','Player',pos,original:GetWorldAngles()) end)
System.LogAlways('KCDUS|ENGINE|spawn-player|'..tostring(ok)..'|'..tostring(value)..'|'..type(value))
System.LogAlways('KCDUS|ENGINE|local-player-preserved|'..tostring(player==original))
if not ok or not value then return end
local g=type(value)=='table' and value or System.GetEntity(value)
if not g or not g.actor or player~=original then error('Remote player initialization invalid') end
KCDUS_ENGINE_REMOTE=g
System.LogAlways('KCDUS|ENGINE|proxy-channel|'..tostring(g.actor:GetChannel())..'|'..tostring(g.actor:IsPlayer()))
g.actor:MakeLookAsActor(original.id,true)
Script.SetTimer(500,function()
    g.human:PlayAnim('MotionMovement','walk')
    g.human:SetAnimMotionParam(0,1.5)
    Script.SetTimer(300,function() System.LogAlways('KCDUS|ENGINE|proxy-state|'..tostring(g.actor:GetCurrentAnimationState())) end)
    Script.SetTimer(600,function()
        System.LogAlways('KCDUS|ENGINE|proxy-world-preserved|'..tostring(KCDUS.inWorldRaw())..'|'..tostring(player==original)..'|'..tostring(player.soul:GetId()==originalSoul))
    end)
end)
