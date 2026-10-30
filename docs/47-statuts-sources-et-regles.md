# 47 — Statuts : qui en pose, sur qui, et ce que ça change aux dégâts

> Demande du chef : « réfléchir à toutes les cartes qui ajoutent des statuts, style les cartes
> co-op, et les effets que les mobs peuvent appliquer au joueur ou au mob lui-même ».
> Trois familles, un seul index, aucune valeur inventée.

## 1. Inventaire (mesuré dans le code du jeu)

`tools/status/index_sources.py` lit les sources (`PowerCmd.Apply<T>(ctx, cible, montant, source, carte)`)
et produit `data/catalog/status_sources.json` :

    238 cartes appliquent un pouvoir      192 sur elles-mêmes · 75 sur l'ennemi · 2 indéterminées
     96 monstres appliquent un pouvoir    131 sur eux-mêmes  · 50 sur le joueur · 8 indéterminées
    458 applications · 228 pouvoirs distincts · 0 pouvoir inconnu du catalogue

Chaque application porte : pouvoir, cible (`self`/`enemy`/`unknown`), montant (expression du code),
méthode (`OnPlay`, `AfterAddedToRoom`…), ligne. Les cartes co-op sont indexées comme les autres —
elles appliquent à `base.Owner.Creature` (le porteur) ou à la cible choisie.

## 2. Règles de dégâts (générées, jamais écrites à la main)

`tools/status/build_status_rules.py` lit les hooks déjà extraits dans `powers.json` et produit
`data/catalog/status_rules.json` :

    36 pouvoirs ont un hook qui touche aux dégâts ou à l'armure
    12 sont modélisés (direction connue + valeur numérique)
    24 sont publiés « non modélisés » avec leur raison — la direction (infligé / subi / armure)
       n'est pas déductible du nom du hook : Weak et Vulnerable utilisent tous deux
       ModifyDamageMultiplicative, c'est le pipeline qui les distingue

Valeurs, toutes issues du code :

    weak        ModifyDamageMultiplicative 0.75
    vulnerable  ModifyDamageMultiplicative 1.5
    frail       ModifyBlockMultiplicative  0.75
    intangible  ModifyDamageCap            1
    strength    ModifyDamageAdditive       Amount (par stack)
    dexterity   ModifyBlockAdditive        Amount (par stack)

## 3. Le calcul

`StatusRules.Incoming(dégâts, statuts attaquant, statuts cible)` applique, dans cet ordre :

    (dégâts + plat_attaquant) × multiplicateur_attaquant × multiplicateur_cible
    puis plafond éventuel (Intangible), puis borne 0

Exemples testés : 10 dégâts avec Faiblesse = 8 · avec Vulnérabilité = 15 · les deux = 11 ·
40 dégâts contre Intangible = 1 · 10 + Force 3 = 13 · 10 armure avec Dextérité 2 et Frail = 9.

`StatusRules.BlockGained(base, statuts)` fait la même chose côté armure.

`ThreatModel.PredictedMaxHit(rencontre, ascension, statutsMonstre, statutsJoueur)` branche tout ça
sur le pire coup déjà calculé : sans statuts, le résultat est identique à `MaxHit`.

## 4. Ce que le modèle ne fait pas (et le dit)

- **La rotation n'est pas modélisée.** On sait qu'Axebot peut poser Weak et Frail, on ne sait pas
  quand. `StatusSources.Monster(id)` donne les applications et leur ligne ; prédire la séquence
  demanderait la rotation, que docs/22 interdit d'inventer.
- **24 pouvoirs restent non modélisés** (colossus, conqueror, gigantification…). Ils sont listés
  par `StatusRules.Unqualified` avec leur raison, jamais appliqués au hasard.
- **Poison, Doom, Plating, Thorns, Artifact, Buffer, Regen** n'ont pas de hook de dégâts dans
  l'index : ils ne modifient pas un coup, ils agissent sur le tour. Ils restent hors du calcul de
  coup, et le barème de statuts (docs/41) les note séparément.

## 5. Fichiers

    tools/status/index_sources.py       index du code (à lancer sur la machine du jeu)
    tools/status/build_status_rules.py  génère les règles depuis powers.json
    data/catalog/status_sources.json    50 Ko  (clés compactes p/t/a/m/l)
    data/catalog/status_rules.json      11 Ko
    Ultima.Engine/Scoring/Status/StatusRules.cs
    Ultima.Engine/Scoring/Status/StatusSources.cs
