"""Close only the UUID-marked probe PID after verifying it and unchanged real saves."""
import subprocess, json
import engine_session
s = engine_session.verified()
subprocess.run(['powershell', '-NoProfile', '-Command',
    f"$probeProcess = Get-CimInstance Win32_Process -Filter 'ProcessId={s['pid']}'; if (-not $probeProcess -or -not $probeProcess.CommandLine.Contains('{s['profile']}')) {{ throw 'Owned probe mismatch' }}; Stop-Process -Id {s['pid']}"], check=True)
if engine_session.hashes() != s['realSaveHashes']: raise RuntimeError('Real-save hashes changed')
s['pid'] = None
engine_session.CURRENT.write_text(json.dumps(s, indent=2))
print('Owned probe stopped; real saves unchanged:', len(s['realSaveHashes']))
