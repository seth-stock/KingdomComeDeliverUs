-- The engine, as little of it as the mod touches, for running the real mod Lua offline under MoonSharp.
-- The tests drive the clock (__advance), the world (__world) and read what the mod printed (__log).

__clock = 100.0
__timers = {}
__log = {}
__cmds = {}
__notes = {}
__infos = {}
__spawned = {}
__removed = {}
__nextId = 1000
__entities = {}
__calls = {}

__world = {
    started = false, loading = false,
    pos = { x = 10.0, y = 20.0, z = 30.0 }, ang = { x = 0, y = 0, z = 1.5 }, vel = { x = 1.0, y = 2.0, z = 0.0 },
    health = 100, stamina = 105, anim = "MotionIdle", worldTime = 36000,
    weapon = false, mounted = false, dialog = false, dead = false, danger = false,
    quests = {},   -- code -> { started=bool, completed=bool, objectives={ids} }
    failSpawn = false,
}

System = {}
function System.GetViewCameraPos()
    if __world.camera then return __world.camera end
    return { x = __world.pos.x + 1.0, y = __world.pos.y - 3.0, z = __world.pos.z + 1.7 }   -- behind the player, like the game camera
end
function System.LogAlways(s) __log[#__log + 1] = tostring(s) end
function System.GetCurrAsyncTime() return __clock end
function System.GetFrameTime() return 0.016 end
function System.AddCCommand(name, code, help) __cmds[name] = { code = code, help = help } end
function System.SpawnEntity(params)
    if __world.failSpawn then error("spawn refused") end
    __nextId = __nextId + 1
    local e = { id = __nextId, class = params.class, name = params.name,
                pos = { x = params.position.x, y = params.position.y, z = params.position.z }, angles = { x = 0, y = 0, z = 0 },
                flags = {} }
    function e:SetWorldPos(p) self.pos = { x = p.x, y = p.y, z = p.z }; self.moves = (self.moves or 0) + 1 end
    function e:SetWorldAngles(a) self.angles = { x = a.x, y = a.y, z = a.z } end
    function e:GetWorldPos() return self.pos end
    function e:Event_MakeInvulnerable() self.flags.invulnerable = true end
    function e:DisableBehaviorTreeEvaluation() self.flags.btOff = true end
    e.soul = { RestrictDialog = function(self, v) e.flags.noDialog = v end }
    e.actor = { EquipClothingPreset = function(self, g) e.flags.clothes = g end }
    __entities[e.id] = e
    __spawned[#__spawned + 1] = e
    return e
end
function System.GetEntity(id) return __entities[id] end
function System.RemoveEntity(id) __removed[#__removed + 1] = id; __entities[id] = nil end

Script = {}
function Script.SetTimer(ms, f) __timers[#__timers + 1] = { at = __clock + ms / 1000.0, f = f } end
function Script.ReloadScript(path) return __loadMod(path) end

CryAction = { IsGameStarted = function() return __world.started end }
Game = {
    IsLoadingEngineSaveGame = function() return __world.loading end,
    ShowNotification = function(t) __notes[#__notes + 1] = t end,
    SendInfoText = function(t, a, b, secs) __infos[#__infos + 1] = { text = t, secs = secs } end,
    SaveGameViaResting = function() __calls[#__calls + 1] = "SaveGameViaResting"; if __world.saveFails then error("no save") end end,
}
__globals = {}
Variables = {
    SetGlobal = function(k, v) __globals[k] = v end,
    GetGlobal = function(k) return __globals[k] end,
}
__actions = {}
UIAction = {
    StartAction = function(name, args) if __world.actionFails then error("no such action") end __actions[#__actions + 1] = name end,
}
Calendar = {
    GetWorldTime = function() return __world.worldTime end,
    SetWorldTime = function(t) __world.worldTime = t; __calls[#__calls + 1] = "SetWorldTime:" .. tostring(t) end,
}
AIPARAM_INVISIBLE = 77
AI = {
    SetBehaviorTreeEvaluationEnabled = function(id, v) __calls[#__calls + 1] = "bt:" .. tostring(id) .. ":" .. tostring(v) end,
    ChangeParameter = function(id, p, v) __calls[#__calls + 1] = "param:" .. tostring(id) .. ":" .. tostring(p) .. ":" .. tostring(v) end,
}
QuestSystem = {
    IsQuestStarted = function(c) local q = __world.quests[c]; return q ~= nil and q.started or false end,
    IsQuestCompleted = function(c) local q = __world.quests[c]; return q ~= nil and q.completed or false end,
    GetActiveObjectives = function(c)
        local q = __world.quests[c]
        local out = {}
        if q and q.objectives then for i, id in ipairs(q.objectives) do out[i - 1] = id end end
        return out
    end,
}

player = {
    id = 1,
    GetWorldPos = function(self) return __world.pos end,
    GetWorldAngles = function(self) return __world.ang end,
    GetVelocity = function(self, v) v.x, v.y, v.z = __world.vel.x, __world.vel.y, __world.vel.z end,
    IsDead = function(self) return __world.dead end,
    soul = {
        GetState = function(self, k) if k == "health" then return __world.health end return __world.stamina end,
        IsInCombatDanger = function(self) return __world.danger end,
        GetStatLevel = function(self, n) local v = __henry.stats[n]; if v == nil then error("no stat " .. tostring(n)) end return v end,
        GetSkillLevel = function(self, n) local v = __henry.skills[n]; if v == nil then error("no skill " .. tostring(n)) end return v end,
        AdvanceToStatLevel = function(self, n, v) if v > __henry.stats[n] then __henry.stats[n] = v end end,
        AdvanceToSkillLevel = function(self, n, v) if v > __henry.skills[n] then __henry.skills[n] = v end end,
    },
    inventory = {
        GetInventoryTable = function(self) local t = {}; for i, it in ipairs(__henry.items) do t[i - 1] = it end return t end,
        GetCountOfClass = function(self, c) local n = 0; for _, it in ipairs(__henry.items) do if it.class == c then n = n + it.amount end end return n end,
        AddItem = function(self, it)
            -- a stack of the same class and health merges; a non-stackable one (class prefix "sword") ignores the amount, like the real game
            __henry.items[#__henry.items + 1] = it
        end,
    },
    actor = { GetCurrentAnimationState = function(self) return __world.anim end },
    human = {
        IsWeaponDrawn = function(self) return __world.weapon end,
        IsMounted = function(self) return __world.mounted end,
        IsInDialog = function(self) return __world.dialog end,
    },
}

-- the player's Henry as the card reads it: levels, and things (class, health, amount)
__henry = {
    stats = { str = 1, agi = 1, vit = 1, spc = 3 },
    skills = { fencing = 0, weapon_sword = 0, stealth = 0, alchemy = 0 },
    items = { { class = "aaaaaaaa-0000-0000-0000-000000000001", health = 1, amount = 1 } },
}
ItemManager = {
    GetItem = function(id) return id end,
    CreateItem = function(class, health, amount)
        if string.sub(class, 1, 8) == "bbbbbbbb" then amount = 1 end   -- a sword: the amount is ignored
        return { class = class, health = health, amount = amount }
    end,
}

Player = {}
function Player.OnLoad(self) __calls[#__calls + 1] = "Player.OnLoad" end
function Player.OnReset(self) end
function Player.OnSpawn(self) end
function Player.OnInit(self) end
function Player.OnResetLoad(self) end

-- the engine drops every script timer when a save is loaded
function __dropTimers() __timers = {} end

-- run the timers due within dt seconds, in order, moving the clock as the engine would
function __advance(dt)
    local target = __clock + dt
    while true do
        local bi, b = nil, nil
        for i, t in ipairs(__timers) do
            if t.at <= target and (b == nil or t.at < b.at) then bi, b = i, t end
        end
        if not b then break end
        table.remove(__timers, bi)
        if b.at > __clock then __clock = b.at end
        b.f()
    end
    __clock = target
end

function __count(prefix)
    local n = 0
    for _, l in ipairs(__log) do if string.sub(l, 1, #prefix) == prefix then n = n + 1 end end
    return n
end
function __lines(prefix)
    local out = {}
    for _, l in ipairs(__log) do if string.sub(l, 1, #prefix) == prefix then out[#out + 1] = l end end
    return out
end
