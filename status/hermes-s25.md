# S25 — Malédictions : pénalité mesurée et conseiller de retrait

Branche `hermes/s25-curses`, worktree `~/wt-s25` (depuis `main` ad434f3, rebasée sur ebe3ad1).
Remplace le **−30 forfaitaire** des malédictions par une pénalité tirée de l'impact mesuré
(`data/catalog/curse_impact.json`, 63 634 runs entrées en acte 3, base 78,6 %), ajoute un
**conseiller de retrait** qui compare le gain de G au prix réel (75 or, **100 à A6+** `Inflation`,
docs/34), et une **valeur nette** pour les reliques qui imposent une malédiction.

## 1. Formule de pénalité (`Scoring/Curses/CurseScorer.cs`)

```
penalty(carte) = −round(toxicité × 0,26)          si malédiction mesurée (n ≥ 300), borné [−1, −30]
               = −12                              si malédiction au corpus mais n < 300
               = −12                              si malédiction absente du corpus (aucune donnée)
               = −6                               si statut de combat (forfait faible documenté)
               = 0                                sinon (carte normale : jamais pénalisée)
```

| Constante | Valeur | Justification |
|---|---:|---|
| `CurseScorer.ToxicityScale` | **0,26** | 26 points pour une toxicité de 100 ⇒ POOR_SLEEP (97) −25, INJURY (65) −17, CLUMSY (53) −14 : les trois ancres de docs/35 |
| `CurseScorer.UnmeasuredPenalty` | **−12** | pénalité médiane des malédictions classées ; jamais 0, un zéro affirmerait « inoffensive » |
| `CurseScorer.StatusPenalty` | **−6** | moitié du générique, même rapport que `StatusPenalty` 3 / `CursePenalty` 6 dans G ; la data ne classe pas les statuts (n ≤ 3) |
| `CurseScorer.MinPenalty` | **−30** | borne basse : une malédiction ne doit pas écraser une carte jouable offerte à côté |

Pénalités effectives : POOR_SLEEP **−25**, INJURY **−17**, DEBT/GREED **−15**, DOUBT/CLUMSY/
CURSE_OF_THE_BELL **−14**, ASCENDERS_BANE **−12** (n = 6, non classable), NORMALITY **−11**,
REGRET **−5**, ENTHRALLED **−12** (n = 1), BAD_LUCK **−12** (absent du corpus), BURN/SLOTH **−6**.

**Correction de fond trouvée en route** : la méta publique ne contient **aucune** malédiction
(0 entrée « Curse » dans `card_tiers.json`, vérifié sur les 6 personnages). L'ancien
`if (IsCurse(meta))` du `CardScorer` ne se déclenchait donc **jamais** sur une vraie malédiction :
le −30 était du code mort. La détection passe maintenant par `CurseScorer` (corpus d'impact → type/
rareté de la méta → `data/catalog/cards.json`). Test de non-régression :
`Facade_actually_attaches_a_curse_term_to_a_real_curse`.

## 2. Exemples chiffrés

Terme posé dans `ScoreCard` : libellé `Malédiction (impact mesuré)`, source
« curse_impact.json — 63 634 runs entrées en acte 3 (base 78,57 %) : POOR_SLEEP −9,46 pp,
toxicité 97/100 sur 314 runs ; −round(97 × 0,26) = −25 ».

| Cas | Avant (S25) | Après | Terme |
|---|---|---|---|
| POOR_SLEEP en récompense, méta réelle | **aucun terme**, total 0 | terme **−25** sourcé, total 0 (aucune note communautaire pour une malédiction : le total reste borné à 0) | `Malédiction (impact mesuré) −25` |
| CLUMSY, même contexte | aucun terme | terme **−14** | `Malédiction (impact mesuré) −14` |
| POOR_SLEEP avec une base positive (méta de test 60) | −30 ⇒ **16** | **17** (= 46 − 25 − 4 de fit) | idem |
| CLUMSY avec la même base | −30 ⇒ **16** | **28** (= 46 − 14 − 4) | idem |
| BAD_LUCK (malédiction absente du corpus) | aucun terme | terme **−12** « aucune donnée mesurée » | `Malédiction (aucune donnée mesurée)` |
| ASCENDERS_BANE (n = 6) | aucun terme | terme **−12** « n = 6 < 300 : non classable » | `Malédiction (impact non classable)` |
| BURN (statut) | aucun terme | terme **−6** forfaitaire | `Statut (hors deck final)` |

Honnêteté sur l'effet visible : dans le jeu réel une malédiction n'a aucune note communautaire, donc
son **total** reste 0 dans les deux versions (borne 0..100 du contrat). Ce qui change, c'est que le
terme existe, qu'il est chiffré et sourcé (avant : rien), qu'il distingue deux malédictions dès
qu'une base positive existe (historique perso, enchantement), et qu'il alimente le conseiller de
retrait.

## 3. Conseiller de retrait (`Scoring/Deck/RemovalAdvisor.cs`)

