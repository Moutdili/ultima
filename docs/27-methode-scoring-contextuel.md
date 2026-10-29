# 27 — Méthode de scoring contextuel (réflexion, 2026-10-29)

> But : passer d'une note « force intrinsèque » à une note **« que vaut ce choix pour CE joueur,
> dans CE deck, à cet instant »** — et pouvoir l'expliquer en une phrase sourcée.

## 1. Ce qui existe déjà (mesuré)

| Brique | État | Preuve |
|---|---|---|
| Intrinsèque carte | rang Spire Codex → 20..75 | `Calibration_*` (2 tests) |
| Intrinsèque relique | rang → 20..78 | `RelicScorer` |
| Synergie deck (cartes) | tags + clusters d'archétype, plafond +5 | `SynergyEngine`, `CardTags` |
| Synergie relique | pool du perso +1, cluster +1..3 (max +5) | `RelicScorer.Synergy` |
| Boss de l'acte | `BossProfile` (plus gros coup, traits) | `CardScorer` |
| Besoin PV (reliques) | **nouveau** : 0/5/10/15 selon la bande de PV | `RelicScorer.Needs`, 4 tests |
| Explication | chaque terme = libellé + points + source | `ScoreTerm` |
| Source lisible | « Spire Codex — consensus 78/100 » | `CardScorer.SourceLabel` |
| Équipe / co-op | **absent** | `RunView` ne porte qu'un joueur |

Exemple réel obtenu : `RELIC.MANGO` → **59 (B)** à 70/70 PV, **74 (A)** à 18/70 PV.

## 2. Principe directeur

Un score n'est pas une note de tier-list : c'est **une recommandation d'action**, avec une raison.

Conséquences :
1. On affiche **deux chiffres distincts** : le tier intrinsèque (Spire Codex) et la note contextuelle.
   Le joueur doit voir « Spire Codex : B · pour toi : A (+15 besoin PV) » — jamais un chiffre unique
   qui mélange les deux sans le dire.
2. La note contextuelle est **additive** : chaque point a un libellé et une preuve. Pas de réseau de
   neurones, pas de score magique : on doit pouvoir contester chaque terme.
3. La note de **deck** reste stable (indépendante des PV) ; ce sont les notes d'**items** qui bougent.
   Sinon le joueur voit sa note de deck chuter quand il prend un coup, et ne comprend plus.

## 3. Les quatre couches

```
note(item, joueur) = L0 intrinsèque
                   + L1 fit deck
                   + L2 état / besoin
                   + L3 équipe (co-op seulement)
   borné 0..100, lettre S/A/B/C/D/F
```

### L0 — intrinsèque (0..78)
Rang du consensus Spire Codex dans la table chargée (pas la valeur brute : les notes locales ont
une autre échelle). Inchangé, déjà testé.

### L1 — fit deck (±20)
Objectif : qu'une carte « nulle en général » mais parfaite pour ce deck puisse **dépasser** sa base.
D'où ±20 : une carte à 45 (C) + fit 20 + besoin 10 = 75 (A). On ne ment pas sur sa faiblesse
intrinsèque (elle reste affichée), on dit qu'ici elle vaut mieux.

Modèle par **manques**, pas par comptage de tags :
- mesurer le deck : densité de bloc, dégâts par tour, mise à l'échelle (Force/Poison), pioche/énergie,
  dégâts de zone, cartes de fin de tour ;
