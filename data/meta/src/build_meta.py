#!/usr/bin/env python3
"""Construit data/meta/{card_tiers,relic_tiers,archetypes}.json depuis les sources publiques.

Usage :
  python3 -I data/meta/src/build_meta.py --fetch   # télécharge les sources dans data/meta/cache/ (local, gitignoré)
  python3 -I data/meta/src/build_meta.py           # reconstruit les JSON depuis le cache

Règles (docs/meta/README.md) :
- chaque note porte source_id + patch + date ; aucune donnée sans source ;
- on ne recopie jamais les commentaires des auteurs (seulement lettre de tier et drapeaux factuels) ;
- correspondance nom -> classe C# : identifiant du jeu (Untapped, Spire Codex) sinon titre anglais de la
  localisation du jeu (slaythetierlist, guides) ; les cas ambigus ou introuvables sont listés.
"""
import argparse, glob, html, json, os, re, statistics, sys, urllib.request
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
META = os.path.dirname(HERE)
CACHE = os.path.join(META, 'cache')
GAME = os.environ.get('STS2_GAME', '/home/mio/sts2/game')
SRC = os.path.join(GAME, 'csharp/MegaCrit/Sts2/Core/Models')
LOC = os.path.join(GAME, 'pck/localization')
CATALOG = os.environ.get('STS2_CATALOG', os.path.join(os.path.dirname(os.path.dirname(META)), 'data/catalog/cards.json'))
if not os.path.exists(CATALOG):
    CATALOG = '/home/mio/sts2/data/catalog/cards.json'

PATCH = 'v0.107.1'
ACCESSED = '2026-10-28'
CHARS = ['ironclad', 'silent', 'defect', 'regent', 'necrobinder']
POOL2CHAR = {f'{c.capitalize()}CardPool': c for c in CHARS} | {'ColorlessCardPool': 'colorless'}
RPOOL2CHAR = {f'{c.capitalize()}RelicPool': c for c in CHARS} | {'SharedRelicPool': 'shared', 'EventRelicPool': 'ancient_event'}

# Convention ÉCLAIREUR (pas une donnée de source) : lettres réparties linéairement sur 0-100.
TIER_SCORE = {'S': 90, 'A': 75, 'B': 60, 'C': 45, 'D': 30, 'F': 15}
RELIC_CATEGORIES = ['Always Amazing', 'Always Good', 'Needs Synergy', 'Inconsistent', 'Almost Never']

UNTAPPED_API = 'https://api.sts2.untapped.gg/api/v1/tier-lists/{}/resolve/?game_version=' + PATCH
SC_API = 'https://spire-codex.com/api/runs'


def snake(cls):
    return re.sub(r'(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])', '_', cls).upper()


def norm(name):
    return re.sub(r'[^a-z0-9]', '', (name or '').lower())


def load(path):
    with open(path, encoding='utf-8') as f:
        return json.load(f)


def get(url, dest):
    req = urllib.request.Request(url, headers={'User-Agent': 'ULTIMA-eclaireur/1 (personal tracker)'})
    with urllib.request.urlopen(req, timeout=60) as r, open(dest, 'wb') as f:
        f.write(r.read())
    print('fetched', url, '->', os.path.relpath(dest, META))


def fetch(sources):
    os.makedirs(CACHE, exist_ok=True)
    for sid, prefix in (('untapped-baalorlord', 'bl'), ('untapped-jorbs', 'jorbs')):
        for ch, l in sources[sid]['lists'].items():
            get(UNTAPPED_API.format(l['uuid']), os.path.join(CACHE, f'{prefix}_{ch}.json'))
    get(f'{SC_API}/scores/cards?bracket={PATCH}&include_skip=true', os.path.join(CACHE, f'sc_cards_{PATCH}.json'))
    get(f'{SC_API}/scores/cards?bracket=a10:{PATCH}&include_skip=true', os.path.join(CACHE, f'sc_cards_a10_{PATCH}.json'))
    get(f'{SC_API}/scores/relics?bracket={PATCH}', os.path.join(CACHE, f'sc_relics_{PATCH}.json'))
    get(f'{SC_API}/archetypes?lang=eng', os.path.join(CACHE, 'sc_archetypes.json'))
    get('https://slaythetierlist.com/', os.path.join(CACHE, 'stl.html'))


