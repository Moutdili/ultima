# Fenêtre de test n°1 — preuves réelles de la vague 0 (~35 min)

Réunit les protocoles de C2 (`overlay/gnome/TEST-WINDOW.md`), C1 (`overlay/x11/TEST-WINDOW.md`),
B (`docs/mirror/test-window.md`), A et G. **Mio joue ; le chef lance les commandes** et lit les
résultats. Mio dit seulement où il en est (« carte », « combat », « récompense », « shop »).

Accords demandés à Mio au début de la fenêtre (valables pour cette fenêtre seulement) :
- captures d'écran composées (portail) aux étapes marquées 📸 ;
- `inotifywait` sur le dossier du jeu (audit no-write) et `strace` sur NOS outils seulement.

## Étape 0 — avant le jeu (Mio, 3 min)
1. `cd ~/sts2 && overlay/gnome/install-dev.sh --check && overlay/gnome/install-dev.sh`
2. Se déconnecter / se reconnecter (seule coupure de session de toute la fenêtre).
3. `gnome-extensions enable ultima@mio` → le rail apparaît au bord droit, hors jeu.

## Étape 1 — jeu lancé, menu principal
- Chef : audit no-write démarré (`tools/audit/no-write-audit.sh`), contrôle `mods/` vide.
- Chef : `ultima-mirror probe` (attache sur le vrai CoreCLR 9.0.7, lecture seule sous strace).

## Étape 2 — run en cours, sur la CARTE (profil normal, plein écran)
- Chef : `ultima-saves run` + `ultima-mirror probe --sections run --compare-save`.
- Mio : lit PV, or, nombre de cartes, reliques → comparaison. 📸
- C2 : rail visible au-dessus du jeu plein écran ? 📸

## Étape 3 — COMBAT
- C2 : survol → panneau verre ; clic sur une carte du jeu SOUS le panneau ; `Super+F9`,
  `Super+F8` ; le jeu garde le focus. 📸 panneau ouvert
- FPS (compteur Steam) : extension visible / masquée.
- C2 désactivée (`Super+F8`), C1 : `overlay/x11/ultima-x11 run` puis `toggle` (raccourci ou
  commande) → visible au-dessus du vrai jeu ? clic traversant ? 📸 puis `ultima-x11 quit`.

## Étape 4 — ÉCRAN DE RÉCOMPENSE (ne pas cliquer tout de suite)
- Chef : `ultima-mirror probe --sections reward` → 3 cartes lues.
- Mio : lit les 3 noms de gauche à droite → comparaison. Puis clic sur la récompense de cartes,
  nouvelle lecture. 📸

## Étape 5 — SHOP (si un marchand est accessible)
- Chef : `ultima-mirror probe --sections shop` → cartes, reliques, potions, prix.
- Mio : lit chaque prix → comparaison (piège connu : réductions de reliques). Achat d'une carte,
  nouvelle lecture.

## Étape 6 — fin
- Chef : arrêt de l'audit → rapport (attendu : 0 écriture d'ULTIMA, `modded/` inchangé).
- Résultats consignés dans `STATUS.md` : S1 (moteur retenu), S2 (miroir validé ou non), G-réel A.

## Retour arrière à tout moment
`gnome-extensions disable ultima@mio` · `overlay/x11/ultima-x11 quit` · Ctrl+C sur les sondes.
Rien n'est jamais écrit dans le dossier du jeu.
