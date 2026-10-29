# 27 — Méthode de scoring contextuel (réflexion, 2026-10-29)

> But : passer d'une note « force intrinsèque » à une note **« que vaut ce choix pour CE joueur,
> dans CE deck, à cet instant »** — et pouvoir l'expliquer en une phrase sourcée.

## 1. Ce qui existe déjà (mesuré)

| Brique | État | Preuve |
|---|---|---|
| Intrinsèque carte | rang Spire Codex → 20..75 | `Calibration_*` (2 tests) |
| Intrinsèque relique | rang → 20..78 | `RelicScorer` |
| Synergie deck (cartes) | tags + clusters d'archétype, plafond +5 | `SynergyEngine`, `CardTags` |
| Synergie relique | pool du perso +1, cluster +1..3 (max +5) | `RelicScorer.Synergy` |
| Boss de l'acte | `BossProfile` (plus gros coup, traits) | `CardScorer` |
| Besoin PV (reliques) | **nouveau** : 0/5/10/15 selon la bande de PV | `RelicScorer.Needs`, 4 tests |
| Explication | chaque terme = libellé + points + source | `ScoreTerm` |
| Source lisible | « Spire Codex — consensus 78/100 » | `CardScorer.SourceLabel` |
| Équipe / co-op | **absent** | `RunView` ne porte qu'un joueur |

Exemple réel obtenu : `RELIC.MANGO` → **59 (B)** à 70/70 PV, **74 (A)** à 18/70 PV.

## 2. Principe directeur

Un score n'est pas une note de tier-list : c'est **une recommandation d'action**, avec une raison.

Conséquences :
1. On affiche **deux chiffres distincts** : le tier intrinsèque (Spire Codex) et la note contextuelle.
   Le joueur doit voir « Spire Codex : B · pour toi : A (+15 besoin PV) » — jamais un chiffre unique
   qui mélange les deux sans le dire.
2. La note contextuelle est **additive** : chaque point a un libellé et une preuve. Pas de réseau de
   neurones, pas de score magique : on doit pouvoir contester chaque terme.
3. La note de **deck** reste stable (indépendante des PV) ; ce sont les notes d'**items** qui bougent.
   Sinon le joueur voit sa note de deck chuter quand il prend un coup, et ne comprend plus.

## 3. Les quatre couches

```
note(item, joueur) = L0 intrinsèque
                   + L1 fit deck
                   + L2 état / besoin
                   + L3 équipe (co-op seulement)
   borné 0..100, lettre S/A/B/C/D/F
```

### L0 — intrinsèque (0..78)
Rang du consensus Spire Codex dans la table chargée (pas la valeur brute : les notes locales ont
une autre échelle). Inchangé, déjà testé.

### L1 — fit deck (±20)
Objectif : qu'une carte « nulle en général » mais parfaite pour ce deck puisse **dépasser** sa base.
D'où ±20 : une carte à 45 (C) + fit 20 + besoin 10 = 75 (A). On ne ment pas sur sa faiblesse
intrinsèque (elle reste affichée), on dit qu'ici elle vaut mieux.

Modèle par **manques**, pas par comptage de tags :
- mesurer le deck : densité de bloc, dégâts par tour, mise à l'échelle (Force/Poison), pioche/énergie,
  dégâts de zone, cartes de fin de tour ;
