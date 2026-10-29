# 35 — Malédictions et statuts : classement mesuré, et le coût du retrait

> Source : 50 000 runs gagnées + 50 000 perdues (extraction `tools/explosivity/extract.py`).
> Générateur : `tools/index_curse_impact.py` → `data/catalog/curse_impact.json`.

## Méthode

Comparer les malédictions dans le deck final est trompeur : les gagnants jouent plus longtemps et en
accumulent davantage. On restreint donc aux **runs entrées en acte 3** (étage ≥ 33) — 63 634 runs,
taux de victoire de base **78,6 %** — et on compare le taux de victoire des runs ayant acquis la
carte **avant l'acte 3** à cette base.

## Classement (delta de win rate, en points)

| Carte | Type | n | Win % | Δ pp | Toxicité /100 |
|---|---|---:|---:|---:|---:|
| **POOR_SLEEP** | curse | 314 | 69,1 | **−9,5** | **97** |
| **INJURY** | curse | 5 190 | 75,5 | −3,0 | 65 |
| **DEBT** | curse | 3 188 | 77,1 | −1,4 | 57 |
| **GREED** | curse | 5 782 | 77,3 | −1,2 | 56 |
| **DOUBT** | curse | 1 163 | 77,6 | −0,9 | 55 |
| **CLUMSY** | curse | 11 305 | 78,0 | −0,6 | 53 |
| **CURSE_OF_THE_BELL** | curse | 923 | 78,0 | −0,6 | 53 |
| NORMALITY | curse | 2 206 | 80,4 | +1,9 | 41 |
| DECAY | curse | 1 792 | 80,5 | +1,9 | 40 |
| SPORE_MIND | curse | 438 | 81,0 | +2,5 | 38 |
| GUILTY | curse | 3 545 | 81,7 | +3,1 | 35 |
| SHAME | curse | 459 | 82,3 | +3,8 | 31 |
| WRITHE | curse | 582 | 83,2 | +4,6 | 27 |
| REGRET | curse | 587 | 84,7 | +6,1 | 19 |

## Lecture honnête

- **Les deltas positifs ne disent pas que la malédiction est bonne.** Beaucoup viennent d'événements
  qui donnent une contrepartie forte (relique, or, carte) : le delta mesure le **bundle**, pas la
  carte. Seuls les deltas négatifs sont interprétables comme un coût.
- **POOR_SLEEP** est la plus toxique mais n = 314 : solide sans être massif.
- **INJURY** est le signal le plus fiable (n = 5 190, −3,0 pp) : c'est elle qui doit peser le plus
  dans une pénalité par défaut.
- Les **statuts** (Burn, Wither, Beckon, Sloth, Frantic Escape) sont temporaires et presque jamais
  dans le deck final (n = 0 à 3) : le corpus ne permet pas de les classer. Il faut les traiter par
  leur effet décrit, en le disant.
- **Ascender's Bane** (A5+) est ajoutée au deck à l'étage 1, pas via `cards_gained` : elle est donc
  absente de cette mesure. À traiter comme une malédiction de départ, avec sa propre pénalité.

## Ce que ça change

1. **Pénalité de malédiction data-driven** : remplacer le `−30` fixe de `CardScorer` par une pénalité
   issue de la toxicité (POOR_SLEEP **−25**, INJURY **−17**, CLUMSY **−14**, inconnue → valeur prudente
   explicite, jamais 0).
2. **Conseiller de retrait** : classer les cartes à retirer par gain de ΔG (deck après retrait),
   malédictions d'abord ; comparer au prix réel.
3. **Coût du retrait** : A6 `Inflation` → **100 or** au lieu de 75 (`MerchantCardRemovalEntry.cs:20`).
   Un retrait qui rapporte 12 points de G ne vaut pas 100 or au milieu de l'acte 2 si le shop propose
   une relique à 150.
4. **Relique qui donne une malédiction** : sa valeur nette = ΔG(relique) − pénalité(toxicité) −
   éventuel coût de retrait. C'est exactement l'arbitrage que Mio décrit : une relique forte peut
   être un piège si la carte imposée coûte plus qu'elle ne rapporte.