```
DeckGain  = G(deck − carte) − G(deck)          // même G que le « Fit au deck », une copie à la fois
CurseBonus = toxicité / 10                     // POOR_SLEEP +10, CLUMSY +5 (malédictions et statuts)
Score      = DeckGain + CurseBonus             // ordre de classement
Price      = 75  (A0..A5) | 100 (A6+)          // Inflation, docs/34
WorthIt    = Score ≥ Price / 12                // seuil : 6,25 à 75 or, 8,33 à 100
Affordable = Price ≤ or du joueur
```

`PointsPerGold = 12` : un point de G vaut 12 or dans cet arbitrage. Pourquoi c'est nécessaire : G ne
pénalise une malédiction que de 6 points de consistance **pondérés** (≈1 point de G), alors que
l'effet mesuré est de −9,5 pp de victoire ; sans le bonus de toxicité, une Frappe et Poor Sleep
sortiraient à égalité.

Deck Silent de 12 cartes (4 Frappe, 4 Défense, Neutralize, Survivor, Poor Sleep, Clumsy),
62/70 PV, acte 1 :

| Carte | ΔG | Bonus toxicité | Score | A5 (75) | A6 (100) |
|---|---:|---:|---:|---|---|
| Poor Sleep | +2 | +10 | **12** | rentable | **rentable** |
| Clumsy | +2 | +5 | **7** | rentable (7 ≥ 6,25) | **non rentable** (7 < 8,33) |
| Frappe (×4) | 0 | 0 | 0 | non | non |
| Défense (×4) | 0 | 0 | 0 | non | non |
| Survivor | −1 | 0 | −1 | non | non |
| Neutralize | −3 | 0 | −3 | non | non |

C'est le cas d'usage de docs/35 §3 : le même retrait de Clumsy cesse d'être rentable quand
l'ascension passe à 6, alors que Poor Sleep le reste. Les 4 Frappes sont groupées (`Copies = 4`,
une seule copie retirée par achat).

## 4. Reliques qui imposent une malédiction (`data/catalog/relic_curses.json`)

Correspondances **mesurées par co-occurrence** sur le corpus (`final.rel` × `final.deck`), pas
déduites d'un nom :

- **CALLING_BELL → CURSE_OF_THE_BELL ×1** : 1 623 / 1 623 runs portant la relique ont la carte
  (100 %) ; 1 623 des 1 640 runs contenant cette malédiction (99,0 %) portent la relique.
  `RelicScorer.NetValue` : note brute **33** → nette **19** (−14), **11** si l'on ajoute le retrait
  à A6 (−8 points = 100 or / 12).
- **Écartées, avec la raison dans le fichier** : NEOWS_BONES et SERE_TALON (malédictions aléatoires,
  aucune carte fixe : 79,6 % / 91,9 % des runs ont au moins une malédiction, mais ASCENDERS_BANE en
  représente la moitié et vient du palier A5+), DARKSTONE_PERIAPT (récompense les malédictions au
  lieu de les imposer). Elles ne sont **pas** chiffrées : `NetValue` renvoie la note brute + une
  ligne à 0 point « Malédiction aléatoire (nombre non mesuré) ».

Réserve assumée : le consensus communautaire d'une relique a été mesuré sur des runs qui l'avaient
prise, malédiction comprise — la valeur nette la soustrait donc deux fois. C'est un choix
pessimiste, étiqueté dans le terme, et qui ne remplace pas `Score()`.

## 5. Tests

`env -u ULTIMA_DATA bash tools/hermes-verify.sh` → **PASS** (build `-warnaserror` propre, **181/181**
après rebasage sur main, dont 22 `CurseTests`, aucun champ interdit, aucun stub, contrats intacts).

**Variable d'environnement à signaler (pas masquée)** : ce shell exporte `ULTIMA_DATA=/home/mio/wt-s23`,
un worktree qui ne contient ni `catalog/curse_impact.json` (S25) ni `catalog/constraints.json` (S19).
Avec cette variable, `hermes-verify.sh` finit en **HOLD** : 9 tests `ThreatConstraintTests` de S19
échouent. Vérifié sur un checkout **vierge de `origin/main`** (sans aucune modification S25) : mêmes
9 échecs, donc **pré-existants et sans lien avec cette branche**. Les tests S25, eux, chargent
explicitement `data/catalog` depuis la racine du dépôt : ils passent dans les deux environnements.

`Ultima.Engine.Tests/Scoring/CurseTests.cs` — 22 tests :

