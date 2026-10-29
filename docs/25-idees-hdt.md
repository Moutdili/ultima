# 25 — Idées reprises de Hearthstone Deck Tracker (et transposées à STS2)

> Demande de Mio (2026-10-29) : « si HDT fait quelque chose de bien, pique l'idée ».
> Chaque idée indique sa **source de données** (déjà prouvée ou non) et son **propriétaire**.
> Rappel « probabiliste seulement » : rien qui révèle un tirage caché (ordre de la pioche, prochains
> combats, rng). Calcul = Hermes ; données/daemon/affichage = chef.

## A. Suivi du deck en combat (le cœur de HDT)
| # | Idée HDT | Transposition STS2 | Données | Propriétaire |
|---|---|---|---|---|
| A1 | Cartes restantes dans le deck | Contenu de la **pioche / défausse / épuisement** en combat, compté (pas l'ordre) | miroir (piles — à cartographier) | chef (daemon) + overlay |
| A2 | Probabilité de piocher une carte | « 38 % de piocher ta Frappe enchantée au prochain tour » (combinatoire sur les comptes, sans l'ordre) | A1 | Hermes |
| A3 | Compteur de fatigue | Cartes avant le prochain **remélange** de la pioche | A1 | chef |
| A4 | Cartes générées/créées | Cartes ajoutées en combat (statuts, malédictions, cartes créées) mises en évidence | A1 | chef |
| A5 | Survol = grande image de la carte | Survol d'une ligne de l'overlay → description FR complète, enchantement, amélioration | KB | overlay |

## B. Suivi de l'adversaire
| # | Idée HDT | Transposition STS2 | Données | Propriétaire |
|---|---|---|---|---|
| B1 | Cartes jouées par l'adversaire | Pouvoirs/buffs des ennemis et ce qu'ils font, en clair | miroir (combat) + KB | chef |
| B2 | « Secrets » possibles | **Coups possibles** de l'ennemi (liste publique de son répertoire, pas le tirage) | source du jeu (bestiaire) | Hermes |
| B3 | — (spécifique STS2) | **Prévision des dégâts + coup létal** (déjà codé en v1 : `ForecastEngine`, `LethalIndex`) | miroir (intents) | Hermes (réutilise Ultima.Core) |

## C. Brouillon / choix (Arena helper de HDT/HearthArena)
| # | Idée | Transposition | Données | Propriétaire |
|---|---|---|---|---|
| C1 | Score de carte au draft | **Score récompense + shop** (en cours) | miroir ✅ | Hermes |
| C2 | Mulligan guide | **Aide au choix de l'Ancien (Neow)** : bénédictions proposées notées | miroir (à cartographier) + historique | Hermes |
| C3 | — | **Feu de camp** : se reposer ou améliorer, et quelle carte améliorer | save + KB | Hermes |
| C4 | — | **Événements** : options et conséquences connues (texte du jeu) | KB + miroir (événement courant) | chef + Hermes |
| C5 | — | **Potions** : quand les utiliser (contre quel ennemi / boss) | KB + combat | Hermes |

## D. Statistiques et historique (onglet « Stats » de HDT)
| # | Idée HDT | Transposition | Données | Propriétaire |
|---|---|---|---|---|
| D1 | Taux de victoire par deck/classe | Par personnage, archétype, ascension (tes 250 runs) | history ✅ | Hermes (calcul) + compagnon |
| D2 | Stats contre chaque adversaire | **Dégâts subis par rencontre / boss**, causes de mort | history ✅ | Hermes + compagnon |
| D3 | Historique des parties | Liste des runs : personnage, étage atteint, deck final, cause de mort | history ✅ | compagnon |
| D4 | Export / partage | Export d'une run (texte, CSV — ledger v1 `DamageLedger`/`CsvWriter`) | save + history | chef |
| D5 | Replays (HSReplay) | Lecture de `replays/latest.mcr` (format binaire inconnu) | à étudier | plus tard |

## E. Collection et progression
| # | Idée HDT | Transposition | Données | Propriétaire |
|---|---|---|---|---|
| E1 | Suivi de collection | Cartes/reliques **découvertes** vs total, déblocages (époques) | progress.save ✅ | compagnon |
| E2 | Quêtes / succès | Progression des succès (`achievements`) | progress.save (à vérifier) | compagnon |

## F. Confort (overlay HDT)
| # | Idée HDT | Transposition | Propriétaire |
|---|---|---|---|
| F1 | Overlay personnalisable (positions, tailles, éléments) | Modules + ordre (✅ contrat) ; position/échelle du panneau à ajouter | overlay + compagnon |
| F2 | Minuteur de tour / de partie | Durée du combat, de la run (la save a `run_time`) | overlay |
| F3 | Masquer automatiquement selon l'écran | Modules contextuels (✅ contrat : récompense, shop, chemins) | overlay |
| F4 | Co-op (spécifique STS2) | **Deck et reliques du coéquipier** (le jeu les synchronise) | miroir + overlay |

## Proposition d'ordre (après la vague 1)
1. A1 + A3 + A4 (piles en combat) — la fonction emblématique de HDT, données à cartographier dans le miroir.
2. B3 (dégâts + létal, code v1 réutilisable) et A2 (probabilités de pioche) — Hermes.
3. D1-D3 (stats et historique dans le compagnon) — données déjà prouvées.
4. C2-C5 (Ancien, feu, événements, potions) — Hermes.
5. E1, F2, F4 ; D5 plus tard.
