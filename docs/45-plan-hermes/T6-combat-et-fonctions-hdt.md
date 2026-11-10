# T6 — Combat et fonctions HDT (débloqué par les contrats v1.5 et v1.6)

> Remplace `T6-en-attente-contrat.md`. Les champs existent (`CombatView`, `RestSiteView`, `StatsView`).
> Le feu de camp est passé dans T8 §3 et T10 §3, les statistiques dans T11. **Il reste ici le combat.**

**À lire d'abord** : `docs/46-contrat-v15.md` §2 (Combat), `docs/25-idees-hdt.md` §A et §B, `docs/perf/MESURE.md`.
**Vagues 3 et 4**. **Dépend de** : T8 §3 (code de `CombatView` derrière `combatReads`, désactivé).
**Skills** : `sts2-combat-math`, `sts2-memory-mirror`, `sts2-daemon`, `sts2-performance`, `sts2-external-overlay`.

**Règle de cette fiche** : jusqu'à la fenêtre de test n°3, `EngineState.Combat` reste `null` dans la vraie session.
Tout se développe et se teste sur fixtures et cible simulée, avec `combatReads: false` par défaut. Mio a senti des
baisses de FPS quand les cartes sont piochées : on mesure avant d'activer.

## 1. Voie C — calcul sur les comptes (vague 3)
Entrée : un `CombatView`. Aucune lecture, aucun ordre de pioche, aucun tirage caché.
1. **A2 Probabilité de pioche** : pour chaque carte de la pioche, probabilité de la piocher au prochain tour.
   Hypergéométrique sur les comptes ; quand la pioche est plus petite que la main à tirer, traiter le remélange
   (défausse remise dans la pioche, `DrawUntilReshuffle`). Test : somme et cas limites (pioche vide, 1 carte).
2. **B3 Dégâts entrants et létal** : depuis les intentions **visibles** (`IntentView`), avec le pipeline vérifié
   de `sts2-combat-math` (Faible, Vulnérable, coups multiples). Remplit `IncomingDamage` et `Lethal`. Intention
   sans chiffre ⇒ `null`, pas 0.
3. **C5 Potions** : pour chaque potion possédée, conseil « garder / utiliser maintenant » selon l'ennemi, le
   létal et le boss à venir. Sortie = un `Score` avec ses termes, publié dans `PotionView.Score`.
4. Interface : propose la signature dans `demandes-de-contrat.md` (section v1.6) ; le chef l'ajoute au contrat.
   En attendant, code derrière une classe interne à `Ultima.Engine/Scoring/Combat/`.

## 2. Voie D — lecture et publication derrière le réglage (vague 3)
1. Finir `CombatView` : main, pioche, défausse, épuisement **comptées et triées par id**, cartes générées,
   ennemis et intentions visibles. Jamais l'ordre de la pioche (test qui le prouve sur la sortie JSON).
2. `combatReads: false` ⇒ zéro lecture mémoire en combat (test déjà demandé par T8 §3 : il doit exister et passer).
3. Cadence quand le réglage est actif : une lecture par **changement** (fin de tour, carte jouée vue dans
   `godot.log`), pas un sondage fixe. Donne le nombre de lectures par tour dans le rapport.
4. Préparer la mesure : un commutateur que le chef peut basculer en fenêtre de test sans recompiler, et le
   protocole A/B en trois passes (sans ULTIMA, ULTIMA sans lecture en combat, ULTIMA avec) selon `docs/perf`.

## 3. Voie H — modules de combat (vague 3, sur fixtures)
1. Module `combat` : pioche, défausse, épuisement en listes comptées, cartes générées marquées, compteur avant
   remélange, probabilité de pioche à côté de chaque carte quand le calcul est là.
2. Dégâts entrants et létal en tête du module. `Lethal == true` ⇒ ton `danger`.
3. Aucune minuterie propre : le module ne se redessine que sur un nouvel état. Aucun travail au repos.
4. Demande de contrat attendue : id de module `combat` dans `ModuleIds`. Écris-la, le chef l'ajoute.

## 4. Vague 4 — après la fenêtre de test n°3
Le chef donne le résultat de la mesure FPS. Trois issues possibles :
| Résultat | Suite |
|---|---|
| Pas de perte mesurable | `combatReads` activable depuis le TUI et le compagnon, `combat` publié |
| Perte pendant la lecture seulement | Réduire la cadence (une lecture par tour), remesurer |
| Perte même sans lecture en combat | Le problème est dans le HUD (compositing, flou) : fiche séparée du chef, `combat` reste fermé |

Voie S en vague 4 : réglage `combatReads` dans le TUI et le compagnon, avec l'avertissement « mesuré le … ».

## 5. Critères
- Probabilités de pioche : tests de valeur sur trois decks écrits à la main, dont un avec remélange.
- Létal : les cas de `sts2-combat-math` (lequel des coups d'une séquence N×M tue) passent.
- Sortie JSON de `combat` : aucune liste dans l'ordre de la pioche (test), aucune donnée non visible à l'écran.
- `combatReads: false` ⇒ zéro lecture en combat (test). `tools/verify-all.sh` vert.
- Rapport : nombre de lectures par tour, coût d'une lecture sur cible simulée, gestes pour la fenêtre n°3.
