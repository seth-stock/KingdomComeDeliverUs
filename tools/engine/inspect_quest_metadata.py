"""Read only five objective metadata rows locally; never extract game assets."""
import importlib.util
from pathlib import Path
spec = importlib.util.spec_from_file_location('catalog', Path(__file__).parents[1] / 'Build-QuestCatalog.py')
catalog = importlib.util.module_from_spec(spec)
spec.loader.exec_module(catalog)
game = catalog.find_game_dir(None)
rows = catalog.table_rows(catalog.read_member(str(Path(game) / 'Data' / 'Tables.pak'), 'Libs/Tables/quest/quest_objective.xml'))
print(rows[:5])
