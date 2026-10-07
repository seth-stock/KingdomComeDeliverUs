if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
local g=KCDUS_ENGINE_REMOTE
if not g then error('No owned player proxy') end
System.LogAlways('KCDUS|ENGINE|no-save-constant|'..tostring(ENTITY_FLAG_NO_SAVE))
if type(ENTITY_FLAG_NO_SAVE)~='number' then error('Cannot prove nonpersistent entity flag') end
g:SetFlags(ENTITY_FLAG_NO_SAVE,0)
System.LogAlways('KCDUS|ENGINE|proxy-no-save|'..tostring(g:HasFlags(ENTITY_FLAG_NO_SAVE)))
System.LogAlways('KCDUS|ENGINE|proxy-soul-separate|'..tostring(g.soul and g.soul:GetId()~=player.soul:GetId()))
g.human:StopAnim()
Script.SetTimer(300,function() System.LogAlways('KCDUS|ENGINE|proxy-stopped|'..tostring(g.actor:GetCurrentAnimationState())) end)
