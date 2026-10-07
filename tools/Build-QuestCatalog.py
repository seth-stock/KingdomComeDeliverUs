# Copyright (C) 2026 the Kingdom Come: Deliver Us contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance and its content
# belong to Warhorse Studios and Deep Silver. Unofficial, free, not affiliated with or endorsed by them.
"""
Every quest of Kingdom Come: Deliverance (the first game) with its English title and the reviewed gating answer, so the
story-lock logic is checked against ALL of them and not only the 29 main quests.

Reads (never writes) <KCD1>\\Data\\Tables.pak and <KCD1>\\Localization\\English_xml.pak, and docs/quest-gating-plan.csv.
Writes
  docs/quest-catalog.csv                        one row per quest, for review
  docs/quest-gating-table.md                    the same, as a readable table
  dotnet/KcdUs.Agent/QuestCatalog.g.cs          the table the agent and the tests read
  mod/kcdus/lua/kcdus_quests.lua                the list of quests the game side watches

    python tools\\Build-QuestCatalog.py [--game-dir F:\\SteamLibrary\\steamapps\\common\\KingdomComeDeliverance] [--check]

--check recomputes in memory and fails if a file differs (the repo's drift check).
"""
import argparse, csv, hashlib, io, os, re, sys, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kcdpak

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PLAN = os.path.join(ROOT, 'docs', 'quest-gating-plan.csv')
CSV_OUT = os.path.join(ROOT, 'docs', 'quest-catalog.csv')
MD_OUT = os.path.join(ROOT, 'docs', 'quest-gating-table.md')
CS_OUT = os.path.join(ROOT, 'dotnet', 'KcdUs.Agent', 'QuestCatalog.g.cs')
LUA_OUT = os.path.join(ROOT, 'mod', 'kcdus', 'lua', 'kcdus_quests.lua')
TIERS = ('open', 'mixed', 'rails')
KINDS = ('main', 'side', 'activity', 'event', 'system', 'test')


def find_game_dir(arg):
    if arg:
        return arg
    for lib in ('F:\\SteamLibrary', 'E:\\SteamLibrary', 'D:\\SteamLibrary', 'C:\\Program Files (x86)\\Steam', 'C:\\games\\steam'):
        p = os.path.join(lib, 'steamapps', 'common', 'KingdomComeDeliverance')
        if os.path.isdir(p):
            return p
    sys.exit('Kingdom Come: Deliverance not found; pass --game-dir')


def table_rows(text):
    return [dict(re.findall(r'(\w+)="([^"]*)"', r)) for r in re.findall(r'<row\s+([^>]*?)/?>', text)]


def read_member(pak, name):
    f, ents = kcdpak.read_pak(pak)
    want = name.lower()
    for e in ents:
        if e[0].lower() == want:
            return kcdpak.read_entry(f, e).decode('utf-8', 'replace')
    sys.exit('%s not found in %s' % (name, pak))


def load_game(game):
    tables = os.path.join(game, 'Data', 'Tables.pak')
    loc = os.path.join(game, 'Localization', 'English_xml.pak')
    pak_hash = hashlib.sha256(open(tables, 'rb').read()).hexdigest()
    quests = table_rows(read_member(tables, 'Libs/Tables/quest/quest.xml'))
    q2s = table_rows(read_member(tables, 'Libs/Tables/quest/quest2skald_subchapter.xml'))
    sq = table_rows(read_member(tables, 'Libs/Tables/skald/skald_quest_string.xml'))
    objs = table_rows(read_member(tables, 'Libs/Tables/quest/quest_objective.xml'))
    text = read_member(loc, 'text_ui_quest.xml')
    strings = {}
    for k, a, b in re.findall(r'<Row><Cell>([^<]*)</Cell><Cell>([^<]*)</Cell><Cell>([^<]*)</Cell></Row>', text):
        strings[k] = (b or a).replace('\ufffd', "'")
    title_of_sub = {r['skald_subchapter_id']: strings.get(r['string_name'], '')
                    for r in sq if r['skald_quest_string_type_id'] == '1'}
    titles = collections.defaultdict(list)
    for r in q2s:
        t = title_of_sub.get(r['skald_subchapter_id'], '')
        if t and t not in titles[r['quest_id']]:
            titles[r['quest_id']].append(t.strip())
    obj_count = collections.Counter(r['quest_id'] for r in objs)
    rows = []
    for r in quests:
        rows.append(dict(code=r['quest_name'], group=r['group'], qtype=r['quest_type_id'], quest_id=r['quest_id'],
                         title=' / '.join(titles.get(r['quest_id'], [])), objectives=obj_count[r['quest_id']],
                         objective_names={int(o['objective_id']): o['objective_name'] for o in objs if o['quest_id'] == r['quest_id']}))
    return rows, pak_hash