# ---------------------------------------------------------------- index du jeu
def game_index():
    def pools(dirn, kind):
        m = defaultdict(list)
        for f in glob.glob(f'{SRC}/{dirn}/*.cs'):
            p = os.path.basename(f)[:-3]
            for c in re.findall(rf'ModelDb\.{kind}<(\w+)>\(\)', open(f, encoding='utf-8').read()):
                m[c].append(p)
        return m
    cat = {c['class']: c for c in load(CATALOG)}
    out = {}
    for kind, dirn, locf, names in (
        ('card', 'CardPools', 'cards.json', None),
        ('relic', 'RelicPools', 'relics.json', None),
    ):
        pm = pools(dirn, kind.capitalize())
        eng = load(f'{LOC}/eng/{locf}'); fra = load(f'{LOC}/fra/{locf}')
        classes = set(pm)
        if kind == 'card':
            classes |= set(cat)
        else:
            classes |= {os.path.basename(f)[:-3] for f in glob.glob(f'{SRC}/Relics/*.cs')}
        rows = {}
        for c in sorted(classes):
            i = snake(c)
            r = {'class': c, 'id': i, 'pools': sorted(pm.get(c, [])),
                 'name_eng': eng.get(i + '.title'), 'name_fra': fra.get(i + '.title')}
            if kind == 'card' and c in cat:
                r.update({k: cat[c].get(k) for k in ('rarity', 'type', 'cost')})
            rows[i] = r
        out[kind] = rows
    return out


def owner(row, table):
    for p in row['pools']:
        if p in table:
            return table[p]
    return 'other:' + (row['pools'][0] if row['pools'] else 'none')


def name_resolver(rows, table):
    by = defaultdict(list)
    for r in rows.values():
        if r['name_eng']:
            by[norm(r['name_eng'])].append(r)

    def resolve(name, character=None):
        cands = by.get(norm(name), [])
        exact = [r for r in cands if r['name_eng'].strip().casefold() == name.strip().casefold()]
        if len(cands) > 1 and exact:
            cands = exact
        if character and len(cands) > 1:
            pref = [r for r in cands if owner(r, table) == character]
            if len(pref) == 1:
                return pref[0], None
        if len(cands) == 1:
            return cands[0], None
        if not cands:
            return None, 'introuvable dans la localisation eng du jeu v0.107.1'
        return None, 'ambigu : ' + ', '.join(r['class'] for r in cands)
    return resolve


# ---------------------------------------------------------------- sources
def untapped_ratings(sources):
    """-> list of (kind, item_id, rating dict)"""
    out = []
    for sid, prefix in (('untapped-baalorlord', 'bl'), ('untapped-jorbs', 'jorbs')):
        for ch, l in sources[sid]['lists'].items():
            p = os.path.join(CACHE, f'{prefix}_{ch}.json')
            t = load(p)['tier_list']
            assert t['version'] == PATCH, (p, t['version'])
            for tier in t['tiers']:
                for group in ('cards', 'relics'):
                    for it in tier.get(group) or []:
                        note = []
                        if it.get('good_upgrade'):
                            note.append('bonne amélioration (good_upgrade)')
                        if it.get('max_copies'):
                            note.append(f"max {it['max_copies']} copie(s)")
                        if it.get('commentary'):
                            note.append("commentaire d'auteur sur la source (non recopié)")
                        name = tier['name']
                        r = {'source_id': sid, 'list': ch, 'patch': it.get('commentary_version') or t['version'],
                             'date': t['updated'][:10], 'note': '; '.join(note)}
                        if name in TIER_SCORE:
                            r.update(tier=name, score=TIER_SCORE[name])
                        else:
                            r.update(tier=name, score=None, tier_kind='ancient_category')
                        if not it.get('commentary_version'):
                            r['patch'] = t['version']
                            r['note'] = (r['note'] + '; ' if r['note'] else '') + 'commentary_version absent : patch de la liste utilisé'
                        out.append((group[:-1], it['item_id'], r))
    return out


def sc_ratings(kind, bracket, label):
    p = os.path.join(CACHE, f"sc_{kind}s_{bracket.replace(':', '_')}.json")
    d = load(p)
    out = []
    for iid, v in d.items():
        if iid == 'SKIP' or not isinstance(v, dict) or v.get('score') is None:
            continue
        note = f"n={v.get('picks')} prises, victoire {v.get('win_rate')} %"
        if v.get('pick_rate') is not None:
            note += f", taux de prise {v['pick_rate']} %"
        if v.get('elo') is not None:
            note += f", Elo {v['elo']} (toutes versions)"
        out.append((kind, iid, {'source_id': 'spirecodex-scores', 'bracket': bracket, 'tier': None,
                                'score': v['score'], 'patch': PATCH, 'date': ACCESSED,
                                'n': v.get('picks'), 'note': label + ' ; ' + note}))
    return out, d.get('SKIP')


