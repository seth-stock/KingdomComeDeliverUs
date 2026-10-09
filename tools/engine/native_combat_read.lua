-- Read-only native actor/controller check. Owned private engine only.
if not KCDUS.inWorldRaw() then error('Loaded private world required') end
local function read(e)
    local value=e.human:PlayAnim('@kcdus/combat-read','')
    if type(value)~='table' or value.version~=1 then error('Native combat reader not installed') end
    System.LogAlways(string.format('KCDUS|S6|combat-read|%s|actor=%d|channel-guard=%d|input=%d|animation-ready=%d|actor-vt=%x|animation-vt=%x',
        e:GetName(),value.actorPresent,value.channelGuard,value.inputSupported,value.animationReady,value.actorVtableRva,value.animationVtableRva))
    return value
end
local hero=read(player)
if hero.actorPresent~=1 or hero.inputSupported~=1 then error('Player native input route not confirmed') end
local seen=0
for _,e in pairs(System.GetEntitiesInSphere(player:GetWorldPos(),30)) do
    if e~=player and e.human and e.soul and string.sub(e.class or '',1,3)=='NPC' and seen<8 then
        local native=read(e);seen=seen+1
        if native.inputSupported~=0 then error('Unexpected NPC input route; investigate before enabling') end
    end
end
if seen==0 then error('No ordinary NPCs inspected') end
System.LogAlways('KCDUS|S6|combat-read|PASS|'..seen)
