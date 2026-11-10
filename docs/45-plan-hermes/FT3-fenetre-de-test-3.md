# FT3 — Fenêtre de test n°3 sur le vrai jeu (chef + Mio)

> Ce fichier est pour le chef et Mio. **Hermes ne lance rien de ce qui suit** : il le complète (§4) avec les
> gestes à faire pour vérifier ce qu'il a livré. Se tient après le merge de la vague 2.
> Procédure d'installation et de retour arrière : `overlay/gnome/TEST-WINDOW.md`. Mesure FPS : `docs/perf/MESURE.md`.

## 1. Ce qui reste de la fenêtre n°2 (jamais fait)
| # | Situation | Ce qu'on regarde |
|---|---|---|
| 1 | Événement `?` et Ancien | Options lues avant le choix, texte FR, note par option, « — » si texte absent |
| 2 | Feu de camp | Options visibles, note, carte conseillée pour l'amélioration |
| 3 | Compagnon | Fenêtre ouverte pendant une run, réglages écrits puis appliqués par le HUD |
| 4 | Co-op en direct | Deck et reliques des alliés, module `party`, personnage de chaque joueur |
| 5 | Mesure FPS | Voir §3 |

## 2. Ce que les vagues 1 et 2 ajoutent
| # | Livré par | Geste | Attendu |
|---|---|---|---|
| 6 | T7 | Ouvrir une récompense de 3 cartes | Une case sous chaque carte, note S-F, à ± 12 px du centre |
| 7 | T7 | Prendre une carte | La case disparaît en moins de 1,5 s |
| 8 | T7 | Ouvrir le shop, acheter une carte puis une relique | Cases sous les 13 articles et le retrait ; l'article acheté perd sa case |
| 9 | T7 | Survoler une case, puis le HUD | Opacité 0,8 ⇒ 1,0, la case se développe ; les clics vont toujours au jeu |
| 10 | T10 | Relancer une récompense (si la relique s'y prête) | Les cases suivent les nouvelles cartes |
| 11 | T10 | Atteindre le dernier acte en A10 | Deux boss affichés dans le HUD et le TUI |
| 12 | T10 | Feu de camp avec plusieurs actions | Plan affiché en tête, cible du soin = allié le plus bas |
| 13 | T10 | Dépasser 3 potions | Autant de cases que d'emplacements |
| 14 | T8 §2 | `ultima font list` après installation | Polices du jeu présentes (extraction locale) |
| 15 | T8 §2 | Shop de l'acte 1 avec un deck de départ | Retrait d'une Frappe qui n'est plus F ; notes moins tassées |

## 3. Mesure FPS (MangoHud, protocole `docs/perf/MESURE.md`)
Sur secteur, même combat, même deck, trois passes de 3 minutes par configuration :
| Passe | Configuration | Question |
|---|---|---|
| A | ULTIMA arrêté | Référence |
| B | ULTIMA, HUD ouvert, `combatReads: false` | Coût du HUD seul (le symptôme de Mio : baisse quand on pioche) |
| C | ULTIMA, `combatReads: true` | Coût des lectures en combat |
| D | Shop et récompense, relecture 1 Hz de T7 | Coût de la relecture |

À rendre : moyenne, 1 % bas, pire image, pour chaque passe. Décision du chef ensuite (T6 §4).

## 4. Gestes ajoutés par Hermes
Une ligne par chose livrée qui n'a tourné que sur fixture ou cible simulée : le geste, l'attendu, le fichier de
journal à regarder. À remplir à la fin de chaque vague.

| # | Voie | Geste | Attendu | Journal |
|---|---|---|---|---|
| | | | | |

## 5. Preuves
Captures et journaux dans `/home/mio/sts2-proofs/` (hors dépôt, jamais commités). Le chef écrit le résultat dans
`STATUS.md`, section « Fenêtre de test n°3 ».
