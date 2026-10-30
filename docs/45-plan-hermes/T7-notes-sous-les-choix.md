# T7 — Notes sous les choix, façon HDT (récompenses et shop)

> Demande de Mio (fenêtre de test n°2, 2026-10-30) : « comme sur Hearthstone Deck Tracker : les notes au moment des
> picks sont en dessous ». Dans le shop, sous chaque carte, relique, potion (et le retrait), une petite case avec la
> note S-F, **toujours visible** pendant le choix ; au survol, elle se développe avec des infos pratiques (notes de
> la communauté…). Idem sous les cartes d'une récompense. **Dépend du contrat v1.5** (§4) : le chef le livre d'abord.

**Skills** : `sts2-external-overlay`, `sts2-design-system` (§8-12), `sts2-engine-contract`, `sts2-memory-mirror`,
`sts2-daemon`, `sts2-performance`, `sts2-game-api`.

## 1. Exigences de Mio (mot pour mot quand c'est possible)
1. Case sous chaque option, note S-F aux couleurs du thème ; **survol ⇒ elle se développe** (consensus communautaire,
   nombre de sources et écart, ΔG/ajustement au deck, raison principale, prix et abordabilité au shop).
2. « Quand on achète quelque chose ou qu'on pick la carte désirée, le score disparaît **aussitôt**. »
3. « Réduis l'opacité du texte à **0,8** pour la note et tout, mais quand on passe dessus l'opacité passe à **1,0**. »
   (S'applique aux cases ET au texte du HUD.)
4. Le nombre d'options varie (1, 2, 3, 4 cartes…) : **aucune position codée à la main par cas**.

## 2. Géométrie (prouvée sur le vrai jeu, 1920×1200 à 125 %, captures locales du chef)
- Le jeu rend en **1920×1080 avec deux bandes noires de 60 px** (haut et bas), sans étirement 16:10 :
  `écran = scène + (0, 60)` sur cet écran. Généraliser : viewport 1920×1080, `stretch canvas_items` +
  `aspect expand` (`game/pck/project.godot`) ⇒ calculer l'échelle et la bande pour toute résolution, tests sur
  1920×1080, 1920×1200, 2560×1440, 3440×1440, 1536×960.
- **Récompense** : `NCardRewardSelectionScreen.cs` l.131/153 — carte i parmi N à
  `x = centre_rangée + (i − (N−1)/2) × 350` (unités de scène). Mesuré (N = 3) : centres x = 610 / 960 / 1310,
  cartes de y ≈ 505 à 843 ⇒ cases à y ≈ 870 ; bouton « Skip » centré (960, 980) ⇒ note « passer » à côté.
- **Shop** (`scenes/merchant/merchant_inventory.tscn`, emplacements fixes) — mesuré à l'écran :
  | Emplacements | Centres x | Prix à y | Case à y |
  |---|---|---|---|
  | 5 cartes du personnage | 437, 700, 960, 1219, 1485 | ≈ 603 | ≈ 630 |
  | 2 cartes incolores | 502, 776 | ≈ 977 | ≈ 1005 |
  | 3 reliques | 990, 1143, 1293 | ≈ 785 | ≈ 810 |
  | 3 potions | 990, 1143, 1290 | ≈ 926 | ≈ 955 |
  | Retrait | 1490 | ≈ 950 | ≈ 978 |
  Les positions doivent être **calculées depuis la scène** (ancrages + offsets) et ce tableau sert de test de
  vérité (tolérance ± 12 px). Les coordonnées sont des faits ; ne jamais commiter de capture ni d'asset du jeu.
- Animation d'ouverture de l'inventaire (glissement) : n'afficher les cases qu'inventaire **ouvert et immobile**.

## 3. Disparition immédiate (point 2)
Prouvé : le jeu n'écrit **rien** dans `godot.log` lors d'un achat, et ne sauvegarde la run qu'en **quittant** le
shop ; aucune ligne non plus quand on choisit la carte (« selected card reward » = ouverture du choix). Donc :
relecture mémoire **~1 Hz uniquement en Shop et Récompense** (jamais en combat), arrêt dès la sortie de l'écran ;
option vendue/choisie ⇒ case retirée à la publication suivante. Budget : une sonde ≈ 270-320 ms en lecture seule ;
**mesure FPS obligatoire** (MangoHud, protocole `docs/perf`) avec et sans relecture, à présenter au chef.

## 4. Contrat v1.5 (chef — ne pas implémenter avant sa livraison)
- `ShopItem.Slot` (`card:0..4`, `colorless:0..1`, `relic:0..2`, `potion:0..2`) et `ShopView.InventoryOpen`.
- `RewardView` : ordre de `Offered` = ordre de l'écran (déjà vrai, à garantir) + nombre d'options.
- `Score` : détail communautaire structuré pour le survol (consensus, sources, écart, taux de prise si connu).
- Signal « option consommée » (vendue / choisie) publié dès la relecture.

## 5. Travail (après v1.5)
1. **Miroir** : emplacement de chaque article, état ouvert de l'inventaire, article vendu ; aucune donnée cachée.
2. **Daemon** : relecture ~1 Hz en Shop/Récompense seulement, remplit les champs v1.5, retire l'option consommée.
3. **HUD** : couche de cases ancrées (acteurs non réactifs ; survol détecté par la position du pointeur, les clics
   vont toujours au jeu), opacité 0,8 → 1,0 au survol (cases et texte du HUD), développement au survol, thème et
   police v1.4, aucune minuterie au repos hors Shop/Récompense. Réglable dans `appearance.hud` (activer, taille).
4. **Console/compagnon** : réglage « notes sous les choix » (activer, taille) dans l'onglet Apparence.

## 6. Critères
- `tools/verify-all.sh` vert ; positions calculées == tableau §2 à ± 12 px (test), N = 1 à 4 cartes testés.
- Option achetée/choisie retirée en ≤ 1,5 s (test sur cible simulée) ; aucune lecture mémoire en combat (test).
- Rapport avec la liste des gestes pour la fenêtre de test suivante (le chef et Mio vérifient sur le vrai jeu).
