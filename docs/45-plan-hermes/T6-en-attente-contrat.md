# T6 — En attente d'un contrat v1.5 du chef (ne pas commencer)

Ces fonctions (docs/25) demandent de nouveaux champs. Le chef les ajoutera après la fenêtre de test n°2
(mesure FPS des lectures mémoire en combat). Elles sont listées ici pour que tu puisses préparer le calcul.

| Idée (docs/25) | Ce qui manque | Calcul attendu ensuite |
|---|---|---|
| A1/A3/A4 piles en combat (pioche/défausse/épuisement comptées, avant remélange, cartes générées) | `CombatView` avec piles comptées (miroir 2 les lit déjà, sans ordre) | A2 : probabilité de piocher une carte au prochain tour (combinatoire sur les comptes) |
| B3 dégâts entrants + coup létal | intentions ennemies dans `CombatView` | prévision et létal (`sts2-combat-math`) |
| C3 feu de camp (se reposer / améliorer quoi) | `RestSiteView` | note des options |
| C5 potions (quand les utiliser) | `CombatView` | conseil par ennemi/boss |
| D1-D3 statistiques et historique (taux de victoire, dégâts par rencontre, liste des runs) | `StatsView` (historique) | agrégats depuis `history/*.run` |
| F4 co-op réel (deck et reliques du coéquipier) | validation sur une vraie partie co-op | — |
