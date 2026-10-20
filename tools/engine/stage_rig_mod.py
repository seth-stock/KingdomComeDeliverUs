"""Stage the mod build under test for the two-game rig: the installed Mods/kcdus (with its menu pak) plus the freshly built kcdus.pak.
    python tools/engine/stage_rig_mod.py        (run tools/Build-Pak.py --out _work/mod-build first)
The rig then starts its private games from _work/rig-mod/kcdus (engine_session KCDUS_PROBE_MOD); the installed mod is never touched."""
import shutil
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
GAME = Path(r'F:\SteamLibrary\steamapps\common\KingdomComeDeliverance')
dst = ROOT / '_work' / 'rig-mod' / 'kcdus'
if dst.exists(): shutil.rmtree(dst)
shutil.copytree(GAME / 'Mods' / 'kcdus', dst)
shutil.copy2(ROOT / '_work' / 'mod-build' / 'kcdus' / 'Data' / 'kcdus.pak', dst / 'Data' / 'kcdus.pak')
print('staged', dst)
