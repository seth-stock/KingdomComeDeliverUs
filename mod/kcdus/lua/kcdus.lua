-- Kingdom Come: Deliver Us -- the mod's init script (the game runs Scripts/Mods/<modid>.lua once at start).
-- Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. Unofficial, free, not affiliated with Warhorse Studios or Deep Silver.
-- Modified version of the Kingdom Come: Together mod (https://github.com/DeepFriedDepp/KingdomCome-Together).
KCDUS = KCDUS or {}

local parts = { "version", "kcdus_quests", "core", "local", "native_identity", "locomotion", "outfits", "ghosts", "quests", "quest_mirror", "ui", "menu", "pause", "combat", "loot", "npcs", "rewards", "card", "capabilities", "boot" }
for _, name in ipairs(parts) do
    local path = "Scripts/Mods/kcdus/" .. name .. ".lua"
    local ok, err = pcall(Script.ReloadScript, path)
    if not ok then
        System.LogAlways("KCDUS|ERR|load|" .. name .. "|" .. tostring(err))
    end
end
