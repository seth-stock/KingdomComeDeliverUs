-- boot: register the console commands, announce ourselves, start ticking.
local K = KCDUS

local function command(name, code, help)
    local ok, err = pcall(System.AddCCommand, name, code, help)
    if not ok then
        K.out("ERR", "command", name, K.clean(err))
    end
end

command("kcdus", "KCDUS_In(%line)", "Kingdom Come: Deliver Us - the agent's channel (do not type)")
command("kcdus_say", "KCDUS_Say(%line)", "Say something to your co-op partners")
command("kcdus_status", "KCDUS_Status()", "Show the co-op status")
command("kcdus_capabilities", "KCDUS.capabilities()", "Read-only binding candidates; does not verify or change character state")
command("kcdus_quest_mode", "KCDUS_QuestMode(%line)", "Candidate one-way quest mirroring, default off: candidate|off")
function KCDUS_QuestMode(mode)
    if mode == 'candidate' or mode == 'off' then
        K.handlers.QMODE({'QMODE', mode, K.QuestMirror.scope})
    else K.log('Quest mirroring is Candidate. Use kcdus_quest_mode candidate|off.') end
end
command("kcdus_join", "KCDUS.out('KEY', 'join')", "Join your host in the story stretch you were asked about")
command("kcdus_stay", "KCDUS.out('KEY', 'stay')", "Stay in the open world instead of joining your host")
command("kcdus_off", "KCDUS.stop()", "Stop the co-op mod's update loop")
command("kcdus_on", "KCDUS.start()", "Start the co-op mod's update loop")

K.handlers["HELLO?"] = function(f)
    K.out("HELLO", K.VERSION or "0", K.PROTO or 0, K.LUA_BUILD or "")
    K.out("READY", K.inWorldNow and 1 or 0)
end

K.out("HELLO", K.VERSION or "0", K.PROTO or 0, K.LUA_BUILD or "")
K.hookPlayer()
K.start()
