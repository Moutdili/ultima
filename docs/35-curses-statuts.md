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
   issue de la toxicité (POOR_SLEEP ≈ −25, INJURY ≈ −18, CLUMSY ≈ −12, inconnue → valeur prudente
   explicite, jamais 0).
2. **Conseiller de retrait** : classer les cartes à retirer par gain de ΔG (deck après retrait),
   malédictions d'abord ; comparer au prix réel.
3. **Coût du retrait** : A6 `Inflation` → **100 or** au lieu de 75 (`MerchantCardRemovalEntry.cs:20`).
   Un retrait qui rapporte 12 points de G ne vaut pas 100 or au milieu de l'acte 2 si le shop propose
   une relique à 150.
4. **Relique qui donne une malédiction** : sa valeur nette = ΔG(relique) − pénalité(toxicité) −
   éventuel coût de retrait. C'est exactement l'arbitrage que Mio décrit : une relique forte peut
   être un piège si la carte imposée coûte plus qu'elle ne rapporte.
