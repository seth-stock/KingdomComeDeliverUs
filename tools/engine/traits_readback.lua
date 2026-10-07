if not KCDUS.inWorldRaw() then error('Loaded disposable world required') end
for _,name in ipairs({'str','agi','vit','spc'}) do
    System.LogAlways('KCDUS|ENGINE|trait-stat|'..name..'|'..tostring(player.soul:GetStatLevel(name)))
end
for _,name in ipairs({'reading','weapon_sword','stealth','herbalism'}) do
    System.LogAlways('KCDUS|ENGINE|trait-skill|'..name..'|'..tostring(player.soul:GetSkillLevel(name))..'|'..tostring(player.soul:GetSkillProgress(name)))
end
System.LogAlways('KCDUS|ENGINE|trait-world-time|'..tostring(Calendar.GetWorldTime()))
