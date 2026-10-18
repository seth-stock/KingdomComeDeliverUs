-- ui: what the player sees: a notification, a chat line, a held prompt (the join-or-stay question).
local K = KCDUS
K.UI = { prompt = nil }
local U = K.UI

-- the game's notification banner (top centre, upper case): about 45 characters fit, a longer text is cut (seen in the retail game)
function U.banner(text)
    K.try("ui:notify", function() Game.ShowNotification(text) end)
end
function U.notify(text) U.say(text) end

function U.info(text, seconds)
    K.try("ui:info", function() Game.SendInfoText(text, true, nil, seconds or 6) end)
end

-- A short message is the game's notification banner; a longer one is the centred info line (the banner cuts it off). Both only draw while the
-- game window is in front (an unfocused game drops them: seen).
-- Messages wait in a queue, in order: while a menu is open nothing on the HUD can be seen, so they are held, and one that has waited longer
-- than MAX_AGE is dropped (they used to pop up long after the moment they were about, and two long ones in a row replaced each other).
U.SHORT = 44
U.MAX_AGE = 20
U.queue = {}
U.busyUntil = 0

function U.seconds(text) return math.min(15, math.max(6, math.floor(#text / 12))) end

function U.say(text)
    if text == "" then return end
    U.queue[#U.queue + 1] = { text = text, t = K.now() }
    U.pump(K.now())
end

function U.pump(now)
    while #U.queue > 0 and now - U.queue[1].t > U.MAX_AGE do table.remove(U.queue, 1) end
    if #U.queue == 0 or not K.inWorld() or (K.Pause and K.Pause.menuOpen) then return end
    local m = U.queue[1]
    if #m.text <= U.SHORT then
        table.remove(U.queue, 1)
        U.banner(m.text)
        return U.pump(now)   -- the game lines banners up itself
    end
    if U.prompt ~= nil or now < U.busyUntil then return end   -- the join-or-stay question holds the centre; one long line at a time
    table.remove(U.queue, 1)
    local secs = U.seconds(m.text)
    U.info(m.text, secs)
    U.busyUntil = now + secs
end

K.every(0.5, "notes", U.pump)

K.handlers["NOTE"] = function(f)
    U.say(K.clean(f[2] or ""))
end

K.handlers["CHAT"] = function(f)
    local text = f[3] or ""
    if #text > 160 then
        text = string.sub(text, 1, 160)
    end
    U.say(K.clean((f[2] or "?") .. ": " .. text))
end

-- INFO|text|seconds : a centred line. A prompt is re-sent every 2 s until INFOCLEAR so it stays up.
K.handlers["INFO"] = function(f)
    U.info(f[2] or "", K.num(f[3], 6))
end

K.handlers["PROMPT"] = function(f)
    U.prompt = { text = f[2] or "", untilT = K.now() + K.num(f[3], 30) }
    U.info(U.prompt.text, 3)
end

K.handlers["PROMPTCLEAR"] = function(f)
    U.prompt = nil
end

K.every(2.0, "prompt", function(now)
    local p = U.prompt
    if p == nil then
        return
    end
    if now > p.untilT then
        U.prompt = nil
        return
    end
    U.info(p.text, 3)
end)

-- the player types:  kcdus_say hello there
function KCDUS_Say(text)
    text = K.clean(text)
    if text == "" then
        return
    end
    K.out("CHAT", text)
    U.say("You: " .. text)
end

function KCDUS_Status()
    local n = K.Ghosts and K.Ghosts.count() or 0
    U.info(string.format("Deliver Us %s: %d player(s) in view, %d errors", K.VERSION or "?", n, K.errN), 5)
end
