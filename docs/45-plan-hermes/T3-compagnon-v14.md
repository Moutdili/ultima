# T3 — Compagnon v1.4

**Skills** : `sts2-companion-app`, `sts2-design-system` (§1, §2, §8-12), `sts2-engine-contract`.
**Périmètre** : `overlay/companion/**`. État actuel : `status/compagnon-design.md` (172 tests).

## Travail
1. **Résolveur de thème** Python : `appearance.companion.theme` → `extends` → `appearance.companion.tones` ;
   `auto-perso` selon la run, repli `sts` ; thème cassé ⇒ `sts` + message dans l'interface. Tout le CSS passe par
   les tons ; aucun hex hors des fichiers de thème (test).
2. **Polices** : `appearance.companion.font` (12–24 px) cherchée dans `fonts/{perso,jeu,base,systeme}` puis
   fontconfig ; introuvable ⇒ police du thème puis système. Supprimer la lecture actuelle de `game/pck/fonts`.
3. **Modules perso et plugins** : dessiner `EngineState.Custom` comme les autres blocs, à la place de `modules[]`,
   en respectant `screens` ; santé `stale`/`error`/`disabled` discrète.
4. **Réglages** : bornes du contrat (0,30–0,85 / 0,35–1), `enabled` absent ⇒ `true`, `screens` ; écriture :
   relire avant, ne toucher que ses clés, conserver les inconnues (`appearance`, `pluginsEnabled`…), atomique.
5. **Éditeur d'apparence** du compagnon (thème + aperçu, tons, police/taille), minimal (§11) ; config du HUD et du
   TUI en lecture avec la commande `ultima` équivalente.
6. **Décisions de Mio** : la note du deck suit l'ordre des réglages ; la page Run montre tous les modules, même
   désactivés pour le HUD.

## Critères
- `tools/verify-all.sh` vert, aucune fenêtre affichée (isolation de `tests/conftest.py` conservée).
- Repli `sts` prouvé sur un thème cassé ; conservation des clés inconnues prouvée.