def load_plan():
    with open(PLAN, encoding='utf-8', newline='') as f:
        rows = list(csv.DictReader(f))
    return rows


def validate(game_rows, plan_rows):
    errs = []
    game = {r['code'] for r in game_rows}
    plan = collections.OrderedDict()
    for p in plan_rows:
        if p['code'] in plan:
            errs.append('duplicate plan row ' + p['code'])
        plan[p['code']] = p
    for c in game - set(plan):
        errs.append('quest has no plan row: ' + c)
    for c in set(plan) - game:
        errs.append('plan row has no quest: ' + c)
    periods = collections.defaultdict(list)
    for c, p in plan.items():
        if p['tier'] not in TIERS:
            errs.append('%s: tier %r' % (c, p['tier']))
        if p['kind'] not in KINDS:
            errs.append('%s: kind %r' % (c, p['kind']))
        if p['tier'] == 'open' and p['period']:
            errs.append('%s: an open quest names period %s' % (c, p['period']))
        if p['tier'] != 'open' and not p['period']:
            errs.append('%s: a %s quest has no period' % (c, p['tier']))
        if p['kind'] in ('system', 'test') and p['tier'] != 'open':
            errs.append('%s: a %s row cannot be gated' % (c, p['kind']))
        if p['period']:
            periods[p['period']].append(p)
    for per, mem in periods.items():
        if per not in [m['code'] for m in mem]:
            errs.append('period %s is not named by one of its own members' % per)
        whys = {m['why'] for m in mem}
        if len(whys) != 1 or not next(iter(whys)):
            errs.append('period %s: members disagree about the why (%s)' % (per, sorted(whys)))
    mains = [p for p in plan.values() if p['kind'] == 'main' and int(p['order'] or 0) > 0]
    orders = sorted(int(p['order']) for p in mains)
    if orders != sorted(orders) or orders[0] != 1:
        errs.append('main order does not start at 1')
    return errs, plan, periods


def merged(game_rows, plan):
    out = []
    for g in game_rows:
        p = plan[g['code']]
        out.append(dict(code=g['code'], kind=p['kind'], order=int(p['order'] or 0), title=g['title'], group=g['group'],
                        dlc=p['dlc'], tier=p['tier'], period=p['period'], why=p['why'], objectives=g['objectives'],
                        source=p['source'], note=p['note'], objective_names=g['objective_names']))
    return out


def csv_text(rows, pak_hash):
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator='\n')
    w.writerow(['code', 'kind', 'order', 'title', 'group', 'dlc', 'tier', 'period', 'objectives', 'source', 'note'])
    for r in rows:
        w.writerow([r['code'], r['kind'], r['order'], r['title'], r['group'], r['dlc'], r['tier'], r['period'], r['objectives'], r['source'], r['note']])
    return buf.getvalue()


