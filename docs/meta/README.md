# Méta communautaire STS2 — `data/meta/`

> Agent F « L'Éclaireur », swarm v2. Collecte du 2026-10-28. Patch cible : **v0.107.1**
> (build joueur, branche principale ; sortie publique 2026-06-19, commit de build 59260271
> du 2026-06-18). La bêta est en v0.111.0 : rien de la bêta n'est utilisé.
> Skill appliquée : `sts2-meta-scoring` — « `base` : tier communautaire du patch (source + date)
> sinon heuristique du catalogue. »

## 1. Fichiers

| Fichier | Contenu | Produit par |
|---|---|---|
| `sources.json` | registre des 15 sources examinées (id, url, auteur, dates, patch, fiabilité et pourquoi, conditions d'usage, statut retenue / retenue-perimee / ecartee) | écrit à la main |
| `card_tiers.json` | **variante publique** (commitée) : uniquement les sources redistribuables (Spire Codex). Par personnage (+ `colorless`, `other:EventCardPool`) : classe C# → `{class, id, name_eng, name_fra, rarity, type, cost, owner, ratings[], consensus}` ; chaque rating = `{source_id, tier, score, patch, date, note}` (+ `n`, `bracket`, `rated_for` selon la source) | `src/build_meta.py` |
| `relic_tiers.json` | même forme, groupes `ironclad…necrobinder`, `shared`, `ancient_event` (variante publique) | `src/build_meta.py` |
| `local/card_tiers.json`, `local/relic_tiers.json` | **variante complète** (toutes sources, y compris Untapped et slaythetierlist), même schéma, `variant: "full"` : **gitignorée**, produite sur la machine de Mio par `build_meta.py --fetch` (usage personnel). Le moteur de scoring doit lire `local/` si présent, sinon la variante publique | `src/build_meta.py` |
| `archetypes.json` | par personnage : clusters statistiques Spire Codex + archétypes nommés de guides | `src/build_meta.py` + `src/archetypes_curated.json` |
| `src/build_meta.py` | `--fetch` re-télécharge les sources dans `cache/` (gitignoré) ; sans option, reconstruit les JSON depuis le cache | — |

Reproduire : `python3 -I data/meta/src/build_meta.py --fetch` (réseau) puis relire le résumé de couverture imprimé.

## 2. Méthode

1. **Recensement** : WebSearch (tier lists, guides d'archétypes, statistiques), lecture du prior art
   (`docs/04-trackers.md`, `research/notes/overlays-templates.md`). La piste décisive vient du code
   MIT de `yogan/sts2-tierlist-overlay` : l'API publique d'Untapped.gg résout une tier list
   **pour une version de jeu donnée** (`resolve/?game_version=v0.107.1`). Spire Codex expose une
   API publique documentée (`/openapi.json`) avec des scores filtrables par `build_id`.
2. **Datation** : chaque source porte sa date de mise à jour et son patch. Untapped : champ
   `version` = v0.107.1 et `commentary_version` = v0.107.1 par entrée (630/636 ; 6 entrées Regent
   sans version → patch de la liste, signalé dans `note`) ; les versions antérieures renvoient 404,
   `game_version=public` résout vers v0.107.1. Spire Codex : tranche `bracket=v0.107.1`.
3. **Correspondance nom → classe C#** : index construit depuis le source du jeu
   (`game/csharp/.../CardPools/*.cs`, `RelicPools/*.cs`) + `data/catalog/cards.json` + localisation
   `eng`/`fra`. Identifiant du jeu = `UPPER_SNAKE` de la classe (ex. `TimesUp` → `TIMES_UP`).
   - Untapped et Spire Codex donnent directement l'identifiant du jeu : correspondance exacte.
   - slaythetierlist et les guides donnent un nom anglais : résolu par le titre `eng` du jeu
     (exact d'abord, puis normalisé sans ponctuation, puis préférence au personnage de la liste).
4. **Scores** : lettre → score par convention ÉCLAIREUR **linéaire** S 90, A 75, B 60, C 45, D 30,
   F 15 (ce n'est pas une donnée de source). Spire Codex : Codex Score 0-100 tel que publié.
   `consensus` = **médiane** des scores, une voix par source (Spire Codex : tranche toutes
   ascensions), avec `n_sources` et `spread` (max − min). Aucun poids arbitraire.
   Les catégories de Baalorlord pour les reliques d'Ancients (« Always Amazing / Always Good /
   Needs Synergy / Inconsistent / Almost Never ») restent des catégories (`score: null`).
5. **Contenu protégé** : on ne stocke jamais les commentaires des auteurs ; seulement la lettre,
   deux drapeaux factuels Untapped (`good_upgrade`, `max_copies`), des agrégats chiffrés Spire
   Codex, des noms de cartes et des paraphrases courtes pour les guides, plus le lien.

## 3. Couverture (cartes et reliques du jeu v0.107.1 ayant au moins une note)

**Variante publique (commitée)** : Spire Codex seul → cartes 82/87 (Ironclad), 82/88 (Silent,
Defect, Regent, Necrobinder), 64/64 incolores (manquent les cartes Basic et Ancient, jamais
proposées en récompense) ; reliques 8/8 par personnage, 115/115 communes, 140/140 Ancients/événements.
Une seule source par carte : pas de consensus possible dans cette variante.

**Variante complète (`local/`, toutes sources)** :

| Groupe | Cartes du pool | Notées | ≥ 2 sources | Baalorlord | Jorbs | Spire Codex | slaythetierlist |
|---|---:|---:|---:|---:|---:|---:|---:|
| Ironclad | 87 | **87** | 87 | 87 | 87 | 82 | 79 |
| Silent | 88 | **88** | 88 | 88 | 88 | 82 | 79 |
| Defect | 88 | **88** | 88 | 88 | 88 | 82 | 70 |
| Regent | 88 | **88** | 88 | 88 | 88 | 82 | 79 |
| Necrobinder | 88 | **88** | 88 | 88 | 88 | 82 | 80 |
| Incolores | 64 | **64** | 64 | 64 | 64 | 64 | — |
| **Total** | **503** | **503** | 503 | | | | |

Plus 10 cartes d'événement (`other:EventCardPool`) notées dans des listes de personnage
(`rated_for`, exclues du consensus). Spire Codex n'a pas de score pour les cartes de départ (Basic)
ni les cartes Ancient (jamais proposées en récompense) : 6 par personnage environ.

| Reliques | Dans le jeu | Notées | Baalorlord | Spire Codex |
|---|---:|---:|---:|---:|
| Personnage (×5) | 8 chacun | 8 chacun | 7 chacun | 8 chacun |
| Communes (`SharedRelicPool`) | 115 | 115 | 114 | 115 |
| Ancients / événements (`EventRelicPool`) | 140 | 140 | 101 (catégories) | 140 |

Archétypes : 63 clusters Spire Codex (20 marqués `is_playstyle=false`, runs mortes tôt) +
12 archétypes nommés de guides (Necrobinder 3, Regent 4, Defect 3, Silent 2, **Ironclad 0**).

### Correspondances incertaines (les trois premières figurent dans `card_tiers.json` → `uncertain_mappings`)

| Source | Nom source | Problème |
|---|---|---|
| untapped-baalorlord, untapped-jorbs (Silent) | `FOLLOW_THROUGH` | absent du jeu v0.107.1 (ni classe, ni localisation) : note ignorée |
| slaythetierlist (Silent) | Follow Through | idem |
| slaythetierlist (Ironclad) | Grapple | absent du jeu v0.107.1 : note ignorée |
| pcgamesn-necrobinder (relique) | Anchor | deux titres « Anchor » (`Anchor`, `FakeAnchor` = « Anchor??? ») → résolu sur `Anchor` par titre exact |

## 4. Accord entre sources — à lire avant d'utiliser `consensus`

Corrélation de rang (Spearman) entre sources, sur les cartes communes :

| | Jorbs | Spire Codex | slaythetierlist |
|---|---:|---:|---:|
| Baalorlord | 0,44 (n=503) | 0,19 (n=474) | 0,33 (n=387) |
| Jorbs | | 0,20 (n=474) | 0,22 (n=387) |
| Spire Codex | | | 0,08 (n=387) |

Les deux experts ne s'accordent que modérément, et les experts presque pas avec les données.
300 cartes sur 503 ont un `spread` ≥ 30 points. Conséquence pour le scoring (F9) : afficher
**chaque source** à côté du `consensus`, et traiter un `spread` élevé comme une confiance basse.
Le Codex Score mesure le taux de victoire des runs où la carte a été prise (biais de survie :
une carte prise en acte 3 apparaît surtout dans des runs déjà bien parties) ; il ne doit pas
être lu comme « la meilleure prise à l'acte 1 ». L'Elo Spire Codex (choix réels, modèle
Bradley-Terry, toutes versions) et la référence `skip_reference` (passer = 28,6 % des écrans)
sont fournis pour une comparaison « carte vs passer » façon HearthArena.

## 5. Ce qui est antérieur à v0.107.1 (marqué)

- `untapped-guide-shiv` : écrit à l'époque de v0.102.0 bêta → archétypes Silent `stale: true`.
- `pcgamesn-necrobinder` (2026-04-07) et `intoindiegames-regent` (2026-03-12) : antérieurs à la
  sortie de v0.107.1 (38 cartes et 10 reliques changées) → `stale: true`.
- `untapped-guide-orb-spam` : aucune date ni patch visibles → traité comme non prouvé à jour.
- Écartées pour péremption : Mobalytics (2026-04-02), BrokenBuilds (publiée 2026-04-19, avant le
  patch qu'elle revendique). Écartée car bêta : finalboss.io (v0.110.0).
- Les tiers (Untapped, slaythetierlist, Spire Codex) visent tous v0.107.1.

## 6. Limites

- **Pas de méta « officielle »** : jeu en accès anticipé, deux experts publics versionnés,
  un agrégateur de données, un site anonyme. C'est mince, et les sources divergent (§4).
- **Biais de population** Spire Codex : seulement les joueurs qui ont le mod/overlay.
- **Ironclad** : aucun guide d'archétype nommé et daté trouvé ; seulement les clusters Spire Codex.
- **Statistiques Untapped** (taux de pick/victoire) : derrière un compte OAuth, non utilisées.
- **Synergies** carte↔carte et carte↔boss : aucune source publique structurée trouvée ; à
  dériver du catalogue (couche « statique jeu ») et des runs de Mio (couche « personnelle »).
- Formule exacte du Codex Score non publiée.

## 7. Licences et conditions d'usage — ce qui a été appliqué, et ce que Mio doit décider

- **Untapped.gg** (Baalorlord, Jorbs) : CGU HearthSim — usage personnel non commercial,
  interdiction de copier, redistribuer ou scraper le contenu du service. Nous ne stockons ni
  commentaires ni ordre interne, seulement les lettres de tier attribuées avec lien. **Mais le
  dépôt `Moutdili/ultima` est public** : publier ces lettres pour toutes les cartes revient à
  republier la classification complète. Options : (a) passer le dépôt en privé ; (b) retirer les
  notes Untapped du commit et ne les produire qu'en local via `build_meta.py --fetch` (le cache est
  déjà gitignoré), comme le fait l'overlay MIT `sts2-tierlist-overlay` ; (c) demander l'accord
  d'Untapped. **Appliqué par défaut : (b)** — `sources.json` porte `redistribution: local-only`
  pour Untapped et slaythetierlist ; leurs lettres ne sont écrites que dans `data/meta/local/`
  (gitignoré). Si Mio choisit (a), il suffit de retirer `local/` du `.gitignore` et de commiter.
- **Spire Codex** : aucune condition publiée trouvée ; API documentée avec export massif destiné
  aux consommateurs ; robots.txt autorise `/api` (sauf images). Agrégats + lien.
- **slaythetierlist.com** : « Copyright © 2026 », sans licence. Même traitement que Untapped
  (`local-only`) : recopier toutes ses lettres serait une copie intégrale de la liste.
- **Guides** (PCGamesN, Into Indie Games, Untapped) : noms de cartes et paraphrases courtes.