- comparer à des cibles par acte (ex. bloc ≈ 12/tour à l'acte 2) ;
- la carte rapporte d'autant plus qu'elle **comble un manque** ; si le manque est déjà comblé → 0 ;
- anti-synergie explicite (carte Poison dans un deck Force sans multiplicateur) → terme négatif.

Les cartes **incolores** suivent exactement la même règle : leur valeur de fit se calcule sur le deck
du joueur, pas sur leur couleur. C'est le cas « carte incolore parfaite ici » de Mio.

### L2 — état / besoin (0..15, parfois négatif)
- PV : bande ≥80 % → 0 ; 60-80 % → 5 ; 40-60 % → 10 ; <40 % → 15 (reliques de PV max/soin, potions).
- Or : une carte/relique chère perd des points quand la bourse est vide (terme négatif borné).
- Boss connu : les cartes qui contrent son profil gagnent (déjà en place).
- Malédictions : le retrait de carte gagne (déjà en place).

### L3 — équipe (co-op)
Le point clé : **le score d'équipe n'est pas une somme**. Un objet ne peut aller qu'à un joueur ;
la question est « qui doit le prendre ? ».

```
pourChaqueJoueur p : note_p = note(item, ctx_p)
teamValue(p) = note_p + bonusCouverture(p) − redondance(p, équipe)
recommandation = argmax_p teamValue(p)
```

- `bonusCouverture` : +10 si l'objet corrige le **maillon faible** (le joueur le plus proche de la
  mort, ou celui qui n'a aucune source de bloc/soin) — c'est le cas « le pote à 20 PV ».
- `redondance` : −5 si le besoin est déjà couvert **chez ce joueur** (deux reliques de PV max).
- Le score d'équipe affiché = celui du joueur recommandé, avec la mention « pour X ».

→ Aucune nouvelle interface de scoring n'est nécessaire : `ScoringContext` est déjà par joueur.
Il faut seulement que le daemon publie **tous** les joueurs (contrat v1.2 : `PartyView`).

## 4. L'explication (exigence « raisonnement clair et net »)

Règle : **pas de phrase sans chiffre derrière**. On génère la phrase à partir des termes triés :

> MANGO — Spire Codex B (59). Pour toi (70/70 PV) : rien ne presse.
> Pour ton pote (18/70) : **A (74)** → +15 parce qu'il est sous 40 % de PV, +5 cluster Poison
> (3 cartes clés dans son deck).

Chaque terme porte déjà sa source (Spire Codex, historique perso, description du jeu). On ajoute
`Evidence` au terme (v1.2) pour citer les cartes concernées plutôt qu'un simple compteur.

## 5. Comment on saura que c'est meilleur (le plus important)

Sans mesure, on ne fait que déplacer des chiffres. Trois niveaux :

1. **Invariants (tests unitaires, immédiats)**
   - monotonie : plus de PV manquants ⇒ note d'une relique de PV max ≥ ;
   - bornes 0..100, pas de score sans source, note de deck stable selon les PV ;
   - une bascule de lettre exige un écart ≥ 5 points (pas de faux mouvement).
2. **Backtest sur l'historique réel (~247 runs)**
   - rejouer chaque décision (récompense, shop) avec l'état de l'époque ;
   - comparer la recommandation à ce que Mio a pris (accord), et le taux de victoire des runs où il
     a suivi vs non suivi — **corrélation, pas causalité**, à dire tel quel ;
   - sert à **régler les poids** (L1/L2) : on ne les invente pas à la main.
3. **Mode ombre + 10 situations annotées (S7)**
   - le daemon calcule et journalise ses recommandations sans les afficher ;
   - Mio note 10 décisions réelles (accord/désaccord, utile/inutile) → mesure honnête de l'utilité.

## 6. Ordre de construction

1. **Fait** : besoin PV sur les reliques, catalogue reliques (304), libellés Spire Codex, 4 tests.
2. Fit deck par manques (±20) + anti-synergie — le cœur de « la carte nulle mais parfaite ici ».
3. Backtest sur l'historique → réglage des poids.
4. Co-op : `PartyView` (contrat v1.2) + `TeamAdvisor` (L3).
5. Modules `ultima` (`hud`, `logs --grep`) — cosmétique, en dernier.

## 7. Questions ouvertes (décisions de Mio)

1. Co-op : que se passe-t-il quand un joueur meurt (fin de run ? réanimation ?) — et les reliques
   sont-elles partagées ou par joueur ? Le L3 en dépend.
2. Amplitude : une relique de PV max peut-elle passer de B à **S** à PV très bas, ou A est-il le
   plafond contextuel (S réservé à la force intrinsèque) ?
3. Affichage : deux notes (tier + contexte) ou une seule avec le détail au survol ?
4. Le mode ombre doit-il tourner dès maintenant pendant tes runs, pour accumuler des données ?
