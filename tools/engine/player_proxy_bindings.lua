for _,name in ipairs({'SpawnPlayer','CreatePlayer','RevivePlayer','RemoveActor','GetPlayerByChannelId','GetPlayers'}) do
    local ok,value=pcall(function() return g_gameRules.game[name] end)
    System.LogAlways('KCDUS|ENGINE|gamerules-binding|'..name..'|'..tostring(ok)..'|'..type(value))
end
for _,ent in ipairs({player,KCDUS_ENGINE_GHOST}) do
    System.LogAlways('KCDUS|ENGINE|actor-channel|'..tostring(ent.id)..'|'..tostring(ent.actor:GetChannel())..'|'..tostring(ent.actor:IsPlayer()))
end
