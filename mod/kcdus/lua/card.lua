-- card: a Henry, read out of the game and put back (docs/SHARED-WORLDS.md). The agent keeps the card of the player's Henry and, when a friend's world
-- replaces theirs, asks the game to put the card onto the Henry of the world that loads.
--   agent -> game: CARDGET|<id>                         the game answers with KCDUS|CARD|<id>|<i>|<n>|<text> lines (n pieces)
--   agent -> game: CARDSET|<id>|<i>|<n>|<text>          the pieces of a card; when all are here the card is applied
-- The card is text:  t:<stat>=<level>;s:<skill>=<level>;i:<class>,<health>,<amount>;...  (no '|' and no '~': it travels inside records).
-- What it can and cannot do (the game's own API): levels only go UP (AdvanceTo...Level cannot lower), so a card never makes a stronger Henry weaker;
-- things are ADDED (what the card holds and the Henry lacks, money included), never taken away; perks cannot be read from the game and are not carried.
local K = KCDUS

local STATS = { "str", "agi", "vit", "spc" }
local SKILLS = { "stealth", "horse_riding", "fencing", "bard", "lockpicking", "pickpocketing", "alchemy", "cooking", "repairing", "smithing", "fishing", "mining",
    "first_aid", "drinking", "hunter", "defense", "weapon_sword", "weapon_axe", "weapon_bow", "weapon_crossbow", "weapon_shield", "weapon_mace", "weapon_dagger",
    "weapon_large", "weapon_unarmed", "herbalism", "reading", "tailoring", "armourer", "weaponsmithing", "shoemaking", "gambling", "houndmaster" }
local PIECE = 380
local MONEY = "5ef63059-322e-4e1b-abe8-926e100c770e"   -- the item class of groschen (ten of them per displayed coin: GetMoney() * 10)

local function num(v) return string.format("%g", v) end

-- the card of the player's Henry as text
function K.card_build()
    local s, inv = player.soul, player.inventory
    local parts = {}
    for _, n in ipairs(STATS) do
        local ok, v = pcall(s.GetStatLevel, s, n)
        if ok and type(v) == "number" and v > 0 then parts[#parts + 1] = "t:" .. n .. "=" .. num(v) end
    end
    for _, n in ipairs(SKILLS) do
        local ok, v = pcall(s.GetSkillLevel, s, n)
        if ok and type(v) == "number" and v > 0 then parts[#parts + 1] = "s:" .. n .. "=" .. num(v) end
    end
    -- the same thing at the same condition is one entry
    local seen, order = {}, {}
    for _, id in pairs(inv:GetInventoryTable()) do
        local it = ItemManager.GetItem(id)
        if it and it.class then
            local key = it.class .. "," .. string.format("%.2f", it.health or 1)
            if not seen[key] then seen[key] = { class = it.class, health = it.health or 1, amount = 0 }; order[#order + 1] = key end
            seen[key].amount = seen[key].amount + (it.amount or 1)
        end
    end
    for _, key in ipairs(order) do
        local e = seen[key]
        parts[#parts + 1] = "i:" .. e.class .. "," .. string.format("%.2f", e.health) .. "," .. num(e.amount)
    end
    return table.concat(parts, ";")
end

-- put a card onto the player's Henry; returns how many levels were raised and how many things were added
function K.card_apply(card)
    local s, inv = player.soul, player.inventory
    local raised, added, failed = 0, 0, 0
    -- the things first, so a failure there does not stop the levels
    local wants = {}
    for entry in string.gmatch(card, "[^;]+") do
        local kind, rest = string.match(entry, "^(%a):(.*)$")
        if kind == "t" or kind == "s" then
            local name, level = string.match(rest, "^([%w_]+)=([%d%.]+)$")
            level = tonumber(level)
            if name and level then
                local get, set = s.GetStatLevel, s.AdvanceToStatLevel
                if kind == "s" then get, set = s.GetSkillLevel, s.AdvanceToSkillLevel end
                local ok, cur = pcall(get, s, name)
                if ok and type(cur) == "number" and level > cur then
                    local ok2 = pcall(set, s, name, level)
                    if ok2 then raised = raised + 1 else failed = failed + 1 end
                end
            end
        elseif kind == "i" then
            local class, health, amount = string.match(rest, "^([%x%-]+),([%d%.]+),([%d%.]+)$")
            amount = tonumber(amount)
            if class and amount then
                wants[class] = wants[class] or { health = tonumber(health) or 1, amount = 0 }
                wants[class].amount = wants[class].amount + amount
            end
        end
    end
    for class, w in pairs(wants) do
        local ok, have = pcall(inv.GetCountOfClass, inv, class)
        have = (ok and tonumber(have)) or 0
        if w.amount > have then
            local need = w.amount - have
            -- money and big stacks (arrows) are one call; a few single things (a sword) are made one by one: CreateItem's amount is ignored for them (seen)
            local ok2 = pcall(function()
                if class == MONEY or need > 10 then
                    inv:AddItem(ItemManager.CreateItem(class, w.health, need))
                else
                    for _ = 1, math.floor(need + 0.5) do inv:AddItem(ItemManager.CreateItem(class, w.health, 1)) end
                end
            end)
            if ok2 then added = added + 1 else failed = failed + 1 end
        end
    end
    return raised, added, failed
end

K.handlers["CARDGET"] = function(f)
    local id = K.clean(f[2] or "x")
    local ok, card = pcall(K.card_build)
    if not ok then K.out("ERR", "card", K.clean(card)); return end
    local n = math.max(1, math.ceil(#card / PIECE))
    for i = 0, n - 1 do K.out("CARD", id, i, n, string.sub(card, i * PIECE + 1, (i + 1) * PIECE)) end
end

local incoming = {}
K.handlers["CARDSET"] = function(f)
    local id = K.clean(f[2] or "x")
    local i, n = math.floor(K.num(f[3], 0)), math.floor(K.num(f[4], 0))
    if n < 1 or n > 400 or i < 0 or i >= n then return end
    local e = incoming[id]
    if not e or e.n ~= n then e = { n = n, got = 0, parts = {} }; incoming[id] = e end
    if not e.parts[i + 1] then e.got = e.got + 1 end
    e.parts[i + 1] = f[5] or ""
    if e.got >= n then
        incoming[id] = nil
        local card = table.concat(e.parts, "", 1, n)
        local ok, raised, added, failed = pcall(K.card_apply, card)
        if ok then K.out("CARDSET", id, raised, added, failed) else K.out("ERR", "cardset", K.clean(raised)) end
    end
end
