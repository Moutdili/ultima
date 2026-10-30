# Personnaliser ULTIMA (v1.4)

Ce guide est écrit pour **toi** (l'utilisateur) **et pour ton agent** : il dit où sont les fichiers,
à quoi ils ont droit, comment les valider, et ce qu'il faut faire quand ça ne s'affiche pas comme prévu.
Tout ce qui suit est vérifié par les tests du dépôt : chaque exemple de ce fichier passe réellement
`ultima theme check`, `ultima module validate` et `ultima plugin test`.

> Règle d'or : **rien ne casse**. Un thème, un module ou un plugin invalide est ignoré (champ par champ
> ou en entier) avec un message clair ; la surface retombe sur le thème `sts`. Aucune commande de ce
> guide ne lit le jeu ni ne le modifie.

---

## 1. Où sont les fichiers

| Quoi | Où | Qui écrit |
|---|---|---|
| Réglages (dont `appearance`) | `~/.config/ultima/settings.json` | TUI, compagnon, toi/ton agent |
| Lancement du daemon | `~/.config/ultima/console.json` | TUI (`ultima start`, miroir, options) |
| Thèmes perso | `~/.config/ultima/themes/<id>.json` | toi/ton agent |
| Modules déclaratifs | `~/.config/ultima/modules/<id>.json` | toi/ton agent |
| Plugins | `~/.local/share/ultima/plugins/<id>/plugin.json` (+ code) | toi/ton agent |
| Thèmes intégrés | `<dépôt>/themes/*.json` (copiés dans `~/.local/share/ultima/themes/`) | dépôt |
| Schémas | `<dépôt>/schemas/*.schema.json` | dépôt (contrat v1.4) |
| Polices | `~/.local/share/ultima/fonts/{base,systeme,jeu,perso}/` | `tools/install.sh`, `ultima font refresh` |

`XDG_CONFIG_HOME` / `XDG_DATA_HOME` sont respectés (utile pour tester dans un dossier jetable).
Rien de ce que tu écris ici n'est publié : le dépôt ne contient que des polices libres et des exemples.

### Les trois surfaces

| Surface | Ce qu'elle habille | Thème par défaut |
|---|---|---|
| `tui` | la console `ultima` (cadre **et** contenu : notes, malédictions, journal, bandeau) | `sts` |
| `hud` | le panneau GNOME (position, liseré, police) | `sts` |
| `compagnon` | la fenêtre GTK | `sts` |

Chacune a son thème, ses tons et sa police : `settings.json → appearance.{tui,hud,companion}`.

---

## 2. Les thèmes

### 2.1 Identifiants livrés

`sts` (défaut, palette du jeu) · par personnage : `ironclad`, `silent`, `defect`, `regent`,
`necrobinder` · neutres : `nuit` (noir minimal), `ardoise` (gris bleuté), `contraste` (haut contraste,
texte ≥ 7:1) · `auto-perso` (thème du personnage de la run en cours, repli `sts` hors run) ·
`terminal` (les 16 couleurs ANSI de ton terminal, aucune couleur imposée, fond du terminal respecté —
TUI seulement).

`ultima theme list` les affiche (une `*` marque ceux utilisés par une surface) ; `ultima theme show <id>`
montre les tons résolus.

### 2.2 Un thème complet (exemple testé)

Dépose ce fichier dans `~/.config/ultima/themes/brume.json` :