1. catalogue : 63 634 runs, base 78,57 %, seuil 300, ≥ 17 entrées, POOR_SLEEP tox 97, INJURY n = 5 190 ;
2. clé canonique (`POOR_SLEEP` = `CARD.POOR_SLEEP` = `PoorSleep`) ;
3. barème : −25 / −17 / −14, monotonie, plus de −30, deux malédictions ≠ même pénalité ;
4. source du terme mesuré : « 63 634 », « POOR_SLEEP », « 97/100 » ;
5. POOR_SLEEP coûte plus cher que CLUMSY dans le score (base égale : 17 vs 28) ;
6. malédiction inconnue (BAD_LUCK) : −12 explicite, jamais 0, terme présent dans `ScoreCard` ;
7. n < 300 (ASCENDERS_BANE) : libellé « non classable », source « n = 6 » ;
8. statut (BURN, SLOTH) : −6, libellé dédié, plus faible qu'une malédiction mesurée ;
9. carte normale : aucun terme, pénalité 0 ;
10. catalogue absent : −12 explicite (jamais 0) ;
11. prix 75 à A5 / 100 à A6, seuils 6,25 et 8,33 ;
12. le conseiller classe la malédiction avant la Frappe, groupe les 4 exemplaires, bonus 10 ;
13. rentabilité : Poor Sleep rentable aux deux paliers, Clumsy seulement à A5, seuil = prix/12 ;
14. abordabilité : 100 or > 90 or ⇒ `Affordable = false` mais `WorthIt = true` ;
15. deck vide : liste vide, pas d'exception ;
16. `relic_curses.json` : une seule correspondance chiffrée, 3 reliques rejetées ;
17. relique à malédiction : net < brut, −14, terme mesuré présent ;
18. coût de retrait : −6 à A5, −8 à A6, net A6 < net A5 ;
19. relique à malédictions aléatoires : note inchangée + ligne 0 point ;
20. relique sans malédiction : net = brut = `Score()` ;
21-22. façade : terme de malédiction réellement posé sur `CARD.POOR_SLEEP`, `Scorer.Removal` exposé.

## 6. Non prouvé / limites

- **Le classement est observationnel**, pas causal : les deltas positifs (REGRET +6,1 pp, WRITHE
  +4,6) mesurent un bundle événement + contrepartie. C'est pourquoi une toxicité basse donne une
  pénalité faible (−5) et jamais un bonus.
- **POOR_SLEEP n = 314** : la plus toxique, mais l'échantillon reste modeste ; INJURY (n = 5 190)
  est le signal le plus fiable, et c'est elle que la pente 0,26 place au second rang.
- **Statuts non classables** : n = 0 à 3 dans le corpus ; le −6 est un forfait argumenté par le
  rapport G (3 vs 6), pas une mesure.
- **Le seuil 12 or/point de G n'est pas calibré** : il vient de l'ordre de grandeur de docs/35 §3
  (« 12 points de G ne valent pas 100 or »). Le backtest devra le régler comme `FitScale` (S14).
- **ΔG de retrait sature** : sur un deck de 12 cartes, retirer une Frappe rend 0 point de G (les
  dimensions sont des parts) ; le conseiller ne distingue donc pas « 4 Frappes » de « 3 Frappes ».
  C'est une propriété du G actuel, pas du conseiller.
- **Double compte résiduel dans la note de deck** : `DeckEvaluator` ajoute encore `−8` par
  malédiction par-dessus la moyenne des `ScoreCard`. En pratique l'effet est nul sur la moyenne
  (une malédiction vaut 0 dans les deux versions, aucune note communautaire), mais le terme `−8`
  reste un doublon conceptuel du terme mesuré — hors périmètre S25 (formule figée docs/24 §2), à
  arbitrer par le propriétaire de la note de deck.
- **Valeur nette des reliques = lecture pessimiste** (voir §4) : le consensus inclut déjà la
  malédiction.
- **`ULTIMA_DATA`** : l'environnement de cette session le pointe sur `/home/mio/wt-s23`, un
  worktree **sans** `curse_impact.json` (ni `constraints.json`). Les tests de S25 chargent donc
  explicitement `data/catalog` depuis la racine du dépôt (aucun test ne dépend de la variable) ;
  mais un processus lancé avec ce `ULTIMA_DATA` verrait toutes les malédictions en « aucune donnée
  mesurée » (−12). Signalé, pas masqué : c'est `CurseCatalog.Problem` qui portera le chemin
  cherché. À lancer avec `ULTIMA_DATA` pointant sur un dossier `data/` qui contient
  `catalog/curse_impact.json`, ou sans la variable (cf. §5 pour les 9 échecs S19 pré-existants).

## 7. Fichiers

Créés : `Ultima.Engine/Scoring/Curses/CurseCatalog.cs`, `Ultima.Engine/Scoring/Curses/CurseScorer.cs`,
`Ultima.Engine/Scoring/Deck/RemovalAdvisor.cs`, `Ultima.Engine/Scoring/Relics/RelicCurseCatalog.cs`,
`data/catalog/relic_curses.json`, `Ultima.Engine.Tests/Scoring/CurseTests.cs`, ce fichier.

Modifiés : `Scoring/Cards/CardScorer.cs` (terme de malédiction + injection `CurseScorer`),
`Scoring/Shop/ShopScorer.cs` (préfixe de libellé, la détection ne dépend plus du libellé exact),
`Scoring/Relics/RelicScorer.cs` (`NetValue` / `RelicNetValue`), `Scoring/Scorer.cs` (façade :
`Removal`, `ScoreRelicNet`), `docs/35-curses-statuts.md` (§Implémentation).

Non touchés (interdits) : `Contracts/**`, `Saves/**`, `ThreatModel.cs`, `StrengthWeights.cs`.
