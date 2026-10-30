# Miroir 2 — rapport (2026-10-30)

Branche `v2/miroir2`, rebasée sur `main` = `96b0f7a` (aucun conflit). Auteur des commits : Mio.

| Commit | Contenu |
|---|---|
| `a9bea0a` feat(mirror) | bibliothèque : `ReadEvent`, `ReadPiles` (comptées), `CardView.Enchantment`, `ReadUnorderedPile` + `ReadGuard.CheckUnordered`, 23 champs de plus (79) |
| `0a43ad8` feat(mirror) | cible de test : scènes `event`, `ancient`, `event-stale`, piles, enchantements ; harnais `mirror-proof` (refuse tout autre processus) |
| `f459b1c` docs(mirror) | `object-paths.md` §7-10, `read-only-proof.md` §6, campagne rejouée (`evidence/miroir2/20-33`) |
| `ee137d4` test(mirror) | nouveau `Ultima.Mirror.Tests` (38 tests), ajouté à `Ultima.Mirror.slnx` |

## 1. Fait

- **Événement courant** (`--sections event`) : id de la salle (`EventRoom.CanonicalEvent`), options AFFICHÉES
  (`EventModel._currentOptions` de la copie locale de `EventSynchronizer._events`), verrou, ancien, fini, relique
  d'option. Recoupement « propriétaire == joueur local ET id == id de la salle », sinon `optionsKnown=false`,
  `options=null` : jamais les options d'un autre événement.
- **Piles de combat** (`--sections piles`) : main, pioche, défausse, épuisement, jeu, rendues **comptées et triées**
  (id, amélioration, enchantement). La pioche n'est lisible que par `MirrorSession.ReadUnorderedPile`, qui
  efface l'ordre avant de rendre ; `ReadGuard.Check` refuse toujours `<DrawPile>k__BackingField`.
- **Enchantement** sur toute carte lue (deck, récompense, shop, main, piles) : `{"id":"ENCHANTMENT.X","amount":N}` ou `null`.
- **Format aligné sur le daemon de main** (écart trouvé et corrigé côté miroir) : le daemon
  (`Ultima.Daemon/Mirror/MirrorModels.cs`) attend `name`, `options[].label`, `options[].description` en
  **chaînes** et `options[].id`. Le travail interrompu émettait des objets `{table,key}` et `textKey` :
  `MirrorResult.Parse` aurait levé une `JsonException`, rendant la section `event` **toujours null**.
  Désormais : `id` = `TextKey`, `available` = `!IsLocked`, chaînes d'affichage = `null` (le miroir n'appelle
  aucune méthode du jeu, donc ne rend aucun texte), clés de localisation à côté (`nameKey`, `titleKey`,
  `descriptionKey`), ignorées par le daemon actuel.
- Le probe CLI (Câbleur, `78e23f6`) appelle `GameReader.ReadEvent()/ReadPiles()` par réflexion : **non modifié**.
  `GameReader.Miroir2Sections` a été supprimé (inutile depuis cette CLI).

## 2. Prouvé (cible de test `Sts2Shape.TestTarget` UNIQUEMENT)

```
$ bash docs/mirror/evidence/miroir2/run-campaign.sh          # 2026-10-30
20-build.txt     0 Warning(s) 0 Error(s)   (Ultima.Mirror.slnx + ProofRunner)
21-layout.txt    79/79 chemins présents dans la métadonnée ; exit=0
22-guard.txt     GUARD OK ; self-check exit=0
28-draw-order    [piles] 40 sondes : 1 sortie(s) distincte(s)   (cible rebat sa pioche toutes les 5 ms)
29-endurance     event 20/20, ancient 20/20, event-stale 20/20, shop 20/20
30-leaks         SECRET : 0 dans chaque fichier ; total strace : ptrace=0 process_vm_writev=0
31-source-lines  79 vérifiés, 0 écart(s)
32-probe-json    ultima-mirror probe --pid <cible> --json --sections run,reward,shop,event,piles
                 event exit=0 ptrace=0 process_vm_writev=0 process_vm_readv=7419 kill=1 (kill(pid,0))
                 event-stale → "options": null, "optionsKnown": false ; ancient → isAncient true, relicId
33-tests         Passed! - Failed: 0, Passed: 38, Total: 38 - Ultima.Mirror.Tests.dll
```

```
$ dotnet test Ultima.sln          (identique à main)
Passed! 64 Saves, 63 Knowledge, 252 Engine, 39 Daemon — 0 échec
```
(1 avertissement xUnit1031 préexistant dans `Ultima.Saves.Tests`, hors périmètre.)

