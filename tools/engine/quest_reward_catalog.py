"""Private, read-only: the installed game's per-objective quest rewards (Libs/Tables/quest/quest_reward_*), as a Lua table for engine probes.
Reads the user's own installed Tables.pak at run time; writes only under the ignored _work folder. Never publish its output.
    python tools/engine/quest_reward_catalog.py <game dir> <out.lua>
"""
import importlib.util
import sys
import zipfile
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

spec = importlib.util.spec_from_file_location('pak', Path(__file__).parents[1] / 'Inspect-EngineCapabilities.py')
pak = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pak)


def load(game):
    z = zipfile.ZipFile(Path(game) / 'Data' / 'Tables.pak')

    def rows(name):
        e = [i for i in z.infolist() if i.filename.lower().endswith('/' + name + '.xml')][0]
        t = ET.fromstring(pak.read_entry(z, e))
        return [r.attrib for r in t.iter() if r.tag not in ('database', 'table', 'column', 'columns', 'rows') and r.attrib]

    quest = {r['quest_id']: r['quest_name'] for r in rows('quest')}
    objective = {(r['quest_id'], r['objective_id']): r['objective_name'] for r in rows('quest_objective')}
    exp = {r['exp_change_id']: float(r['change']) for r in rows('exp_change')}
    money = {r['money_change_id']: float(r['change']) for r in rows('money_change')}
    rewards = defaultdict(list)
    for r in rows('quest_reward_exp'):
        rewards[(r['quest_id'], r['objective_id'])].append(('xp', r['rpg_attr'], exp.get(r['exp_change_id'], 0.0), r['is_immediate']))
    for r in rows('quest_reward_money'):
        rewards[(r['quest_id'], r['objective_id'])].append(('money', '', money.get(r['money_change_id'], 0.0), r['is_immediate']))
    for r in rows('quest_reward_item'):
        rewards[(r['quest_id'], r['objective_id'])].append(('item', r['item_id'], float(r['count']), r['is_immediate']))
    for r in rows('quest_reward_perk'):
        if not r.get('soul_id'):
            rewards[(r['quest_id'], r['objective_id'])].append(('perk', r['perk_id'], 1.0, r['is_immediate']))
    return quest, objective, rewards


if __name__ == '__main__':
    quest, objective, rewards = load(sys.argv[1])
    out = ['KCDUS_QR = {']
    for (qid, oid), rs in sorted(rewards.items()):
        qn, on = quest.get(qid), objective.get((qid, oid))
        if not qn or not on:
            continue
        parts = ','.join("{'%s','%s',%g,'%s'}" % (k, a, v, i) for k, a, v, i in rs)
        out.append("{q='%s',o='%s',r={%s}}," % (qn, on, parts))
    out.append('}')
    Path(sys.argv[2]).write_text('\n'.join(out), encoding='utf-8')
    print(len(out) - 2, 'rewarded objectives')