def cs_str(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'


def cs_text(rows, pak_hash):
    b = ['// <auto-generated>',
         '// Generated by tools/Build-QuestCatalog.py from the game\'s Tables.pak and docs/quest-gating-plan.csv. Do not edit.',
         '// Tables.pak sha256: ' + pak_hash,
         '// </auto-generated>',
         'namespace KcdUs.Agent;', '',
         '/// <summary>One quest of the first game: its code (as QuestSystem takes it), kind, English title, gating tier and period.</summary>',
         'public sealed record QuestRoot(string Code, string Kind, string Title, string Group, string Dlc, string Tier, string Period, string Why, int Order, int Objectives);',
         '', 'public static partial class QuestCatalog', '{',
         '\tpublic const string TablesSha256 = "%s";' % pak_hash, '\tpublic static readonly QuestRoot[] All =', '\t{']
    for r in rows:
        b.append('\t\tnew(%s, %s, %s, %s, %s, %s, %s, %s, %d, %d),' % (
            cs_str(r['code']), cs_str(r['kind']), cs_str(r['title']), cs_str(r['group']), cs_str(r['dlc']), cs_str(r['tier']),
            cs_str(r['period']), cs_str(r['why']), r['order'], r['objectives']))
    b += ['\t};', '}', '']
    return '\n'.join(b)


def lua_text(rows, pak_hash):
    watch = [r for r in rows if r['kind'] in ('main', 'side', 'activity', 'event') and r['tier'] != 'open']
    mains = [r for r in rows if r['kind'] == 'main' and r['tier'] == 'open']
    b = ['-- Generated by tools/Build-QuestCatalog.py from the game\'s Tables.pak and docs/quest-gating-plan.csv. Do not edit.',
         '-- Tables.pak sha256: ' + pak_hash,
         '-- The quests whose state the game side watches: every one that can put the host on rails or in a mixed stretch,',
         '-- plus the open main quests (their start and end are still story facts).',
         'KCDUS_QUESTS = {']
    for r in watch + mains:
        b.append('  { code = "%s", tier = "%s", period = "%s", kind = "%s" },' % (r['code'], r['tier'], r['period'], r['kind']))
    b += ['}', '', '-- Candidate mirroring metadata: identifiers only; no game scripts/assets.', 'KCDUS_QUEST_MIRROR = {']
    for r in rows:
        if r['kind'] not in ('main', 'side', 'activity', 'event'):
            continue
        objectives = ', '.join('[%d]=%s' % (key, cs_str(value)) for key, value in sorted(r['objective_names'].items()))
        b.append('  { code=%s, kind=%s, tier=%s, dlc=%s, objectives={%s} },' %
                 (cs_str(r['code']), cs_str(r['kind']), cs_str(r['tier']), cs_str(r['dlc']), objectives))
    b += ['}', '']
    return '\n'.join(b)


def md_text(rows, periods, pak_hash):
    b = ['# The gating on every quest of the game', '',
         'Generated by `tools/Build-QuestCatalog.py` from the game\'s own quest data (`Tables.pak`, sha256 `%s`) and the reviewed plan'
         % pak_hash[:16], '(`docs/quest-gating-plan.csv`). Do not edit; edit the plan and run the tool.', '']
    cnt = collections.Counter((r['kind'], r['tier']) for r in rows)
    b.append('| Kind | rails | mixed | open | all |')
    b.append('|---|---|---|---|---|')
    for k in KINDS:
        a = [cnt[(k, t)] for t in ('rails', 'mixed', 'open')]
        b.append('| %s | %d | %d | %d | %d |' % (k, a[0], a[1], a[2], sum(a)))
    b.append('| **all** | %d | %d | %d | %d |' % (sum(cnt[(k, 'rails')] for k in KINDS), sum(cnt[(k, 'mixed')] for k in KINDS),
                                                 sum(cnt[(k, 'open')] for k in KINDS), len(rows)))
    b += ['', '## Periods (one question each)', '', '| Period | Why | Members |', '|---|---|---|']
    for per in sorted(periods, key=lambda p: min([int(m['order'] or 999) for m in periods[p]] + [999]) * 1000 + sorted(periods).index(p)):
        mem = periods[per]
        names = ', '.join('%s (%s)' % (m['code'], m['tier']) for m in mem)
        b.append('| `%s` | %s | %s |' % (per, mem[0]['why'].replace('|', '/'), names))
    b += ['', '## Every quest', '', '| Code | Kind | # | Title | DLC | Tier | Period | Objectives |', '|---|---|---|---|---|---|---|---|']
    order = {k: i for i, k in enumerate(KINDS)}
    for r in sorted(rows, key=lambda r: (order[r['kind']], r['order'] or 999, r['code'].lower())):
        b.append('| `%s` | %s | %s | %s | %s | %s | %s | %d |' % (r['code'], r['kind'], r['order'] or '', r['title'].replace('|', '/'), r['dlc'],
                                                              r['tier'], ('`%s`' % r['period']) if r['period'] else '', r['objectives']))
    b.append('')
    return '\n'.join(b)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--game-dir')
    ap.add_argument('--check', action='store_true')
    a = ap.parse_args()
    game = find_game_dir(a.game_dir)
    game_rows, pak_hash = load_game(game)
    plan_rows = load_plan()
    errs, plan, periods = validate(game_rows, plan_rows)
    if errs:
        print('PLAN INVALID:\n  ' + '\n  '.join(errs))
        sys.exit(1)
    rows = merged(game_rows, plan)
    outputs = {CSV_OUT: csv_text(rows, pak_hash), MD_OUT: md_text(rows, periods, pak_hash),
               CS_OUT: cs_text(rows, pak_hash), LUA_OUT: lua_text(rows, pak_hash)}
    bad = 0
    for path, text in outputs.items():
        if a.check:
            cur = open(path, encoding='utf-8', newline='').read() if os.path.exists(path) else None
            if cur != text:
                print('DRIFT: ' + os.path.relpath(path, ROOT))
                bad += 1
        else:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            open(path, 'w', encoding='utf-8', newline='').write(text)
    cnt = collections.Counter(r['tier'] for r in rows)
    print('%d quests (%d rails, %d mixed, %d open), %d periods%s' % (len(rows), cnt['rails'], cnt['mixed'], cnt['open'], len(periods),
                                                                  '; %d files drift' % bad if a.check else ''))
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
