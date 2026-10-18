-- menu: the Multiplayer tab's side of the conversation (docs/MENU.md). The game's menus are flow graphs; the mod's pak adds a "Multiplayer" page
-- (built at install time from the player's own GameData.pak, dotnet/KcdUs.Agent/Ui). A button on that page runs one line of Lua:
--     KCDUS_Menu('verb', 'arg')        -> KCDUS|MENU|verb|arg  in kcd.log, which the agent reads and acts on (host, join, leave, status, ...)
-- and the agent can ask the game to load a save by sending  LOAD|<list index>|<playline>  (the stock LoadSavedGame node, via the MP_Load graph).
local K = KCDUS

-- Which Multiplayer page is open: "main", "sub" or nil. The agent's status lines go into the main page's title only (elsewhere they were drawn over the
-- game's own main menu: seen); answers and boxes go to any open page, and with no page open a message is the centred line in the world instead.
K.MenuPage = { page = nil }
function K.MenuPage.close() K.MenuPage.page = nil end

-- the menu page's buttons call this global; anything the player cannot see should be safe to repeat
function KCDUS_Menu(verb, arg)
    verb = K.clean(verb)
    if verb == "page" then K.MenuPage.page = "main" elseif K.MenuPage.page == nil then K.MenuPage.page = "sub" end
    K.out("MENU", verb, K.clean(arg))
end

-- a sub-page opened (More options, Story and keys, ...) / any page's Back was pressed
function KCDUS_PageOpened(which) K.MenuPage.page = which or "sub" end
function KCDUS_PageClosed() K.MenuPage.page = nil end

-- agent -> game: MSG|<text>  something the player must read: a box with OK in the open page, the centred line in the world
K.handlers["MSG"] = function(f)
    local text = K.clean(f[2] or "")
    if K.MenuPage.page ~= nil then
        local ok, started = pcall(UIAction.StartAction, "MP_Msg", { text })
        if ok and started ~= false then return end
    end
    if K.UI then K.UI.say(text) end   -- the world's queue: shown in order, when no menu is open
end

-- agent -> game: ASK|<text>  a yes/no question in the open page; Yes runs KCDUS_Menu('confirm','yes'). With no page open it is said, and Join again answers it.
K.handlers["ASK"] = function(f)
    local text = K.clean(f[2] or "")
    if K.MenuPage.page ~= nil then
        local ok, started = pcall(UIAction.StartAction, "MP_Msg", { text })   -- a Yes/No box never reported its answer: Join again says yes
        if ok and started ~= false then return end
    end
    if K.UI then K.UI.say(text) end   -- the world's queue: shown in order, when no menu is open
end

-- agent -> game: MENUTEXT|<code>|<text>  the line at the top of the open page (what the agent is doing). MP_Text takes the words as the action's
-- argument (the Start node's Args port, as the game's own help screen takes its page); a pak built before MP_Text has one graph per code, MP_Title<code>.
K.handlers["MENUTEXT"] = function(f)
    local code = math.floor(K.num(f[2], -1))
    local text = f[3]
    -- a status line (code -1) belongs to the main page; an answer to a button (a code) to the page that is open; nothing without a page
    if K.MenuPage.page == nil or (code < 0 and K.MenuPage.page ~= "main") then
        return
    end
    if text ~= nil and text ~= "" then
        local ok, started = pcall(UIAction.StartAction, "MP_Text", { text })
        if ok and started ~= false then
            return
        end
    end
    if code >= 0 then
        pcall(UIAction.StartAction, "MP_Title" .. code, {})
    end
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
    if K.Npcs then pcall(K.Npcs.releaseAll, 'save') end   -- no puppet (brain off) goes into a save
    local ok, err = pcall(Game.SaveGameViaResting)
    K.out("SAVEWORLD", f[2] or "", ok and 1 or 0, ok and "" or K.clean(err))
end