- comparer à des cibles par acte (ex. bloc ≈ 12/tour à l'acte 2) ;
- la carte rapporte d'autant plus qu'elle **comble un manque** ; si le manque est déjà comblé → 0 ;
- anti-synergie explicite (carte Poison dans un deck Force sans multiplicateur) → terme négatif.

Les cartes **incolores** suivent exactement la même règle : leur valeur de fit se calcule sur le deck
du joueur, pas sur leur couleur. C'est le cas « carte incolore parfaite ici » de Mio.

### L2 — état / besoin (0..15, parfois négatif)
- PV : bande ≥80 % → 0 ; 60-80 % → 5 ; 40-60 % → 10 ; <40 % → 15 (reliques de PV max/soin, potions).
- Or : une carte/relique chère perd des points quand la bourse est vide (terme négatif borné).
- Boss connu : les cartes qui contrent son profil gagnent (déjà en place).
- Malédictions : le retrait de carte gagne (déjà en place).

### L3 — équipe (co-op)
Le point clé : **le score d'équipe n'est pas une somme**. Un objet ne peut aller qu'à un joueur ;
la question est « qui doit le prendre ? ».

```
pourChaqueJoueur p : note_p = note(item, ctx_p)
teamValue(p) = note_p + bonusCouverture(p) − redondance(p, équipe)
recommandation = argmax_p teamValue(p)
```

- `bonusCouverture` : +10 si l'objet corrige le **maillon faible** (le joueur le plus proche de la
  mort, ou celui qui n'a aucune source de bloc/soin) — c'est le cas « le pote à 20 PV ».
- `redondance` : −5 si le besoin est déjà couvert **chez ce joueur** (deux reliques de PV max).
- Le score d'équipe affiché = celui du joueur recommandé, avec la mention « pour X ».

→ Aucune nouvelle interface de scoring n'est nécessaire : `ScoringContext` est déjà par joueur.
Il faut seulement que le daemon publie **tous** les joueurs (contrat v1.2 : `PartyView`).

## 4. L'explication (exigence « raisonnement clair et net »)

Règle : **pas de phrase sans chiffre derrière**. On génère la phrase à partir des termes triés :

> MANGO — Spire Codex B (59). Pour toi (70/70 PV) : rien ne presse.
> Pour ton pote (18/70) : **A (74)** → +15 parce qu'il est sous 40 % de PV, +5 cluster Poison
> (3 cartes clés dans son deck).

Chaque terme porte déjà sa source (Spire Codex, historique perso, description du jeu). On ajoute
`Evidence` au terme (v1.2) pour citer les cartes concernées plutôt qu'un simple compteur.

## 5. Comment on saura que c'est meilleur (le plus important)

Sans mesure, on ne fait que déplacer des chiffres. Trois niveaux :

1. **Invariants (tests unitaires, immédiats)**
   - monotonie : plus de PV manquants ⇒ note d'une relique de PV max ≥ ;
   - bornes 0..100, pas de score sans source, note de deck stable selon les PV ;
   - une bascule de lettre exige un écart ≥ 5 points (pas de faux mouvement).
2. **Backtest sur l'historique réel (~247 runs)**
   - rejouer chaque décision (récompense, shop) avec l'état de l'époque ;
   - comparer la recommandation à ce que Mio a pris (accord), et le taux de victoire des runs où il
     a suivi vs non suivi — **corrélation, pas causalité**, à dire tel quel ;
   - sert à **régler les poids** (L1/L2) : on ne les invente pas à la main.
3. **Mode ombre + 10 situations annotées (S7)**
   - le daemon calcule et journalise ses recommandations sans les afficher ;
   - Mio note 10 décisions réelles (accord/désaccord, utile/inutile) → mesure honnête de l'utilité.

## 6. Ordre de construction

1. **Fait** : besoin PV sur les reliques, catalogue reliques (304), libellés Spire Codex, 4 tests.
2. Fit deck par manques (±20) + anti-synergie — le cœur de « la carte nulle mais parfaite ici ».
3. Backtest sur l'historique → réglage des poids.
4. Co-op : `PartyView` (contrat v1.2) + `TeamAdvisor` (L3).
5. Modules `ultima` (`hud`, `logs --grep`) — cosmétique, en dernier.

## 7. Décisions de Mio (2026-10-29)

1. **Co-op** : un allié mort pendant un combat est **réanimé à 1 PV** si le groupe passe le combat.
   Les reliques sont **personnelles**. Coffre : 3 reliques pour 3 joueurs ; si 2-3 joueurs veulent la
   même, **feuille-pierre-ciseaux** tranche.
   → Conséquence : L3 ne peut pas être une somme ni un partage. Il produit un **classement d'envie**
   (« qui la veut le plus ») et un conseil de tie-break : si l'écart de ΔG est < 3, c'est un vrai
   match nul → RPS ; au-delà, le tracker dit à qui la donner.
2. **Amplitude** : le fit parfait peut aller jusqu'à **S**. Une relique situationnelle C-tier qui
   s'imbrique exactement dans le gameplan doit pouvoir sortir S. L'intrinsèque reste affiché à part.
3. **Affichage** : la charte sera faite avec Claude (emplacements). Base = le score seul ; le
   raisonnement complet (texte) est disponible pour qui veut creuser.
4. **Data** : le mode ombre sur ses runs est trop maigre → on prend la data publique (voir §8).

## 7bis. Le score global du joueur (G) — l'idée centrale

Mio : « un scoring général du player pour que chaque choix soit récompensé par l'idempotence des
autres facteurs ». Formulation retenue :

```
G(joueur) = w_s·Survie + w_o·Offense + w_c·Consistance + w_v·Couverture
            (chaque dimension normalisée 0..100, poids par acte)
valeur d'un item = ΔG = G(état + item) − G(état)
note affichée = clamp(50 + 2·ΔG + prior_intrinsèque_faible)
```

Pourquoi c'est la bonne forme :
- **Idempotence** : les dimensions saturent (rendements décroissants). Une 2e relique de bloc quand le
  bloc est déjà couvert vaut ≈ 0 ; la 1re qui comble un trou vaut beaucoup.
- **Cohérence** : cartes, reliques, potions, retrait, choix de chemin parlent tous la même langue (ΔG).
- **Co-op** : chaque joueur a son G ; la relique vaut max_p(ΔG_p) → « qui la veut le plus ».
- **PV au début du combat** : la survie utilise les **PV effectifs** (PV × mitigation des statuts que
  le boss applique au tour 1), pas les PV bruts. Un boss qui applique Faiblesse/Vulnérabilité change
  la valeur d'un point de PV.
- **S atteignable** : un ΔG énorme (item qui double une dimension) porte la note à 85+, donc S.
  L'intrinsèque n'est qu'un prior ; c'est le fit qui décide.

Prérequis data : les **valeurs numériques** des cartes (dégâts, bloc, montants) ne sont pas dans les
descriptions. Il faut les extraire du code du jeu (`src/Core`, modèles de cartes) vers
`data/catalog/cards.json` — sinon G ne peut pas calculer dégâts/tour ni bloc/tour.

## 8. Data publique (réponse à « il y a quelqu'un sur le net ? »)

Oui, et largement :

- **spire-codex.com** expose une API publique : `/api/runs/list` (50/page, **~10 000 runs**, borne
  basse annoncée) et `/api/runs/shared/{hash}` qui renvoie le **JSON complet du jeu** (mêmes champs
  que `saves/history/*.run` : `map_point_history`, `player_stats` par joueur, `card_choices`,
  `relic_choices`, `was_picked`).
- **Export complet** : `/api/exports/runs/manifest` → 2 466 928 runs au total, 8,54 Go, JSONL gzippé
  par version. Pour v0.107.1 : **785 121 runs, 2,63 Go, sha256 vérifié** (téléchargé).
- `tools/fetch_codex_static.py` → `data/codex/` : 577 cartes (498 avec valeurs numériques),
  115 monstres (15 boss, dégâts par ascension), 87 rencontres, 50 combats avec `avg_damage`/`avg_turns`,
  archétypes, stats communauté (2,24 M runs).
- `tools/index_runs.py` → `data/index/runs-v0.107.1.jsonl.gz` : **785 121 runs, 19 039 107 décisions
  de cartes** (28,3 % de skip), 516 Mo, 401 s. **309 276 runs co-op (39,4 %)**, win rate 20,9 %.
- Replays combat : inexistants (1 run sur 273) → pas de données tour par tour.
- Le corpus contient du **co-op** (échantillon : 5 runs à 2 joueurs, 3 à 3 joueurs) — indispensable
  pour calibrer L3.
- ⚠️ Le jeu public est déjà en **v0.111.0** : sur l'échantillon, 29/60 runs sont sur ce patch, 31 sur
  v0.107.1. Il faut filtrer par `build_id` (ou assumer le mélange et le documenter).

Ce corpus remplace le mode ombre comme source de vérité pour le backtest ; les 10 situations
annotées de Mio restent la mesure d'utilité subjective.
