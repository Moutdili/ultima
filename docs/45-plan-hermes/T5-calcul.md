# T5 — Calcul (ton périmètre habituel)

> **2026-11-10** : d'après `status/hermes-s36.md`, §1, §3 et §4 sont faits mais non poussés ; §2 reste. Pousse-les
> en premier dans `hermes/v1-calcul` (vague 1, voie C). Les défauts T8 §2.2-2.3 et T9 §3.1-3.2 montrent que le
> retrait reste F (9 cas sur 9) et que le malus de courbe à 1 énergie persiste : à traiter dans la même branche.

**Skills** : `sts2-meta-scoring`, `sts2-combat-math`, `sts2-map-paths`, `sts2-game-api`.
**Périmètre** : `Ultima.Engine/Scoring/**`, `Ultima.Engine/Paths/**`, leurs tests, `data/**`, `tools/` de calcul.

1. **Retrait — une seule vérité** (arbitrage du chef, docs/42) : `ScoreRemoval` passe par `RemovalAdvisor` ; le prix
   est un terme informatif à 0 point ; la formule « +25 fixe − coût » disparaît. Test : retirer une malédiction note
   plus haut que retirer une Frappe au même prix. (Vérifie d'abord si c'est déjà fait.)
2. **Pouvoirs comportementaux** (204/260, inventaire `9c67513`) : couche de simulation d'événements
   (`AfterTurnEnd`, `AfterCardPlayed`…) d'un bloc ; pas de valeur inventée (effet non modélisé ⇒ `null` + source).
3. **Derniers trous** : 2 rencontres sans stats, 1 relique non notée — mesurer ou déclarer explicitement absent.
4. **Événements et Ancien** : `IEventAdvisor` sur les options réelles (le miroir 2 lit maintenant l'événement et
   l'Ancien ; le daemon les publie après T1). Notes S-F par option, termes chiffrés et sourcés.
5. Budget data ≤ 2 Mo et `tools/verify-all.sh` vert avant chaque push.
