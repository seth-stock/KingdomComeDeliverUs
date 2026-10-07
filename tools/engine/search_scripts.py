"""Print local game-script call sites for development; never package extracted assets."""
import importlib.util
from pathlib import Path
import sys
import zipfile
spec = importlib.util.spec_from_file_location('inspect_pak', Path(__file__).parents[1] / 'Inspect-EngineCapabilities.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
game = Path(sys.argv[1])
terms = sys.argv[2:]
context=2
for arg in list(terms):
    if arg.startswith('--context='): context=int(arg.split('=')[1]);terms.remove(arg)
for pak in ('Scripts.pak', 'GameData.pak'):
    with zipfile.ZipFile(game / 'Data' / pak) as archive:
        for entry in archive.infolist():
            if not entry.filename.lower().endswith('.lua'):
                continue
            lines = module.read_entry(archive, entry).decode('utf-8', 'replace').splitlines()
            for i, line in enumerate(lines):
                if any(term in line for term in terms):
                    print(entry.filename, i + 1)
                    print('\n'.join(lines[max(0, i-context):i+context+1]))
