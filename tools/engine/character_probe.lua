local function out(name, fn)
    local ok, value = pcall(fn)
    System.LogAlways('KCDUS|ENGINE|'..name..'|'..tostring(ok)..'|'..tostring(value))
    if ok and type(value)=='table' then
        for key, entry in pairs(value) do System.LogAlways('KCDUS|ENGINE|'..name..'-field|'..tostring(key)..'='..tostring(entry)) end
    end
end
out('soul-id', function() return player.soul:GetId() end)
out('archetype', function() return player.soul:GetArchetype() end)
out('skill-before', function() return player.soul:GetSkillLevel('reading') end)
out('progress-before', function() return player.soul:GetSkillProgress('reading') end)
out('xp-negative', function() return player.soul:AddSkillXP('reading', -100000) end)
out('skill-after', function() return player.soul:GetSkillLevel('reading') end)
out('progress-after', function() return player.soul:GetSkillProgress('reading') end)
out('clothing', function() return player.actor:GetInitialClothingPreset() end)
out('role', function() return player.soul:GetRoles() end)