<!-- exemple: theme -->
```json
{
  "id": "brume",
  "label": "Brume",
  "api": 1,
  "extends": "sts",
  "description": "Neutre bleuté : accents doux, texte clair sur fond ardoise.",
  "tones": {
    "text": "#EEF2F8",
    "muted": "#EEF2F8AA",
    "accent": "#8FB3D9",
    "danger": "#FF7A7A",
    "ok": "#86D69A",
    "warn": "#E9C46A",
    "curse": "#CBA6E3",
    "enchant": "#78D6C9",
    "info": "#9CC0E8",
    "grade.S": "#8FB3D9",
    "grade.A": "#86D69A",
    "grade.B": "#78D6C9",
    "grade.C": "#9CC0E8",
    "grade.D": "#E9C46A",
    "grade.F": "#FF7A7A",
    "background": "#1C2230",
    "border": "#8FB3D966"
  },
  "logColors": { "timestamp": "muted", "debug": "muted", "info": "text", "warn": "warn", "error": "danger" },
  "glass": { "tint": "#1C2230", "tintOpacity": 0.55 }
}
```

Puis :

```bash
ultima theme check brume     # schéma + héritage + tons + contraste mesuré
ultima theme set tui brume   # applique à la console (hud / compagnon aussi)
```

- `extends` hérite d'un autre thème (un niveau conseillé, cycles refusés).
- Les tons sont **sémantiques** : `text`, `muted`, `accent`, `danger`, `ok`, `warn`, `curse`, `enchant`,
  `info`, `grade.S` … `grade.F`, plus `background` et `border` (réservés aux thèmes). Un module ou un
  plugin ne connaît que ces noms : il s'affiche donc correctement dans tous les thèmes.
- Contraste : sous 4,5:1 le thème est accepté mais signalé ; `nuit`, `ardoise` et `contraste` sont
  au-dessus de 7:1 (mesuré par un test).
- `logColors` : une couleur du journal par niveau (`timestamp`, `debug`, `info`, `warn`, `error`),
  valeur = un ton ou un `#RRGGBB(AA)`. Les surcharges de `appearance.tui.logColors` priment.

### 2.3 `auto-perso`

`"theme": "auto-perso"` suit `run.character` : Ironclad → `ironclad`, Silent → `silent`, Defect → `defect`,
Regent → `regent`, Necrobinder → `necrobinder` ; hors run (ou personnage inconnu) → `sts`. La console
affiche `auto-perso → necrobinder` dans l'onglet **7 Apparence**.

### 2.4 Le thème `terminal`

