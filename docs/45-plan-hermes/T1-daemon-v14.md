# T1 — Daemon v1.4

**Skills** : `sts2-daemon`, `sts2-engine-contract`, `sts2-memory-mirror`, `sts2-knowledge-base`,
`sts2-save-format`, `sts2-performance`.
**Périmètre** : `Ultima.Daemon/**`, `Ultima.Daemon.Tests/**`, `docs/ipc/**`, ajout de la section
`pluginsEnabled` dans `docs/44-personnalisation.md` (ajout seulement).
**Point de départ** : branche `origin/v2/cableur3` (2 commits, tests daemon 39/39) — déjà fait :
lecture des clés d'événement et des enchantements du miroir 2, textes d'événement FR via `LocTexts` ;
WIP non terminé : câblage de `LocTexts` dans l'hôte, relecture bornée quand `optionsKnown=false`.
À lire : `status/miroir2.md` §5, `docs/mirror/object-paths.md` §7-10.

## Travail
1. **Événement** : finir `EventView` depuis la section `event` du miroir (textes FR via les clés ; texte
   absent ⇒ `null`, jamais la clé affichée). `optionsKnown=false` ⇒ une relecture après un court délai, bornée.
   `isFinished=true` ⇒ une seule option « Continuer ».
2. **Enchantement** : `CardView.Enchantment` rempli (nom + description FR) dans récompense et shop.
3. **Réglages** : `enabled` absent ⇒ `true` ; bornes `HudSettings` (hauteur 0,30–0,85, opacité 0,35–1, ramenées
   à la borne : `HudSettings.HeightMin/HeightMax/OpacityMin/OpacityMax`) ; `ModuleSetting.Screens` lu ; le daemon
   ne réécrit jamais `settings.json`.
4. **Revue de run** : brancher l'`IReviewBuilder` livré en S31 (`6cde032`) sur l'op `review.open` (aujourd'hui
   non rempli).
5. **Modules déclaratifs** (docs/44 §6) : charger `~/.config/ultima/modules/*.json`, valider
   (`schemas/module.schema.json`), recharger à chaud ; évaluateur borné (gabarits `{chemin}`, filtres `|upper`
   `|pct` `|grade` `|count`, `when` = comparaisons + `and`/`or`, AUCUNE exécution de code ; chemin absent ⇒ « — »).
   Publier dans `EngineState.Custom` (`Source="module"`) ; invalide ⇒ `Health="error"` + `Problem`, jamais de chute.
6. **Plugins** (docs/44 §7), seulement ceux listés dans `settings.json` → `pluginsEnabled` (à documenter) :
   processus séparé en `nice 19`, dossier de travail = celui du plugin ; une ligne `{"api":1,"state":…}` sur stdin
   au plus une fois par `minIntervalMs` et seulement si l'état change ; une ligne de réponse validée par
   `plugin-output.schema.json` dans `timeoutMs` ; invalide/trop long/hors délai ⇒ `stale` ; 3 échecs de suite ⇒
   `disabled` + journal ; asynchrone, ne retarde jamais la publication ; arrêt propre avec le daemon.
7. **Commandes de test** pour la console : `ultima-daemon plugin-test <dossier> [--state f]` et
   `module-test <fichier>` (rejouent `docs/ipc/*.sample.json` + fixtures, code de sortie clair) ; interface
   documentée dans `docs/ipc/`.
8. Échantillon `docs/ipc/state.v14.sample.json` avec `custom` (un module, un plugin), anonymisé.

## Critères
- `tools/verify-all.sh` vert ; build daemon sans avertissement.
- Test de forme : l'échantillon v1.4 se reproduit à l'identique.
- Un plugin qui dort, crashe, écrit 1 Mo ou répond un JSON invalide ne retarde pas la publication de plus de
  50 ms (test chronométré) ; plugin d'exemple Python minimal dans les fixtures.