def stl_ratings():
    s = open(os.path.join(CACHE, 'stl.html'), encoding='utf-8', errors='replace').read()
    t = re.sub(r'<script.*?</script>|<style.*?</style>', '', s, flags=re.S)
    t = html.unescape(re.sub(r'<[^>]+>', '\n', t))
    rx = r'^(.+?) — (Ironclad|Silent|Regent|Necrobinder|Defect) ([SABCDF])-Tier card in Slay the Spire 2$'
    return [(m.group(1).strip(), m.group(2).lower(), m.group(3)) for m in re.finditer(rx, t, flags=re.M)]


# ---------------------------------------------------------------- build
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--fetch', action='store_true')
    a = ap.parse_args()
    sources = {s['id']: s for s in load(os.path.join(META, 'sources.json'))['sources']}
    if a.fetch:
        fetch(sources)
    idx = game_index()
    cards, relics = idx['card'], idx['relic']
    uncertain = []

    # --- cartes
    ratings = defaultdict(list)
    relic_ratings = defaultdict(list)
    for kind, iid, r in untapped_ratings(sources):
        table = cards if kind == 'card' else relics
        if iid not in table:
            uncertain.append({'source_id': r['source_id'], 'list': r['list'], 'kind': kind, 'source_name': iid,
                              'reason': "identifiant absent du jeu v0.107.1 (carte retirée/renommée ? ou bêta)"})
            continue
        (ratings if kind == 'card' else relic_ratings)[iid].append(r)
    sc_cards, skip = sc_ratings('card', PATCH, 'toutes ascensions')
    sc_cards_a10, _ = sc_ratings('card', 'a10:' + PATCH, 'Ascension 10')
    for kind, iid, r in sc_cards + sc_cards_a10:
        if iid not in cards:
            uncertain.append({'source_id': 'spirecodex-scores', 'kind': 'card', 'source_name': iid,
                              'reason': 'identifiant absent du jeu v0.107.1'})
            continue
        ratings[iid].append(r)
    sc_rel, _ = sc_ratings('relic', PATCH, 'toutes ascensions')
    for kind, iid, r in sc_rel:
        if iid not in relics:
            uncertain.append({'source_id': 'spirecodex-scores', 'kind': 'relic', 'source_name': iid,
                              'reason': 'identifiant absent du jeu v0.107.1'})
            continue
        relic_ratings[iid].append(r)
    resolve_card = name_resolver(cards, POOL2CHAR)
    stl_date = sources['slaythetierlist']['updated']
    for name, ch, tier in stl_ratings():
        row, why = resolve_card(name, ch)
        if not row:
            uncertain.append({'source_id': 'slaythetierlist', 'kind': 'card', 'source_name': name, 'list': ch, 'reason': why})
            continue
        if norm(row['name_eng']) != norm(name):
            uncertain.append({'source_id': 'slaythetierlist', 'kind': 'card', 'source_name': name, 'list': ch,
                              'resolved_class': row['class'], 'reason': 'résolu par nom normalisé, graphie différente'})
        ratings[row['id']].append({'source_id': 'slaythetierlist', 'list': ch, 'tier': tier, 'score': TIER_SCORE[tier],
                                   'patch': PATCH, 'date': stl_date, 'note': ''})

    def entry(row, rs, table):
        e = {k: row.get(k) for k in ('class', 'id', 'name_eng', 'name_fra', 'rarity', 'type', 'cost') if k in row}
        e['owner'] = owner(row, table)
        for r in rs:
            if r.get('list') and r['list'] not in (e['owner'], 'colorless') and table is POOL2CHAR:
                r['rated_for'] = r['list']
        e['ratings'] = rs
        # consensus : médiane des scores normalisés, une voix par source (Spire Codex : tranche toutes ascensions)
        per_src = {}
        for r in rs:
            if r.get('score') is None or r.get('bracket', '').startswith('a10'):
                continue
            if r.get('rated_for'):
                continue
            per_src.setdefault(r['source_id'], r['score'])
        if per_src:
            vals = list(per_src.values())
            e['consensus'] = {'score': statistics.median(vals), 'n_sources': len(vals),
                              'spread': max(vals) - min(vals), 'sources': sorted(per_src)}
        return e

    # couverture
    def coverage(rows, table, groups, keys):
        cov = {}
        for k in keys:
            total = [r for r in rows.values() if owner(r, table) == k]
            rated = [r for r in total if r['class'] in groups.get(k, {})]
            by_src = Counter()
            for r in rated:
                for s in {x['source_id'] for x in groups[k][r['class']]['ratings'] if not x.get('rated_for')}:
                    by_src[s] += 1
            multi = sum(1 for r in rated if groups[k][r['class']].get('consensus', {}).get('n_sources', 0) >= 2)
            cov[k] = {'total_in_pool': len(total), 'with_rating': len(rated), 'with_2plus_sources': multi,
                      'by_source': dict(sorted(by_src.items())),
                      'unrated': sorted(r['class'] for r in total if r['class'] not in groups.get(k, {}))}
        return cov

    def build_groups(rat, rows, table, full):
        g = defaultdict(dict)
        for iid, rs in rat.items():
            kept = [dict(r) for r in rs if full or sources[r['source_id']].get('redistribution') != 'local-only']
            if not kept:
                continue
            e = entry(rows[iid], kept, table)
            g[e['owner']][rows[iid]['class']] = e
        return g

    tier_doc = {
        'S': 'tier S/A/B/C/D/F des sources → score 90/75/60/45/30/15 (convention ÉCLAIREUR, linéaire, documentée docs/meta/README.md)',
        'spirecodex': 'score = Codex Score 0-100 tel que publié par Spire Codex (formule non publiée)',
        'consensus': 'médiane des scores normalisés, une voix par source (Spire Codex : tranche toutes ascensions v0.107.1) ; notes rated_for (carte notée dans la liste d’un autre personnage) exclues',
    }
    variants = {}
    for full in (False, True):
        groups = build_groups(ratings, cards, POOL2CHAR, full)
        rgroups = build_groups(relic_ratings, relics, RPOOL2CHAR, full)
        scope = ('COMPLET (toutes sources, usage personnel local, jamais commité)' if full else
                 'PUBLIC (sources redistribuables seulement ; les notes local-only sont dans data/meta/local/ après build_meta.py --fetch)')
        card_doc = {
            'schema': 'ultima.meta.card_tiers/1', 'variant': 'full' if full else 'public', 'scope': scope,
            'generated': ACCESSED, 'patch_target': PATCH,
            'generator': 'data/meta/src/build_meta.py', 'sources_file': 'data/meta/sources.json',
            'score_conventions': tier_doc,
            'skip_reference': {'source_id': 'spirecodex-scores', 'patch': 'toutes versions',
                               'pick_rate_percent': skip and skip.get('pick_rate'), 'elo': skip and skip.get('elo'),
                               'note': 'Option « passer » : taux de passage communautaire et Elo (Bradley-Terry) ; utile pour HearthArena-like (comparer chaque carte à SKIP).'},
            'coverage': coverage(cards, POOL2CHAR, groups, CHARS + ['colorless']),
            'characters': {k: dict(sorted(v.items())) for k, v in sorted(groups.items())},
        }
        relic_doc = {
            'schema': 'ultima.meta.relic_tiers/1', 'variant': 'full' if full else 'public', 'scope': scope,
            'generated': ACCESSED, 'patch_target': PATCH,
            'generator': 'data/meta/src/build_meta.py', 'sources_file': 'data/meta/sources.json',
            'score_conventions': tier_doc | {'ancient_category': "Baalorlord classe les reliques d'Ancients (EventRelicPool) en catégories « " + ' / '.join(RELIC_CATEGORIES) + " » : non converties en score (pas d'ordre de puissance prouvé)"},
            'coverage': coverage(relics, RPOOL2CHAR, rgroups, CHARS + ['shared', 'ancient_event']),
            'groups': {k: dict(sorted(v.items())) for k, v in sorted(rgroups.items())},
        }
        variants[full] = (card_doc, relic_doc)

    # --- archétypes
    resolve_relic = name_resolver(relics, RPOOL2CHAR)
    sc = load(os.path.join(CACHE, 'sc_archetypes.json'))
    arche = defaultdict(list)
    for CH, arr in sc['characters'].items():
        ch = CH.lower()
        for a in arr:
            h = {x['version']: x for x in a['history']}
            v = h.get(PATCH)
            item = {
                'name': a['name'], 'source_id': 'spirecodex-archetypes', 'kind': 'cluster_statistique',
                'date': sc['built_at'][:10],
                'key_cards': [{'class': cards[c['id']]['class'] if c['id'] in cards else None, 'id': c['id'], 'name_eng': c['name']} for c in a['defining_cards']],
                'key_relics': [{'class': relics[r['id']]['class'] if r['id'] in relics else None, 'id': r['id'], 'name_eng': r['name']} for r in a['defining_relics']],
                'runs': a['size'], 'share_percent': a['share'], 'win_rate_percent': a['win_rate'],
                'at_patch': {'patch': PATCH, 'share_percent': v['share'], 'win_rate_percent': v['win_rate']} if v else None,
                'is_playstyle': a['win_rate'] >= 5,
                'conditions': 'Cluster de runs réelles (cartes + reliques possédées).' + ('' if a['win_rate'] >= 5 else ' Victoire < 5 % : profil de runs mortes tôt (cartes/reliques d’acte 1), pas une stratégie.'),
            }
            arche[ch].append(item)
    for a in load(os.path.join(HERE, 'archetypes_curated.json'))['archetypes']:
        src = sources[a['source_id']]
        kc, kr = [], []
        for n in a['key_cards']:
            row, why = resolve_card(n, a['character'])
            kc.append({'class': row['class'] if row else None, 'name_eng': n})
            if not row:
                uncertain.append({'source_id': a['source_id'], 'kind': 'card', 'source_name': n, 'list': a['character'], 'reason': why})
        for n in a['key_relics']:
            row, why = resolve_relic(n)
            kr.append({'class': row['class'] if row else None, 'name_eng': n})
            if not row:
                uncertain.append({'source_id': a['source_id'], 'kind': 'relic', 'source_name': n, 'list': a['character'], 'reason': why})
        arche[a['character']].append({
            'name': a['name'], 'source_id': a['source_id'], 'kind': 'guide',
            'date': src.get('updated'), 'patch': src.get('patch'),
            'stale': src['status'] == 'retenue-perimee',
            'key_cards': kc, 'key_relics': kr, 'conditions': a['conditions'],
        })
    arch_doc = {
        'schema': 'ultima.meta.archetypes/1', 'generated': ACCESSED, 'patch_target': PATCH,
        'generator': 'data/meta/src/build_meta.py', 'sources_file': 'data/meta/sources.json',
        'characters': dict(sorted(arche.items())),
    }

    # dédoublonnage des incertitudes
    seen, unc = set(), []
    for u in uncertain:
        k = json.dumps(u, sort_keys=True)
        if k not in seen:
            seen.add(k); unc.append(u)
    os.makedirs(os.path.join(META, 'local'), exist_ok=True)
    for full, (card_doc, relic_doc) in variants.items():
        card_doc['uncertain_mappings'] = [u for u in unc if u['kind'] == 'card']
        relic_doc['uncertain_mappings'] = [u for u in unc if u['kind'] == 'relic']
        base = os.path.join(META, 'local') if full else META
        for name, doc in (('card_tiers.json', card_doc), ('relic_tiers.json', relic_doc)):
            with open(os.path.join(base, name), 'w', encoding='utf-8') as f:
                json.dump(doc, f, ensure_ascii=False, indent=1)
                f.write('\n')
        print('==', 'local/ (complet)' if full else 'public')
        for k, v in card_doc['coverage'].items():
            print(f"cards {k:12} {v['with_rating']:3}/{v['total_in_pool']:3}  2+src={v['with_2plus_sources']:3}  {v['by_source']}")
        for k, v in relic_doc['coverage'].items():
            print(f"relics {k:14} {v['with_rating']:3}/{v['total_in_pool']:3}  {v['by_source']}")
    with open(os.path.join(META, 'archetypes.json'), 'w', encoding='utf-8') as f:
        json.dump(arch_doc, f, ensure_ascii=False, indent=1)
        f.write('\n')
    print('uncertain', sum(u['kind'] == 'card' for u in unc), 'cards,', sum(u['kind'] == 'relic' for u in unc), 'relics')

if __name__ == '__main__':
    sys.exit(main())