`"theme": "terminal"` n'impose **aucune** couleur : les 16 couleurs ANSI du terminal sont utilisées
(noms ANSI, jamais de 24 bits) et le fond du terminal est respecté. Les surcharges `tones` en hex sont
ignorées pour cette surface (elles n'auraient pas de sens). Pour revenir à la palette du jeu : `sts`.

---

## 3. Réglages : `appearance` dans `settings.json`

<!-- exemple: appearance -->
```json
"appearance": {
  "hud": {
    "theme": "sts",
    "font": { "family": "Liberation Sans", "size": 14 },
    "tones": { "accent": "#EFC851" },
    "position": { "side": "right", "anchor": "center", "offsetX": 0, "offsetY": 0, "width": 360 },
    "railEdge": true
  },
  "companion": { "theme": "sts", "font": { "family": "Liberation Sans", "size": 14 }, "tones": {} },
  "tui": { "theme": "terminal", "tones": {}, "logColors": { "warn": "warn", "error": "#FF5555" } }
}
```

- **Écriture** : tout écrivain (TUI, compagnon, toi si tu utilises les commandes) relit le fichier juste
  avant d'écrire, ne modifie que ses clés, **conserve les clés inconnues** et écrit atomiquement
  (fichier temporaire + rename). Un `settings.json` invalide n'est jamais écrasé : la commande refuse
  et le dit.
- Bornes : `size` 12–24 px ; `width` 280–520 px ; `offsetX`/`offsetY` en px logiques (bornés).
  Valeur absente ou invalide ⇒ défaut, champ par champ.
- **Migration v1.3 → v1.4** : l'ancien `console.json` portait `theme` et `logColors` ; ils sont repris
  **une seule fois** dans `appearance.tui` (thème → `sts`/`terminal`, ancien token de palette → ton).
  `console.json` est conservé tel quel et ne sert plus qu'au lancement du daemon.

### Blocs et écrans

`settings.json → modules[]` liste les blocs affichés : `{"id": "deck", "enabled": true, "order": 0}`.
Les modules perso et plugins y figurent avec leur id `x.…`. Nouveau (v1.4), optionnel :

```json
{ "id": "x.or-par-etage", "enabled": true, "order": 12, "screens": ["map", "shop"] }
```

`screens` restreint le bloc à ces écrans (`menu`, `map`, `combat`, `reward`, `shop`, `event`, `restSite`,
`treasure`) ; absent = écrans par défaut du module. Dans la console : onglet **4 HUD** ou
**7 Apparence → Modules**, touche `e` pour faire défiler les écrans, `[` `]` pour l'ordre, espace pour
afficher/masquer.

### Plugins activés

`settings.json → "pluginsEnabled": ["x.mon-plugin"]` : seuls les plugins `x.…` listés ici tournent.
On n'ajoute un id **qu'après un test réussi** (`ultima plugin enable` refuse un plugin qui échoue).

---

## 4. Un module déclaratif (aucun code exécuté)

Un module est un fichier JSON évalué par le daemon (un seul évaluateur) et dessiné par les surfaces.
Dépose ceci dans `~/.config/ultima/modules/x.or-par-etage.json` :

<!-- exemple: module -->
```json
{
  "id": "x.or-par-etage",
  "label": "Or / étage",
  "api": 1,
  "screens": ["map", "shop"],
  "rows": [
    { "text": "{run.gold} or · étage {run.floor}", "tone": "accent" },
    { "when": "run.gold >= 150", "text": "Shop rentable", "tone": "ok" }
  ]
}
```

Puis :

```bash
ultima module validate ~/.config/ultima/modules/x.or-par-etage.json
ultima module new x.mon-module          # squelette valide, à modifier
ultima module list                      # tout ce qui est chargé, avec les erreurs
```

- `text` : gabarit `{chemin.dans.EngineState}` (camelCase du contrat), 160 caractères au plus.
  Filtres limités : `|upper`, `|pct`, `|grade`, `|count`. Chemin absent ou `null` ⇒ « — », jamais 0.
- `when` : comparaison simple (`== != < <= > >=`, `and`, `or`), rien d'autre.
- `tone` : un ton du thème (§2.2) ; `grade` : une lettre S-F facultative.
- 20 lignes au plus. Un fichier refusé est publié en `Health="error"` avec sa raison — jamais un arrêt.

---

## 5. Un plugin Python (vrai code, isolé)

Un plugin est un **processus séparé** lancé par le daemon : `plugin.json` + ton code. Crée le dossier
`~/.local/share/ultima/plugins/x.exemple/` avec ces deux fichiers.

`plugin.json` :

<!-- exemple: plugin-manifest -->
```json
{
  "id": "x.exemple",
  "label": "Exemple",
  "api": 1,
  "version": "1.0",
  "description": "Plugin d'exemple : or et étage, une ligne.",
  "exec": ["python3", "main.py"],
  "screens": ["map", "shop"],
  "timeoutMs": 300,
  "minIntervalMs": 1000
}
```

`main.py` :

<!-- exemple: plugin-code -->
```python
#!/usr/bin/env python3
"""Plugin ULTIMA minimal : une ligne JSON d'état sur stdin, une ligne JSON de sortie sur stdout."""
import json
import sys


def render(state):
    run = state.get("run") or {}
    gold = run.get("gold")
    floor = run.get("floor")
    if gold is None:
        return [{"text": "or —", "tone": "muted"}]
    text = f"{gold} or · étage {floor if floor is not None else '—'}"
    tone = "ok" if gold >= 150 else "accent"
    return [{"text": text, "tone": tone}]


def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            message = json.loads(line)
        except ValueError:
            continue
        print(json.dumps({"rows": render(message.get("state") or {})}, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
```

Puis :

```bash
ultima plugin new x.mon-plugin     # squelette complet qui passe le test
ultima plugin test x.exemple       # rejoue les états d'exemple (docs/ipc/*.sample.json)
ultima plugin enable x.exemple     # activé SEULEMENT si le test a réussi
ultima plugin list
ultima plugin disable x.exemple
```

Protocole : le daemon écrit sur stdin **une ligne JSON** `{"api":1,"state":EngineState}` (au plus une
fois par `minIntervalMs`), et attend **une ligne JSON** sur stdout :

```json
{"rows": [{"text": "150 or · étage 4", "tone": "ok"}]}
```

`rows` ≤ 20, `text` ≤ 160 caractères, `tone` du contrat, `grade` S-F facultative. Sortie invalide, trop
longue ou hors délai ⇒ ignorée pour ce tour ; **3 échecs de suite ⇒ plugin désactivé** et signalé
(`ultima doctor`, journal du daemon). Un plugin ne tourne jamais dans GNOME Shell, le TUI ou le
compagnon ; il ne reçoit que `EngineState`, qui ne contient aucune donnée cachée (pas de tirage, pas
d'ordre de pioche) : lire la mémoire ou les saves du jeu pour en obtenir est hors des règles d'ULTIMA.

> Si le daemon de test n'est pas encore disponible (branche `hermes/t1-daemon-v14` non fusionnée),
> `ultima plugin test` le dit (« à brancher ») et fait un **essai local** : un état d'exemple, un
> processus, une ligne validée par `schemas/plugin-output.schema.json`. Le mode utilisé est affiché
> (`local`, `schema` ou `daemon`) et `--json` le donne champ `mode`.

---

## 6. Les polices

Quatre dossiers, jamais mélangés, sous `~/.local/share/ultima/fonts/` :

| Dossier | Contenu | Origine |
|---|---|---|
| `base/` | polices **libres** livrées par ULTIMA (OFL 1.1), licence à côté, ≤ 2 Mo, sha256 vérifié | dépôt `fonts/base/` |
| `systeme/` | **liens** vers les polices installées (jamais de copie) + `index.json` | `fc-list`, `ultima font refresh` |
| `jeu/` | polices du jeu extraites **localement**, **jamais commitées** | `tools/install.sh --game-fonts DIR` |
| `perso/` | tes polices (.ttf/.otf) | toi |

```bash
ultima font list        # familles par dossier + contrôle licence/sha256
ultima font refresh     # recrée les liens système, l'index et le fontconfig
```

`~/.config/fontconfig/conf.d/60-ultima.conf` (écrit par `install.sh`, retiré par `uninstall.sh`) rend
ces dossiers visibles du HUD et du compagnon. Famille introuvable ⇒ police du thème, puis police
système par défaut. Le TUI utilise la police du terminal (sans objet).

Ajouter une police livre : déposer le `.ttf`/`.otf` **libre** dans `fonts/base/`, mettre à jour
`fonts/base/SHA256SUMS`, garder la licence à côté, relancer `tools/install.sh`. Une police non libre
ou extraite du jeu ne doit **jamais** entrer dans le dépôt.

---

## 7. La console (TUI)

`ultima` (sans argument) ouvre la console. Onglet **7 Apparence** :

- sous-onglet **TUI** : thème, tons, couleurs du journal, aperçu vivant ;
- sous-onglet **HUD** : thème, police et taille, côté/ancrage/décalages/largeur, liseré, tons ;
- sous-onglet **Compagnon** : thème, police et taille, tons ;
- sous-onglet **Modules** : blocs (affiché, ordre, écrans), modules `x.*`, plugins (activés après test).

`←/→` change la valeur, `espace` bascule, `[` `]` déplace un bloc, `e` fait défiler ses écrans.
Chaque changement est enregistré aussitôt (écriture atomique) et s'applique sans redémarrer.

Les commandes équivalentes (scriptables, `--json` partout) :

```bash
ultima theme list|show <id>|check <id>|set <tui|hud|compagnon> <id>
ultima font list|refresh
ultima module list|validate <fichier>|new <id>
ultima plugin list|new <id>|test <id>|enable <id>|disable <id>
```

`NO_COLOR=1` (ou `--color never`) coupe les couleurs ; `--color always` les force (utile dans un
script ou une capture). `--json` donne la forme machine.

---

## 8. Dépannage

| Symptôme | Cause probable | Quoi faire |
|---|---|---|
| Le thème ne change rien | id inconnu ou fichier invalide | `ultima theme list`, `ultima theme check <id>` ; un fichier invalide est ignoré en entier |
| Le contenu garde les anciennes couleurs | surface différente (TUI vs HUD) | chaque surface a son thème : `ultima theme set hud <id>` |
| `settings.json invalide` | JSON cassé à la main | réparer ou retirer le fichier ; aucune commande ne l'écrase |
| Un module n'apparaît pas | id sans `x.`, schéma, `when` invalide | `ultima module validate <fichier>` ; `ultima doctor` |
| Un plugin ne s'active pas | test échoué (sortie, délai, schéma) | `ultima plugin test <id> --json` ; le détail dit quoi corriger |
| Plugin actif mais absent | `screens` ne couvre pas l'écran courant, ou bloc masqué | onglet 4 HUD / Apparence → Modules |
| Police absente | famille mal orthographiée, `base` non installé | `ultima font list` ; `tools/install.sh` ; `ultima font refresh` |
| Migration inattendue | `console.json` portait `theme`/`logColors` | c'est prévu une fois : voir `appearance.tui` ; l'ancien fichier est conservé |

Toujours : `ultima doctor` (lecture seule) en premier.

---

## 9. Consignes pour un agent

Si tu es un agent qui configure ULTIMA pour quelqu'un :

1. **Lis ce fichier en entier** avant d'écrire. Ne devine pas les tons : ils sont listés en §2.2.
2. **Emplacements** : thème → `~/.config/ultima/themes/<id>.json` ; module →
   `~/.config/ultima/modules/<id>.json` ; plugin → `~/.local/share/ultima/plugins/<id>/`. Jamais dans
   le dépôt (sauf `themes/` si tu es le mainteneur) ; jamais de police non libre ni du jeu.
3. **Un fichier à la fois, puis validation** :
   `ultima theme check <id>` · `ultima module validate <fichier>` · `ultima plugin test <id>`.
   Ne passe pas à l'étape suivante tant que la commande n'a pas rendu 0.
4. **Changer un réglage** : préfère `ultima theme set <surface> <id>` (ou l'onglet Apparence) à une
   réécriture manuelle de `settings.json` — l'écrivain relit le fichier et conserve les clés inconnues.
   Si tu édites à la main : garde **toutes** les clés existantes, écris un fichier temporaire puis
   renomme (atomique), et laisse `appearance` aux clés que tu comprends.
5. **Plugins** : n'ajoute jamais un id à `pluginsEnabled` sans `ultima plugin test` réussi. Un plugin
   est un processus séparé : pas d'import de code du jeu, pas de lecture de sa mémoire, pas de réseau
   implicite ; borne `rows` et `text` comme le schéma.
6. **Toujours vérifier** : `ultima theme list --json`, `ultima module list --json`,
   `ultima plugin list --json` pour l'état, puis `ultima doctor` pour l'installation.
7. **Ne touche pas** : `schemas/`, `themes/sts.json`, `Ultima.Engine/`, les réglages d'une autre
   surface que celle demandée, et les vrais dossiers XDG si tu testes (utilise un `HOME` temporaire).
8. En cas de doute, le contrat est `docs/44-personnalisation.md` ; les schémas sont dans `schemas/`.
