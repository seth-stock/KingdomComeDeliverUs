-- menu: the Multiplayer tab's side of the conversation (docs/MENU.md). The game's menus are flow graphs; the mod's pak adds a "Multiplayer" page
-- (built at install time from the player's own GameData.pak, dotnet/KcdUs.Agent/Ui). A button on that page runs one line of Lua:
--     KCDUS_Menu('verb', 'arg')        -> KCDUS|MENU|verb|arg  in kcd.log, which the agent reads and acts on (host, join, leave, status, ...)
-- and the agent can ask the game to load a save by sending  LOAD|<list index>|<playline>  (the stock LoadSavedGame node, via the MP_Load graph).
local K = KCDUS

-- the menu page's buttons call this global; anything the player cannot see should be safe to repeat
function KCDUS_Menu(verb, arg)
    K.out("MENU", K.clean(verb), K.clean(arg))
end

-- agent -> game: MENUTEXT|<code>  the line at the top of the open page (what the agent is doing). A flow-graph variable is a number only, so
-- the agent sends a code and the pak holds one tiny graph per code, MP_Title<code> (MenuUi.Title).
K.handlers["MENUTEXT"] = function(f)
    local code = math.floor(K.num(f[2], 0))
    pcall(UIAction.StartAction, "MP_Title" .. code, {})
end

-- agent -> game: LOAD|<index in the playline's list, 0 = newest>|<playline number>
-- SaveId is the position in the engine's list of the playline's saves (newest first), not the file's number.
K.handlers["LOAD"] = function(f)
    local index = math.floor(K.num(f[2], 0))
    local playline = math.floor(K.num(f[3], 0))
    Variables.SetGlobal("KCDUS_LoadSaveId", index)
    Variables.SetGlobal("KCDUS_LoadPlayLine", playline)
    local ok, err = pcall(UIAction.StartAction, "MP_Load", {})
    K.out("LOADING", index, playline, ok and 1 or 0, ok and "" or K.clean(err))
end

-- agent -> game: ask the engine to write an autosave now (the retail game's QuickSave is a stub; this is the one that writes a file)
K.handlers["SAVEWORLD"] = function(f)
    if not K.inWorldRaw() then K.out('SAVEWORLD',f[2] or '',0,'world not ready');return end
    if f[3]=='character' then
        -- Drawn weapons/mounts/dialogue can leave references outside the native
        -- inventory record. Do not move those references into another world.
        local state=K.Local.sample()
        if state.flags~=0 then K.out('SAVEWORLD',f[2] or '',0,'holster weapons and finish combat, riding or dialogue first');return end
    end
    if K.Loot then pcall(K.Loot.settle, 0) end   -- no unconfirmed take goes into a save
    local ok, err = pcall(Game.SaveGameViaResting)
    K.out("SAVEWORLD", f[2] or "", ok and 1 or 0, ok and "" or K.clean(err))
end
