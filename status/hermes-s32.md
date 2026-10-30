# Hermes — S32 : daemon v1.4 (T1 du plan 45)

> Branche `hermes/t1-daemon-v14`, partie de `origin/v2/cableur3`, `origin/main` fusionné (sans conflit).
> Périmètre respecté : `Ultima.Daemon/**`, `Ultima.Daemon.Tests/**`, `docs/ipc/**`, **une** section ajoutée à
> `docs/44-personnalisation.md` (§7bis `pluginsEnabled`). Aucune modification de `Ultima.Engine/Contracts/**`,
> `schemas/**`, `docs/22`, du reste de `docs/44`.
> Aucun essai n'a lu ni écrit `~/.config/ultima`, `~/.local/share/ultima`, `~/.local/state/ultima` : tous les
> essais utilisent des `XDG_*`/`HOME` temporaires (`/tmp/smoke.*`), le jeu n'a jamais été lancé ni lu.

## Fait

1. **Événement** — `EventView` depuis la section `event` du miroir, textes FR par clés (`nameKey`,
   `options[].titleKey/descriptionKey`), texte absent ⇒ `null` (jamais la clé ni l'id nu) ; `isFinished=true`
   ⇒ une seule option « Continuer » (`ContinueOptionId`) ; `optionsKnown=false` ⇒ **une** relecture bornée
   (anti-rebond 250 ms, drapeau remis à zéro au changement d'écran, jamais de boucle).
2. **Enchantement** — `MCard.Enchantment` (`MEnchant`) passé à `CardInstance` dans `BuildReward` et
   `BuildShop` : `CardView.Enchantment` (id, nom, montant, description FR) est rempli dans la récompense,
   le marchand et le deck.
3. **Réglages** — `enabled` **absent ⇒ `true`** ; `ModuleSetting.Screens` lu et validé contre `ScreenIds.All`
   (doublons et écrans inconnus retirés, liste vide ⇒ `null`) ; bornes `HudSettings` **ramenées à la borne**
   (hauteur 0,30–0,85, opacité 0,35–1 : `HeightMin/HeightMax/OpacityMin/OpacityMax`) au lieu du retour au
   défaut ; `pluginsEnabled` lu (hors contrat) ; le daemon **ne réécrit jamais** `settings.json`
   (prouvé par contenu + date de modification inchangés).
4. **Revue de run** — `IReviewBuilder` de S31 (`Ultima.Engine/Scoring/Review/ReviewBuilder`) branché dans
   `DaemonHost.BuildEngine` : `review.open` écrit désormais une timeline au lieu de répondre
   « review indisponible ».
5. **Modules déclaratifs** — `~/.config/ultima/modules/*.json` chargés au démarrage et **rechargés à chaud**
   (inotify, regroupé 200 ms, dossier créé plus tard géré) ; validation par `schemas/module.schema.json`
   (schémas livrés à côté de l'exécutable, copiés par le csproj) ; évaluateur **borné sans exécution de code** :
   chemins `{run.gold}`, `{run.deck[0].name}` (réflexion, 8 segments, index < 1000), filtres `|upper |pct
   |grade |count` (liste fermée), `when` avec `== != < <= > >=` et `and` (plus fort) / `or`, sans parenthèses ;
   chemin absent ⇒ « — » ; fichier invalide ⇒ `Health="error"` + `Problem`, les autres modules continuent ;
   publication dans `EngineState.Custom` (`Source="module"`).
6. **Plugins** — `~/.local/share/ultima/plugins/<id>/plugin.json` validé par `schemas/plugin.schema.json`,
   activés **seulement** par `settings.json` → `pluginsEnabled` ; **processus séparé** (dossier de travail =
   celui du plugin, `nice 19`), protocole `{"api":1,"state":…}` sur stdin **au plus une fois par
   `minIntervalMs`** et seulement si l'état change, une ligne stdout bornée à 8 Kio validée par
   `schemas/plugin-output.schema.json` dans `timeoutMs` ; invalide/trop long/hors délai ⇒ `stale` ;
   **3 échecs consécutifs ⇒ `disabled`** + journal ; asynchrone (fil dédié par plugin), arrêt propre avec le
   daemon (enfants tués et récoltés).
7. **Commandes** — `ultima-daemon module-test <fichier> [--state F]` et `ultima-daemon plugin-test <dossier>
   [--state F]` : rejouent **tous** les `docs/ipc/state.*.sample.json` (v1.3 puis v1.4) avec les mêmes
   validateurs ; codes 0 valide / 1 usage / 2 refusé / 3 échec d'exécution. Interface documentée dans
   `docs/ipc/README.md` §4.3 et §5.
8. **Échantillon** — `docs/ipc/state.v14.sample.json` (anonyme, sans run, `custom` avec un module
   `x.or-par-etage` et un plugin `x.echo`, `screens` v1.4), reproduit à l'identique par le sérialiseur.
   `docs/44` §7bis documente `pluginsEnabled`. Plugin d'exemple Python minimal :
   `Ultima.Daemon.Tests/Fixtures/plugins/x.echo/` (`plugin.json` + `main.py`).

## Prouvé

Build complet, **sans avertissement** :

    $ dotnet build Ultima.sln
    La génération a réussi.
        0 Avertissement(s)
        0 Erreur(s)

Tests du daemon (39 avant, **63** après) :

    $ dotnet test Ultima.Daemon.Tests/Ultima.Daemon.Tests.csproj
    Réussi!  - échec :     0, réussite :    63, ignorée(s) :     0, total :    63, durée : 3 s - Ultima.Daemon.Tests.dll (net9.0)

Porte d'intégration, **deux passes consécutives** (hôte à charge ~5,6 ; voir « Reste » pour la sensibilité
de la mesure à la charge) :

    $ bash tools/verify-all.sh          # essai 1 (load 5.86)
    .NET       vert
    compagnon  vert
    hud        vert
    console    vert
    PORTE : VERTE
    EXIT=0

    $ bash tools/verify-all.sh          # essai 2 (load 5.63)
    .NET       vert
    compagnon  vert
    hud        vert
    console    vert
    PORTE : VERTE
    EXIT=0

Critère des **50 ms** (`V14PluginTests.Sleeping_crashing_flooding_and_invalid_plugins_never_delay_publication_more_than_50ms`) :
4 plugins hostiles (endormi 30 s, `sys.exit(3)`, 1 Mio par ligne, JSON invalide) + 12 publications
chronométrées ⇒ aucune au-delà de 50 ms ; puis 3 échecs consécutifs ⇒ les 4 passent `disabled`, lignes vides,
`problem` renseigné. Test unitaire complémentaire : `Three_consecutive_failures_disable_the_plugin_and_are_logged`
(vérifie `DÉSACTIVÉ` dans le journal).

Daemon réel (binaire lancé, `XDG_*` temporaires, jeu non lancé, `--no-mirror`) — module + plugin chargés,
réglages bornés, arrêt propre :

    $ XDG_CONFIG_HOME=$T/cfg XDG_DATA_HOME=$T/data XDG_STATE_HOME=$T/state XDG_RUNTIME_DIR=$T/run \
      DOTNET_ROOT=$HOME/.dotnet Ultima.Daemon/bin/Debug/net9.0/ultima-daemon run \
      --root fixtures/saves/root --no-mirror --no-scoring --game-comm NoSuchGame-xyz
    WARN  plugin x.absent : dossier introuvable : …/plugins/x.absent
    INFO  modules perso : 1/1 valide(s)
    INFO  plugins activés : x.echo, x.absent
    INFO  prêt : …/run/ultima/state.json + …/state.sock ; miroir désactivé ; nice 10 sur 14 fil(s)

    # state.json publié (extrait)
    hud {'quality': 'verre', 'heightFraction': 0.85, 'restOpacity': 0.35, 'side': 'right', …}   # 0.99→0,85 ; 0.1→0,35
    custom: x.or-par-etage | module | ok | None | ['99 or · étage 1', 'PV 70 % · deck 14']
    custom: x.echo        | plugin | ok | None | ['écran Unknown', '99 or']
    custom: x.absent      | plugin | error | 'dossier introuvable : …' | []

    # processus du plugin (pendant la vie du daemon)
    PID    PPID    NI  COMMAND
    1438518 1438496 19 python3 main.py        # nice 19, PPID = le daemon, cwd = …/plugins/x.echo
    cwd plugin: /tmp/smoke.oHtSKb/data/ultima/plugins/x.echo

    # rechargement à chaud : fichier ajouté pendant que le daemon tourne
    INFO  modules perso : 2/2 valide(s)
    [('x.hot','ok',['rechargé à chaud · or 99']), ('x.or-par-etage','ok',[…] ), ('x.echo','ok',[…] ), ('x.absent','error',[])]

    # SIGTERM
    INFO  arrêt demandé
    daemon vivant ? non        plugin vivant ? non        # socket retirée, state.json conservé

Commandes de test (codes de sortie réels) :

    $ DOTNET_ROOT=$HOME/.dotnet Ultima.Daemon/bin/Debug/net9.0/ultima-daemon module-test Ultima.Daemon.Tests/Fixtures/modules/x.or-par-etage.json
    module  : x.or-par-etage · « Or / étage » · écrans map,shop
    schéma  : ok (schemas/module.schema.json)
    état    : state.v13.sample.json (écran Unknown)
      [accent] 134 or · étage 5
      [muted] PV 70 % · deck 14
      [info] Note F
    état    : state.v14.sample.json (écran Menu)
      [accent] — or · étage —
      [info] Note —
    5 ligne(s) sur 2 état(s) — OK
    code=0

    $ … ultima-daemon plugin-test Ultima.Daemon.Tests/Fixtures/plugins/x.echo
    plugin  : x.echo · « Écho » · api 1 · timeout 200 ms · minInterval 500 ms
    états   : state.v13.sample.json, state.v14.sample.json
    processus : pid 1453594 · nice 19 (le daemon le lance en nice 19)
    tour 1 (state.v13.sample.json) : 2 ligne(s)
      [muted] écran Unknown
      [accent] 134 or
    tour 2 (state.v14.sample.json) : 2 ligne(s)
    …
    2 état(s) — OK
    code=0

    $ … module-test /tmp/bad/mod.json
    schéma  : REFUSÉ (1 erreur(s), schemas/module.schema.json)
              $.rows[0].extra : champ inconnu
    code=2

    $ … plugin-test /tmp/bad/x.bad          # manifeste api:2
    manifeste : REFUSÉ — schéma : $.api : valeur attendue 1
    code=2

    $ … plugin-test /tmp/bad/x.bad          # sortie « pas du JSON »
    tour 1 (state.v13.sample.json) : SORTIE INVALIDE
              JSON invalide : 'p' is an invalid start of a value. …
    code=3

    $ … plugin-test
    usage : ultima-daemon plugin-test <dossier> [--state F]
    code=1

Échantillon v1.4 (test de forme, `V14Tests.V14_sample_is_reproduced_identically_by_the_daemon_serializer`) :
`docs/ipc/state.v14.sample.json` → `StateJson.Deserialize` → `Serialize` ⇒ mêmes clés, mêmes valeurs, mêmes
types (`custom` = 2 entrées `module` + `plugin`, `settings.modules[].screens` présent dont `null` explicites).

Revue : `V14Tests.Review_builder_is_wired_on_review_open` — `DaemonHost.BuildEngine(..., scoring: true)`
rend `Review != null` puis `review.open` répond `ok:true` et écrit une timeline (`runId` = `win_solo.run`).

Événement : `V14Tests.Unknown_event_options_publish_nothing_and_trigger_exactly_one_bounded_reread` —
`optionsKnown=false` ⇒ `Event == null` publié, **2** appels au miroir au total (lecture + relecture bornée),
aucun troisième sur 60 s simulées.

Enchantements : `V14Tests.Enchantments_are_filled_in_reward_and_shop_cards` — récompense et marchand portent
`Enchantment.Id/Amount/Name/Description`.

Réglages : `V14Tests.Settings_enabled_absent_is_true_and_screens_are_read_and_validated`,
`Hud_settings_out_of_bounds_are_clamped_to_the_bounds`, `Daemon_never_writes_settings_json`.

Modules : `Valid_module_renders_paths_filters_and_when` (« 134 or · étage 5 », « PV 70 % · deck 1 », chemin
absent ⇒ « — »), `Module_evaluator_is_bounded_and_never_executes_code` (filtre inconnu, gabarit non fermé,
parenthèses, profondeur, `${…}` ⇒ refus ou « — »), `Invalid_modules_are_published_as_error_with_a_reason_never_a_crash`
(6 fichiers, 5 refus motivés), `Modules_reload_when_the_folder_changes`.

## NON prouvé

- **Rien n'a tourné sur le vrai jeu ni dans une vraie session** : pas de partie lancée, pas de miroir réel,
  pas de lecture mémoire. Les sections `event`/`reward`/`shop` sont prouvées sur les formes JSON du miroir et
  les fixtures, pas sur une partie en cours.
- `optionsKnown=false` réel (préchargement de salle) : le comportement est prouvé en simulation ; la latence
  réelle entre `Preloading 'Event Room'` et `BeginEvent` n'a pas été mesurée.
- Textes FR des événements et des enchantements : rendus par `ILocTexts`/`IKnowledge` ; sans base de
  connaissance, les tests prouvent seulement « jamais la clé, `null` sinon ». Le rendu réel des libellés et
  des variables (`{Damage}`) n'a pas été vérifié.
- Les **50 ms** sont mesurés sur cette machine (12 cœurs, charge partagée 8–12) avec 4 plugins hostiles ; le
  comportement sur la machine de Mio en pleine partie n'est pas mesuré.
- Le plugin d'exemple n'a été exécuté qu'avec Python 3 du système (`/usr/bin/python3`) ; aucun autre langage
  ni runtime n'a été essayé.
- La consommation de `EngineState.Custom` par le HUD/compagnon/TUI n'est pas testée ici (T2/T3/T4).
- `ultima plugin new` (squelette) et `ultima plugin enable|disable` ne sont pas de mon périmètre : la clé
  `pluginsEnabled` est lue, mais aucun outil de mon périmètre ne l'écrit.

## Reste

- **Fenêtre de test réelle (chef + Mio)** : lancer le jeu, provoquer un événement, vérifier `event` +
  enchantements dans la récompense/le marchand, et un plugin activé pendant une partie.
- **Sensibilité de la porte à la charge de l'hôte** : pendant la session, l'hôte (12 cœurs) a porté une charge
  8–18 (autres agents ; un `tools/encounters/measure.py` à 98 % CPU observé). Dans ces conditions,
  `Ultima.Engine.Tests.Scoring.DeckTests.Deck_de_60_cartes_reste_sous_5ms` (budget 5 ms) et parfois la suite
  console (Textual) échouent, **même sans mes changements** : le test passe seul (249 ms) et échoue seulement
  quand une autre suite tourne en parallèle. J'ai réduit la durée de la suite daemon de 5 s à 3 s (moins de
  processus Python lancés) et lancé la porte à charge revenue sous 6 : **deux passes vertes d'affilée**. Aucun
  test du daemon n'a échoué dans les runs rouges. À relancer sur une machine calme pour confirmer.
- `dump` ne compose pas `custom` (seul `run` le fait) : volontaire, `dump` reste « fichiers seulement ».
  À ajouter si le chef veut inspecter les modules hors daemon (facile : passer un `CustomHost`).
- Pas de temporisation entre deux relances de plugin (`minIntervalMs` sert de plafond d'envoi) ; si le chef
  veut un backoff, le contrat n'a pas de champ pour ça.
- `docs/personnalisation/README.md` (exemples testés automatiquement) : hors périmètre T1, à faire en T2/T3.

## Demandes de contrat

1. `EventView.Name` et `EventOptionView.Label` sont **non nullables**, mais le miroir ne rend aucun texte et
   la base de connaissance peut manquer : le daemon est obligé de passer `null` (`name!`, `label!`) pour
   respecter « texte absent ⇒ null, jamais la clé ». Demande : `string? Name` / `string? Label` (ou une
   décision explicite « chaîne vide »). Aujourd'hui le JSON publié contient `"name": null` dans un champ
   typé `string`.
2. `CustomRowView.Grade` : la doc du contrat dit « Grade ∈ S..F », `schemas/plugin-output.schema.json`
   l'impose (enum S..F) mais `schemas/module.schema.json` accepte n'importe quelle chaîne ≤ 80 pour
   `rows[].grade`. Le daemon recopie tel quel (il ne juge pas le schéma). Demande : aligner les deux schémas
   (enum S..F côté module) ou documenter que `grade` de module est un texte libre affiché à côté de la ligne.
3. `docs/22-state.sample.json` ne contient pas `custom` (ni `screens`) : les tests de forme les tolèrent
   comme champs additifs v1.4. Demande : rafraîchir l'échantillon du contrat en v1.4 pour que
   `ContractShapeTests` redevienne exact plutôt que tolérant.
