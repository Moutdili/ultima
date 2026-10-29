# 40 — Calcul de la route (path planner)

> La route décide autant que les cartes : à état égal, enchaîner les élites ou sécuriser un repos
> change la run. `Ultima.Engine/Scoring/Paths/PathPlanner.cs` calcule la **meilleure suite de
> salles** jusqu'au boss par programmation dynamique arrière sur la carte.
> Générateur de mesures : `tools/rooms/measure.py` → `data/calibration/rooms.json`.

## Ce que dit la data

60 000 runs solo, **conditionnés sur l'arrivée en acte 2** (pour retirer les morts précoces) :

| Élites dans l'acte 1 | n | win rate | PV % fin d'acte 1 |
|---:|---:|---:|---:|
| 0 | 4 021 | **15,5 %** | 40,0 |
| 1 | 8 780 | 20,4 % | 38,9 |
| 2 | 11 209 | 29,3 % | 38,8 |
| 3 | 6 485 | **40,8 %** | 38,5 |
| 4 | 962 | 52,9 % | 42,3 |

Les élites rapportent plus qu'elles ne coûtent : les PV de fin d'acte 1 sont **identiques** quel
que soit le nombre d'élites, alors que le win rate double. (Biais de sélection résiduel : les bons
joueurs prennent plus d'élites. Mais le signal est trop fort pour être ignoré.)

Dégâts et or mesurés par salle (acte 1, pleine vie) :

| Salle | Dégâts | Or |
|---|---:|---:|
| monstre | 5,4 | +13,2 |
| élite | 21,0 | +36,0 |
| trésor | 0,0 | +40,6 |
| événement | 3,0 | +10,1 |
| boutique | 0,0 | −129,8 |
| boss | 34,7 | +80,6 |

## Le calcul

```
V(nœud, PV) = valeur(salle, PV) + max( V(successeur, PV après la salle) )
```

- `valeur(élite) = 8 (relique) + 3 (carte) + 36 × 0,06 (or) − 21 × poidsPV`
- `valeur(repos) = max(soin 30 % des PV max × 0,2 ; 3 (amélioration))`
- `valeur(boutique) = +6 si or ≥ 120, sinon −2`
- `poidsPV = 0,35 × (1 + (50 − PV%) / 50)` : un PV vaut plus cher quand on est bas
- **un combat qui met les PV à 0 ajoute +100 de pénalité** : la DP ne choisit jamais une route
  qui tue, même si la salle rapporte gros.

La DP est mémoïsée sur `(nœud, PV)`, les cycles sont coupés, et chaque étape porte une phrase
chiffrée (« élite : relique + carte + 36 or − 21 PV »).

## Ce que ça change (testé)

- À **15/70 PV** : la route passe par le **repos** (une élite à 15 PV est mortelle).
- À **70/70 PV**, une seule élite avant le boss : la route prend l'**élite**.
- À **70/70 PV**, deux élites enchaînées : la route prend **quand même le repos** — la somme des
  dégâts (2 × 21 + boss 34,7) dépasse les PV, alors qu'une salle par salle l'élite semblait
  meilleure. C'est exactement le piège du calcul local.
- Boutique : ignorée avec 40 or, prise avec 300.

## Limites

- Le modèle ne connaît pas encore la **position exacte** des nœuds (étage, branche) : il suppose
  que les successeurs sont les bons et ne pénalise pas un détour inutile.
- Les dégâts des actes 2 et 3 sont **extrapolés** (×1,3 et ×1,6), pas mesurés par acte.
- Le deck n'entre pas dans la valeur d'une salle (une élite vaut plus si le deck vient de gagner
  une carte) : couplage prévu avec `PlayerStrength`.
- Le contrat n'expose pas encore la carte (`MapView`) : le mod devra fournir `nodes` + `next`.
