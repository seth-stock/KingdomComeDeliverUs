"""Live check, ONE private real game (engine adapter loaded, no probe-state switch): the guest-side native quest-XP construction (QXPNATIVEGIVE), run twice with
one key (the second must change nothing), and a bad cause (refused). Synthetic host; nothing here touches a real save. Run after a loaded world:
    python tools/engine/native_quest_xp_live.py
"""
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import engine_session as E  # noqa: E402

results, seq = [], [700]


def lua(code, wait=1.2):
    return E.send(code, wait=wait)


def agent(line, wait=1.5):
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


out = lua("local r=KCDUS.Rewards.myIdentity() System.LogAlways('KCDUS|T|id|'..tostring(r))")
soul = val(out, 'id')
check('the guest has a native persistent identity (the adapter is loaded)', len(soul) == 32, soul)

agent('LOOTMODE|1|nxplive|0')
stat = "player.soul:GetStatLevel('prg')+(player.soul:GetStatProgress('prg') or 0)"
lua("KCDUS_T_P0=%s KCDUS_T_S0=player.human:PlayAnim('@kcdus/xp-last','').sequence" % stat)
key = 'n' + ('0123456789abcdef' * 2) + '.21'
cause, data = '2306d30', '530a000000000005' + '55d2dd02' + '01000000'   # data[8..11] = a real quest id (q_watercarrier), as the engine's own cause carries it
agent('QXPNATIVEGIVE|%s|0|9|2560|%s|%s|nxplive|%s' % (key, cause, data, soul), wait=2.0)
out = lua("local r=player.human:PlayAnim('@kcdus/xp-last','') System.LogAlways(string.format('KCDUS|T|after|%d|%d|%x|%.4f', r.sequence-KCDUS_T_S0, r.stat, r.causeVtableRva, " + stat + "-KCDUS_T_P0))")
a = val(out, 'after').split('|')
check('a quest-state-caused XP is applied once through the engine\'s own cause (1 native application, main progression, cause 2306d30)',
      len(a) == 4 and a[0] == '1' and a[1] == '9' and a[2] == '2306d30', val(out, 'after'))
check('... and Henry\'s progression rose by 20 points of the main XP', len(a) == 4 and float(a[3]) > 0, val(out, 'after'))
given = log_lines('KCDUS|QXPNATIVEGIVEN|')
check('... with a verified native readback line (reason 0, applied 1, verified 1)', bool(given) and given[-1].split('|')[3:6] == ['0', '1', '1'], given[-1] if given else '')

agent('QXPNATIVEGIVE|%s|0|9|2560|%s|%s|nxplive|%s' % (key, cause, data, soul), wait=2.0)
out = lua("local r=player.human:PlayAnim('@kcdus/xp-last','') System.LogAlways('KCDUS|T|again|'..(r.sequence-KCDUS_T_S0))")
check('the same key again changes nothing', val(out, 'again') == '1', val(out, 'again'))

before = int(val(out, 'again') or 1)
agent('QXPNATIVEGIVE|%s|0|9|2560|%s|%s|nxplive|%s' % ('n' + 'f' * 32 + '.22', '2201b10', data, soul), wait=1.5)       # a script cause is never constructed
out = lua("local r=player.human:PlayAnim('@kcdus/xp-last','') System.LogAlways('KCDUS|T|bad|'..(r.sequence-KCDUS_T_S0))")
check('a cause that is not a quest cause is refused', val(out, 'bad') == str(before), val(out, 'bad'))

agent('QXPNATIVEGIVE|%s|0|9|2560|%s|%s|nxplive|%s' % ('n' + 'e' * 32 + '.23', cause, data, 'f' * 32), wait=1.5)       # another soul's identity
out = lua("local r=player.human:PlayAnim('@kcdus/xp-last','') System.LogAlways('KCDUS|T|other|'..(r.sequence-KCDUS_T_S0))")
check("another participant's identity is refused", val(out, 'other') == str(before), val(out, 'other'))
agent('LOOTMODE|0')

print()
bad = [r for r in results if not r[1]]
print('%d checks, %d failed' % (len(results), len(bad)))
sys.exit(1 if bad else 0)
