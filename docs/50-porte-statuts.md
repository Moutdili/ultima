# 50 — Porte de sortie du modèle de statuts

> Plan : « si le backtest ne fait pas monter l'AUC, on s'arrête là ». Résultat : **l'AUC n'est pas
> calculable** avec ce corpus, et la mesure de rechange explique pourquoi. Décision prise et écrite.

## 1. Ce qui a été fait avant la mesure

- **Directions qualifiées par le code, pas par le nom du hook** : `tools/powers/qualify_direction.py`
  lit la garde de camp de chaque hook (`target != base.Owner` → subi, `dealer != base.Owner` →
  infligé). Résultat : **29 pouvoirs modélisés** sur les 36 qui touchent aux dégâts (contre 12 avant),
  chacun avec son fichier:ligne en justification.
- **Nouveau canal `taken_add`** : Tainted ajoute des dégâts **subis** (`TaintedPower.cs:18`), ce que
  le modèle ne savait pas représenter. Pipeline : `(base + plats attaquant + plats cible) × mult
  attaquant × mult cible`, puis plafond, puis 0.
- 7 pouvoirs restent **non modélisés avec leur raison** : valeur dans `DynamicVars` (shrink, slow,
  soar), exponentielle (shadowmeld), dépend de l'état (intercept), condition sur la cible
  (tracking), formule par stack non linéaire (lethality).

## 2. La mesure demandée n'existe pas dans la data

Le corpus ne porte **aucun état de pouvoir par tour** : on sait quelles cartes ont été prises,
jamais quels statuts étaient actifs au moment d'un coup. Une AUC « avec pouvoir activé » demanderait
cette information ; l'inventer serait pire que de ne pas la calculer.

## 3. La mesure de rechange, et ce qu'elle montre

`tools/threat_report` confronte `MaxHit` (majorant par tour) aux dégâts réellement observés,
sur 80 rencontres mesurées :

    ratio médian 8,28 · p10 4,04 · p90 15,85 · dispersion entre rencontres 3,9x
    80/80 rencontres : MaxHit > 2x l'observé

**Pourquoi un facteur 8 ?** Parce que les deux nombres ne mesurent pas la même chose :
`MaxHit` est un **majorant avant bloc**, les dégâts observés sont **après bloc**. Un joueur qui
bloque 30 sur 34 ne subit que 4. Cet axe ne peut donc pas arbitrer un modificateur de ±25-50 %.

Ce que ça interdit : prétendre que « les statuts n'ont pas d'effet » parce que le ratio ne bouge pas.
Ce que ça autorise : dire que **cet axe ne mesure pas la question**.

## 4. Décision

- On **garde** les 29 pouvoirs modélisés : ils sortent du code, coûtent zéro donnée, et sont testés
  (Colossus 0,5 subi, Double Damage x2 infligé, Tainted +Amount subi, Hard to Kill plafond Amount).
- On **s'arrête** sur les 175 pouvoirs conditionnels : les modéliser demanderait la machine à états
  du combat, pour un effet qui reste dans le bruit de l'axe mesuré.
- On **ne présente pas** de gain AUC : il n'a pas été mesuré, donc il n'existe pas.

## 5. Ce qui rouvrirait la porte

1. des **replays** (8 % des runs) : `hit.dmg` + `mods` donnent les statuts réels au moment du coup,
   et là une validation au coup près devient possible ;
2. un **export de combat** avec les pouvoirs actifs par tour (aucun aujourd'hui) ;
3. une mesure **après bloc** côté prédiction (le bloc est connu en combat par le miroir, pas hors
   ligne).

Tant qu'aucune des trois n'est disponible, le modèle de statuts reste **exact sur ce qu'il modélise**
et **muet sur ce qu'il ignore** — c'est la règle des trois états (docs/22).
