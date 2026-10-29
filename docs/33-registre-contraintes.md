# 33 — Registre des contraintes (ce qui est modélisé, ce qui manque)

> Audit du 2026-10-29 sur le code source décompilé (`~/sts2/game/pck/src/Core`, fedora).
> But : ne pas juger un deck sur une mécanique qu'on aurait oubliée.

## 1. La surface réelle

| Mécanique | Nombre | Preuve (source) |
|---|---:|---|
| Pouvoirs implémentés | **260** | `Models/Powers/*.cs` |
| Multiplicateurs de dégâts (`ModifyDamageMultiplicative`) | **25** | Vulnerable, Weak, Flanking, Soar, Shrink, Colossus, Conqueror, Intercept, DoubleDamage, Gigantification, Surrounded, Guarded, Knockdown, Slow, Lethality, Covered, Hang… |
| Modificateurs de pioche (`ModifyHandDraw`) | **17** | reliques + pouvoirs |
| Modificateurs d'énergie max (`ModifyMaxEnergy`) | **16** | reliques + pouvoirs |
| Ajouts de dégâts (`ModifyDamageAdditive`) | **11** | Force, Lethality, etc. |
| Multiplicateurs de bloc (`ModifyBlockMultiplicative`) | **8** | Frail, couvertures… |
| Cartes jouées en plus (`ModifyCardPlayCount`) | **7** | Burst, Double Tap… |
| Plafonds de dégâts (`ModifyDamageCap`) | **2** | `IntangiblePower` (cap 1), `HardToKillPower` (cap = Amount) |
| Paliers d'ascension | **10** | DeadlyEnemies (223 usages), ToughEnemies (173), Scarcity, Poverty, Inflation, WearyTraveler, TightBelt, SwarmingElites, DoubleBoss, AscendersBane |
| Monstres à invocation / phase / respawn | **11+** | Fogmog, Ovicopter, BygoneEffigy, CeremonialBeast, TestSubject, TheObscura, LivingFog, Fabricator, ToughEgg, TwoTailedRat… |

## 2. Ce qui est modélisé aujourd'hui

| Contrainte | État | Où |
|---|---|---|
| Dégâts des moves, variante ascension | ✅ | `ThreatModel.MaxHit`, seuil **A10 mesuré** sur replays |
| PV des monstres, variante ascension | ⚠️ | `HpAt` bascule dès A1 — **incohérent** avec le seuil A10 ; non mesuré |
| Plafond de dégâts (Intangible/HardToKill) | ⏳ | agent S19 en cours |
| Respawn (TestSubject) | ⏳ | agent S19 en cours |
| Vulnérable / Faiblesse (dégâts subis) | ✅ | `ThreatModel.EffectiveHp` (×1,5 / ×0,75) |
| Force / Poison / Focus (scaling) | ✅ | `PlayerStrength` dimension Offense |
| Frail (réduction de bloc) | ❌ | absent |
| Multiplicateurs de dégâts (25) | ❌ | seuls Vulnérable/Faiblesse sont pris |
| Énergie max (+16) | ❌ | G suppose 3 énergie/tour |
| Pioche (+17) | partiel | pioche des cartes seulement, pas des reliques/pouvoirs |
| Cartes jouées en plus (7) | ❌ | absent |
| Copies de monstres | ❌ | `rooms.monster_ids` les contient, `ThreatModel` ne les compte pas |
| Phases / invulnérabilité de boss | ❌ | absent |
| Invocations / minions | ❌ | absent (le codex ne les chiffre pas) |
| Ascension hors dégâts/PV (Scarcity, DoubleBoss, AscendersBane…) | ❌ | absent |
| Prix marchand (`ModifyMerchantPrice`) | ❌ | `ShopScorer` ignore les réductions de prix des reliques |
| Ordre de mélange (`ModifyShuffleOrder`) | ❌ | absent (jeu supposé uniforme) |
| Valeur X (`ModifyXValue`) | partiel | X supposé = 1 énergie |
| Co-op (revive 1 PV, reliques perso, coffre 3, RPS) | ❌ | L3 non implémenté |
| Cartes de soutien co-op (13 : 8 `AnyAlly`, 5 `AllAllies`) | partiel | `CoopScorer` : ΔG sur le deck de chaque allié, cession si écart ≥ +3 (seuil **non calibré**) — `status/hermes-s23.md`, index `data/catalog/coop_cards.json` |
| Reliques (effets numériques) | partiel | converties en tags |
| Potions | partiel | tags dérivés de la description FR |

## 3. Les cinq omissions qui changent un verdict

1. **Les 25 multiplicateurs de dégâts.** Un deck de scaling (Force, Lethality, Double Damage) est sous-évalué : G est linéaire en dégâts. C'est la première brique à ajouter.
2. **Énergie et pioche des reliques (33 hooks).** Lantern (+1 énergie au premier tour), Bag of Preparation (+2 cartes) changent la courbe entière. G suppose 3 énergie et la pioche des cartes seules.
3. **Les copies de monstres.** `rooms.monster_ids` donne 3 exosquelettes ; `ThreatModel.MaxHit` en compte un. Sous-estimation jusqu'à ×4 sur les combats à copies.
4. **Phases et invulnérabilité.** Un boss qui ne peut pas être touché un tour invalide tout calcul « tuer en 2 tours » — c'est exactement le raisonnement de Mio sur les plafonneurs.
5. **Ascension hors dégâts/PV.** DoubleBoss, Scarcity, AscendersBane modifient la run entière, pas seulement les coups.

## 4. Décision de méthode

On ne code pas les 260 pouvoirs. On code **les contraintes qui changent un classement** :

- plafond de dégâts → pénalise le burst, valorise le poison / les dégâts fixes ;
- respawn → pénalise le burst mono-cible, valorise la zone ;
- copies de monstres → multiplie la menace et valorise la zone ;
- multiplicateurs (Force/Vulnérable/Faible) → corrige l'offense et la survie ;
- énergie/pioche des reliques → corrige la consistance.

Chaque contrainte doit être **sourcée** (fichier + ligne ou mesure sur corpus) et **testée** (un cas qui change de grade). Une contrainte sans cas de test qui change un verdict est une décoration.
