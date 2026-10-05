-- ui: what the player sees: a notification, a chat line, a held prompt (the join-or-stay question).
local K = KCDUS
K.UI = { prompt = nil }
local U = K.UI

function U.notify(text)
    K.try("ui:notify", function() Game.ShowNotification(text) end)
end

function U.info(text, seconds)
    K.try("ui:info", function() Game.SendInfoText(text, true, nil, seconds or 6) end)
end

K.handlers["NOTE"] = function(f)
    U.notify(K.clean(f[2] or ""))
end

K.handlers["CHAT"] = function(f)
    local text = f[3] or ""
    if #text > 160 then
        text = string.sub(text, 1, 160)
    end
    U.notify((f[2] or "?") .. ": " .. text)
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
    U.notify("You: " .. text)
end

function KCDUS_Status()
    local n = K.Ghosts and K.Ghosts.count() or 0
    U.info(string.format("Deliver Us %s: %d player(s) in view, %d errors", K.VERSION or "?", n, K.errN), 5)
end
