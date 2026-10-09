"""Owned private-session checks for production reward/ownership/preset paths; not human acceptance."""
from pathlib import Path
import engine_session as e
import time

e.verified()
root=Path(__file__).resolve().parents[2]
print(e.send("if KCDUS.Npcs then KCDUS.handlers.NPCMODE({'NPCMODE','0'}) end"),flush=True)
for module in ('npcs','rewards','weather'):
    print(e.run_file(root/'mod/kcdus/lua'/(module+'.lua')))
def lua(code,wait=1.5):
    wrapped="local ok,err=pcall(function() "+code+" end);if not ok then System.LogAlways('KCDUS|S5|error|'..tostring(err)) end"
    result=e.send(wrapped,wait)
    print(result,flush=True)
    if 'KCDUS|S5|error|' in result:raise RuntimeError(result)
    return result
out=lua("KCDUS.handlers.NPCMODE({'NPCMODE','1','s5','1'});KCDUS.handlers.NPCOWN2({'NPCOWN2','s5','0123456789abcdef0123456789abcdef','1','rat_pirk_guard4:2:5'});KCDUS.handlers.NPCSET2({'NPCSET2','s5','0123456789abcdef0123456789abcdef','2','2','rat_pirk_guard4,5,2526,522,81,0,0,0,0'});KCDUS.handlers.NPCSET2({'NPCSET2','s5','0123456789abcdef0123456789abcdef','2','1','rat_pirk_guard4,5,999,999,81,0,0,0,0'});System.LogAlways('KCDUS|S5|ordered|'..KCDUS.Npcs.puppets.rat_pirk_guard4.x);KCDUS.handlers.NPCOWN2({'NPCOWN2','s5','0123456789abcdef0123456789abcdef','2','rat_pirk_guard4:1:6'});System.LogAlways('KCDUS|S5|released|'..tostring(KCDUS.Npcs.puppets.rat_pirk_guard4==nil));KCDUS.handlers.NPCMODE({'NPCMODE','0'})")
assert 'NPCPUP|rat_pirk_guard4|on|2' in out
assert 'KCDUS|S5|ordered|2526' in out
assert 'KCDUS|S5|released|true' in out
coin='5ef63059-322e-4e1b-abe8-926e100c770e'
lua("KCDUS.handlers.LOOTMODE({'LOOTMODE','1','s5','0'});KCDUS.Rewards.beginApply('q_test');KCDUS.Rewards.appliedHere('q_test');KCDUS_S5_OLD_CREATE=ItemManager.CreateItem;ItemManager.CreateItem=function() return nil end")
try:
    out=lua("KCDUS.handlers.QRGIVE({'QRGIVE','q_test','12345678','"+coin+":1:1'})")
    assert 'QRUNVERIFIED|q_test|12345678' in out and 'QRGIVEN|' not in out
    out=lua("KCDUS.handlers.QRGIVE({'QRGIVE','q_test','12345678','"+coin+":1:1'});System.LogAlways('KCDUS|S5|reward-status|'..KCDUS.Rewards.given['q_test|12345678'])")
    assert 'KCDUS|S5|reward-status|uncertain' in out and 'QRGIVEN|' not in out
finally:
    lua("ItemManager.CreateItem=KCDUS_S5_OLD_CREATE;KCDUS.handlers.LOOTMODE({'LOOTMODE','0'})")
out=lua("KCDUS.handlers.WMODE({'WMODE','1','s5'});KCDUS.handlers.WAPPLY({'WAPPLY','s5','foggy_storm','30'})")
assert 'WEATHER|candidate|foggy_storm' in out
print('PASS ownership term/ordering/release and failed reward native readback; preset API accepted (visual/current-profile proof separate).')