## Implémentation (S25, branche `hermes/s25-curses`)

### Pénalité de score (`Scoring/Curses/CurseScorer.cs`)

`penalty = −round(toxicité × 0,26)`, borné à `[−1, −30]` : 26 points pour une toxicité de 100, soit
−25 pour POOR_SLEEP (un quart de l'échelle 0..100). Une malédiction mesurée garde toujours au moins
1 point de pénalité ; aucune ne dépasse −30 (une malédiction ne doit pas écraser une carte jouable
offerte à côté).

| Cas | Pénalité | Libellé du terme |
|---|---:|---|
| Malédiction mesurée, n ≥ 300 | `−round(tox × 0,26)` : POOR_SLEEP −25, INJURY −17, DEBT/GREED −15, DOUBT/CLUMSY/CURSE_OF_THE_BELL −14, NORMALITY −11, REGRET −5 | `Malédiction (impact mesuré)` |
| Malédiction au corpus mais n < 300 (ASCENDERS_BANE n = 6, ENTHRALLED n = 1) | −12 (médiane des malédictions classées) | `Malédiction (impact non classable)` |
| Malédiction absente du corpus (BAD_LUCK) ou catalogue absent | −12 | `Malédiction (aucune donnée mesurée)` |
| Statut (BURN, SLOTH, BECKON) | −6 | `Statut (hors deck final)` |

Le −6 des statuts est la moitié du −12 générique : même rapport que `StatusPenalty` (3) /
`CursePenalty` (6) dans G. La data ne les classe pas (n ≤ 3) et leurs toxicités (100 pour Sloth sur
1 run) sont du bruit.

**Détection.** La méta publique ne contient **aucune** malédiction (0 entrée « Curse » dans
`card_tiers.json`) : l'ancien `IsCurse(meta)` du `CardScorer` ne se déclenchait donc jamais sur une
vraie malédiction — le −30 était du code mort. La détection passe désormais par `CurseScorer`
(corpus d'impact, puis type/rareté de la méta, puis `data/catalog/cards.json`).

### Conseil de retrait (`Scoring/Deck/RemovalAdvisor.cs`)

- `DeckGain = G(deck − carte) − G(deck)` (même G que le « Fit au deck »), une copie à la fois.
- `CurseBonus = toxicité / 10` (POOR_SLEEP +10, CLUMSY +5) : G ne pénalise une malédiction que de
  6 points de consistance pondérés (≈1 point de G), très en dessous de l'effet mesuré.
- `Score = DeckGain + CurseBonus` ; `Price = 75` ou `100 à A6+` (`Inflation`, docs/34).
- `WorthIt = Score ≥ Price / 12` → 6,25 points de G à 75 or, 8,33 à 100. Un point de G vaut 12 or
  dans cet arbitrage ; en dessous, l'or est mieux dépensé ailleurs.

### Relique qui impose une malédiction (`data/catalog/relic_curses.json`)

Correspondances **prouvées par co-occurrence** sur le corpus (`final.rel` × `final.deck`), pas
déduites d'un nom :

| Relique | Malédiction | Preuve | Valeur nette |
|---|---|---|---|
| CALLING_BELL (Cloche d'appel) | CURSE_OF_THE_BELL ×1 | 1 623 / 1 623 runs avec la relique ont la carte (100 %) ; 1 623 des 1 640 runs à Curse of the Bell (99,0 %) ont la relique | note brute − 14 (`RelicScorer.NetValue`) |

Écartées, avec la raison dans le fichier : NEOWS_BONES et SERE_TALON (malédictions **aléatoires**,
aucune carte fixe : 79,6 % / 91,9 % des runs portent au moins une malédiction, mais ASCENDERS_BANE en
représente la moitié et vient du palier A5+), DARKSTONE_PERIAPT (récompense les malédictions au lieu
de les imposer).

Le coût du retrait s'ajoute en option (`includeRemovalCost`) : −6 points à 75 or, −8 à 100 or.
Réserve assumée : le consensus communautaire de la relique a été mesuré sur des runs qui l'avaient
prise, malédiction comprise — la valeur nette la soustrait donc deux fois. C'est un choix
pessimiste, étiqueté comme tel dans le terme.
