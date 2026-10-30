# T4 — HUD (extension GNOME `ultima@mio`) v1.4

**Skills** : `sts2-external-overlay`, `sts2-design-system` (§1, §2, §8-12), `sts2-engine-contract`, `sts2-performance`.
**Périmètre** : `overlay/gnome/**`. État actuel : `status/verrier-design.md`, `overlay/gnome/TEST-WINDOW.md`,
`PERF.md` (verre v3 eco/verre/loupe, rail en verre seul, `npm test` 458/458).
**Décisions de Mio** : liseré or du rail gardé pour la fenêtre de test (réglable `appearance.hud.railEdge`) ;
survol piloté par le curseur par défaut ; en combat, aucune règle codée en dur (seules exceptions du contrat :
`paths` sur la carte, `deck_score` selon `deckScoreInCombat`), plus `ModuleSetting.screens`.

## Travail
1. **Résolveur de thème** GJS : `appearance.hud.theme` → `extends` → `appearance.hud.tones` ; teinte du verre
   depuis `glass` ; `auto-perso` ; thème cassé ⇒ `sts` + journal `[ULTIMA]`, jamais d'exception dans GNOME Shell.
   Aucun hex hors des fichiers de thème (adapter le test « palette »).
2. **Polices** : `appearance.hud.font` (12–24 px) via le CSS de St ; repli thème puis défaut.
3. **Disposition** : côté, ancrage haut/centre/bas, `offsetX`/`offsetY` bornés à l'écran, largeur 280–520 px,
   hauteur/opacité aux bornes du contrat, `railEdge`. Géométrie testée sur 1920×1200 à 125 %, 1920×1080,
   2560×1440, 3440×1440, 1536×960 : jamais hors écran, ne couvre pas « Fin du tour » à droite.
4. **Modules perso et plugins** : dessiner `EngineState.Custom` (libellé, tons, note S-F) à la place de `modules[]`,
   `screens` respecté, santé ≠ ok atténuée ; texte tronqué proprement. **Aucun code tiers exécuté dans l'extension.**
5. **Performance** : changer de thème/police ne crée aucune minuterie ni travail par image au repos (test) ;
   `eco` reste sans effet.
6. Mettre à jour `TEST-WINDOW.md` (vérifications thèmes, polices, positions, modules perso, avec commandes).

## Critères
- `tools/verify-all.sh` vert (sortie brute de `npm test` dans le rapport).
- Repli `sts` prouvé ; géométrie prouvée sur les 5 résolutions ; aucun travail au repos.
