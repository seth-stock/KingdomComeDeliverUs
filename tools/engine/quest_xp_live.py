"""Live check of causal quest/XP consequences in ONE private real game with the engine adapter loaded WITHOUT the private probe switches (normal play):
the adapter's XP observation (@kcdus/xp-since), the cause it records for a mirrored objective's reward, the host's forwarding rule (only quest-state-caused
XP leaves), and a guest applying a forwarded quest XP once. The second player is synthetic. Run after launch with KCDUS_PROBE_BRIDGE=1 and a loaded world.
    python tools/engine/quest_xp_live.py
"""
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import engine_session as E  # noqa: E402

results = []
seq = [300]


def lua(code, wait=1.2):
    return E.send(code, wait=wait)


def agent(line, wait=1.0):
    seq[0] += 1
    return lua("KCDUS_In('%d~%s')" % (seq[0], line), wait=wait)


def check(name, ok, detail=''):
    results.append((name, bool(ok), detail))
    print(('PASS ' if ok else 'FAIL ') + name + (('  [' + detail + ']') if detail else ''))


def val(out, tag):
    m = re.findall(r'KCDUS\|T\|' + tag + r'\|([^\r\n]*)', out)
    return m[-1] if m else ''


def log_lines(prefix):
    return [l for l in Path(E.log_path()).read_bytes().decode('utf-8', 'replace').splitlines() if l.startswith(prefix)]


out = lua("local r=player.human:PlayAnim('@kcdus/xp-since/0','') System.LogAlways('KCDUS|T|since|'..type(r)..'|'..tostring(type(r)=='table' and r.last))")
check('the XP observation is available in normal play (no private probe switches)', val(out, 'since').startswith('table|'), val(out, 'since'))

# a mirrored objective's native reward: the observation carries the objective cause and its quest data
lua("KCDUS_T_S0=player.human:PlayAnim('@kcdus/xp-since/0','').last")
out = lua("local q,o='q_watercarrier','bribedByTereza' local was=QuestSystem.IsObjectiveCompleted(q,o) local ok=pcall(QuestSystem.CompleteObjective,q,o,false) "
          "System.LogAlways('KCDUS|T|obj|'..tostring(was)..'|'..tostring(ok)..'|'..tostring(QuestSystem.IsObjectiveCompleted(q,o)))", wait=2.0)
check('a rewarded objective completed by script (as the mirror does)', val(out, 'obj').endswith('|true|true'), val(out, 'obj'))
out = lua("local r=player.human:PlayAnim('@kcdus/xp-since/'..KCDUS_T_S0,'') local s='' for i=1,r.n do s=s..r['e'..i]..';' end System.LogAlways('KCDUS|T|rows|'..r.n..'|'..s)")
rows = val(out, 'rows')
objective_rows = [r for r in rows.split('|', 1)[-1].split(';') if r and r.split(',')[7] == '21c8fd0']
check("its native XP rewards are observed with the quest-objective cause", len(objective_rows) >= 1, rows[:200])
check('... and the cause carries the quest identity (24 data bytes)', all(len(r.split(',')[9]) == 48 for r in objective_rows) and objective_rows != [], ' '.join(r.split(',')[9] for r in objective_rows))

# the host forwards only quest-state-caused XP: an objective's XP stays (the guest's mirror pays it natively)
agent('LOOTMODE|1|livexp|1')
lua("KCDUS.Rewards.xpSeq=nil", wait=1.0)
lua("System.LogAlways('KCDUS|T|wait')", wait=1.0)
before = len(log_lines('KCDUS|QXP|'))
lua("player.soul:AddStatXP('str',1)", wait=1.5)                         # a script-caused award: never forwarded
out = lua("System.LogAlways('KCDUS|T|seq|'..tostring(KCDUS.Rewards.xpSeq))", wait=1.0)
check('the host drains the observation and forwards no script- or objective-caused XP', len(log_lines('KCDUS|QXP|')) == before and val(out, 'seq') not in ('', 'nil'), val(out, 'seq'))

# a guest applies a forwarded quest XP once, by stat and native amount, and reads it back
agent('LOOTMODE|1|livexp|0')
lua("KCDUS_T_P0=player.soul:GetStatLevel('prg')+(player.soul:GetStatProgress('prg') or 0) KCDUS_T_X0=player.human:PlayAnim('@kcdus/xp-last','').sequence")
agent('QXPGIVE|xa1b2c31|0|9|2560', wait=1.5)
agent('QXPGIVE|xa1b2c31|0|9|2560', wait=1.5)
out = lua("local r=player.human:PlayAnim('@kcdus/xp-last','') System.LogAlways(string.format('KCDUS|T|gx|%d|%d|%s|%.4f', r.sequence-KCDUS_T_X0, r.stat, r.requestedRaw, "
          "player.soul:GetStatLevel('prg')+(player.soul:GetStatProgress('prg') or 0)-KCDUS_T_P0))")
g = val(out, 'gx').split('|')
check("a guest applies the host's quest XP once (one native application, main progression, 2560 raw = 20 points)",
      len(g) == 4 and g[0] == '1' and g[1] == '9' and g[2] == '000a0000', val(out, 'gx'))
check('... with a verified readback line', any(l.startswith('KCDUS|QXPGIVEN|xa1b2c31|prg|1|1') for l in log_lines('KCDUS|QXPGIVEN|')), '')
agent('LOOTMODE|0')

print()
bad = [r for r in results if not r[1]]
print('%d checks, %d failed' % (len(results), len(bad)))
sys.exit(1 if bad else 0)