Le test `Sortie_reelle_de_la_CLI_lue_par_le_daemon` passe les lignes BRUTES de la CLI
(`evidence/miroir2/32-probe-json-*.jsonl`) dans le vrai `MirrorResult.Parse` du daemon : `Event` non null,
`unsupported` vide, ids/available attendus, aucune occurrence de `SECRET`, pioche triée.

## 3. NON prouvé

- **Rien de tout cela n'a tourné sur le vrai jeu.** Aucune lecture, attache ni énumération du processus
  `SlayTheSpire2`. Les chemins sont vérifiés dans la métadonnée de `sts2.dll` (fichier, non chargé) et le source,
  et les valeurs uniquement sur la cible de test, qui imite la forme des types.
- Que `_events[slot]` suit le slot de `RunState._players` en co-op (déduit du source, solo seulement sur la cible).
- Les événements à mise en page `Custom`/`Combat` (ex. Crystal Sphere) : l'écran peut différer des options lues.
- La fréquence des lectures INCONNU sur les piles pendant une animation de pioche réelle, et le coût en FPS
  d'une sonde `piles` en combat (~7 000 `process_vm_readv` par sonde complète sur la cible).
- Que la table `nameKey` déduite (`events`/`ancients`) correspond à toutes les entrées de la KB.

## 4. Reste à faire (fenêtre de test n°2, avec Mio, sur le modèle de `docs/mirror/test-window.md`)

1. `ultima-mirror layout` sur le `sts2.dll` installé (attendu 79/79).
2. Salle d'événement normale : `probe --json --sections event` avant le clic, puis comparer à l'écran (id, nombre
   d'options, option verrouillée). Ensuite un ancien (Neow) : `isAncient`, `relicId` par option.
3. Juste après l'entrée en salle : vérifier si `optionsKnown=false` apparaît (préchargement) et combien de temps.
4. En combat (une seule sonde, hors animation) : `--sections piles` comparé aux écrans pioche, défausse et
   épuisement (le jeu les montre triés), avec mesure MangoHud.
5. Une carte enchantée dans le deck ou en récompense : `enchantment.id/amount` comparé à l'écran.

## 5. Ce qu'il faut côté daemon (je n'y ai pas touché ; propriétaire : Câbleur)

- **Événement : rien d'obligatoire**, le format est déjà lu. Améliorations souhaitées :
  - rendre `name` et `options[].label/description` via `Ultima.Knowledge` avec `nameKey`/`titleKey`/`descriptionKey`
    (`table` + `key`) ; sinon l'overlay affiche l'id nu (`ABSTAIN`) ;
  - si `optionsKnown == false` (options `null`), relancer une sonde `event` après l'anti-rebond (≥ 250 ms) au lieu
    d'attendre la transition suivante ;
  - si `isFinished == true`, afficher le seul bouton « Continuer » (absent des options lues).
  Champs à ajouter à `MEvent` : `bool? OptionsKnown, bool? IsFinished, bool? IsAncient, MLocKey? NameKey` ;
  à `MEventOption` : `MLocKey? TitleKey, MLocKey? DescriptionKey, string? RelicId`, avec
  `record MLocKey(string? Table, string? Key)`.
- **Enchantement** : `MCard(string? Id, int Upgrade)` l'ignore. Ajouter `MEnchant? Enchantment = null`
  (`record MEnchant(string? Id, int Amount)`, même forme que `Ultima.Saves.Enchantment`) et le passer à
  `new CardInstance(c.Id!, c.Upgrade, <ici>, null)` dans `StateAssembler.BuildReward` et `BuildShop` : les cartes
  proposées enchantées atteindraient alors `CardView.Enchantment` du contrat (déjà présent).
- **Piles** : non demandées par le daemon (`SectionsArg` n'ajoute jamais `piles`, pas de `MPiles`). Forme :
  `{inCombat, hand, draw, discard, exhaust, play, cardsBeforeReshuffle}`, chaque pile
  `{total, cards:[{card:{id,upgrade,enchantment}, count}]}`. Le daemon v1 ne lit pas la mémoire en combat :
  à brancher seulement après la mesure FPS du point 4, et le contrat n'a pas de champ pour les piles (demande
  au chef si l'overlay en veut).
- **Ultima.sln** : `Ultima.Mirror.Tests` n'y est pas (hors périmètre). L'ajouter ferait tourner la vérification de
  format daemon↔miroir avec le reste ; il référence `Ultima.Daemon` et `Ultima.Mirror` (ClrMD).
