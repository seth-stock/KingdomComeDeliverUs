if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
local original=player
local g=g_gameRules.game:SpawnPlayer(78,'kcdus_npc_channel_probe','NPC',player:GetWorldPos(),player:GetWorldAngles())
if not g or not g.actor or player~=original then error('Channel-backed NPC spawn invalid') end
KCDUS_ENGINE_NPC_CHANNEL=g
g:SetFlags(ENTITY_FLAG_NO_SAVE,0)
g:Event_MakeInvulnerable()
g:DisableBehaviorTreeEvaluation()
g.soul:RestrictDialog(true)
System.LogAlways('KCDUS|ENGINE|npc-channel|'..tostring(g.actor:GetChannel())..'|'..tostring(g.actor:IsPlayer()))
Script.SetTimer(500,function()
    g.human:PlayAnim('MotionMovement','walk')
    g.human:SetAnimMotionParam(0,1.5)
    Script.SetTimer(300,function()
        System.LogAlways('KCDUS|ENGINE|npc-channel-animation|'..tostring(g.actor:GetCurrentAnimationState()))
        System.LogAlways('KCDUS|ENGINE|npc-channel-world|'..tostring(KCDUS.inWorldRaw())..'|'..tostring(player==original))
    end)
end)
