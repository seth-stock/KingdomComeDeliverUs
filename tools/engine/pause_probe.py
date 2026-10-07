"""Temporary t_scale observation in the verified private KCD1 profile; always restore it."""
import engine_session as session
session.verified()
print(session.send("assert(KCDUS.inWorldRaw()); assert(tonumber(System.GetCVar('sys_useSteamCloudForPlatformSaving'))==0); KCDUS_PAUSE_PROBE_ORIGINAL=System.GetCVar('t_scale'); System.LogAlways('KCDUS-PROBE|pause-before|'..tostring(Calendar.GetWorldTime())..'|'..tostring(KCDUS_PAUSE_PROBE_ORIGINAL)); System.LogAlways('KCDUS-PROBE|set-cvar|'..type(System.SetCVar))"))
try:
    print(session.send("assert(type(System.SetCVar)=='function'); System.SetCVar('t_scale',0); System.LogAlways('KCDUS-PROBE|pause-set|'..tostring(System.GetCVar('t_scale'))..'|'..tostring(Calendar.GetWorldTime()))", 2))
    print(session.send("System.LogAlways('KCDUS-PROBE|pause-after|'..tostring(Calendar.GetWorldTime())..'|'..tostring(System.GetCVar('t_scale')))"))
finally:
    print(session.send("System.SetCVar('t_scale',tonumber(KCDUS_PAUSE_PROBE_ORIGINAL) or 1); System.LogAlways('KCDUS-PROBE|pause-restored|'..tostring(System.GetCVar('t_scale')))"))
