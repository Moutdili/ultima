# Polices livrées (`fonts/base`)

Deux polices **libres** (SIL Open Font License 1.1), choisies pour couvrir le latin et les chiffres du
HUD et du compagnon sans dépendre du système :

| Fichier | Famille | Licence | Taille |
|---|---|---|---|
| `LiberationSans-Regular.ttf` | Liberation Sans | SIL OFL 1.1 (`LICENSE`) | 411 Kio |
| `LiberationMono-Regular.ttf` | Liberation Mono | SIL OFL 1.1 (`LICENSE`) | 320 Kio |

Total : 714 Kio (≤ 2 Mo, `docs/44` §5). `SHA256SUMS` est la référence : `install.sh` refuse une police
dont la somme ne correspond pas.

- Aucune police du JEU n'est ici : `~/.local/share/ultima/fonts/jeu/` est rempli par une extraction
  LOCALE (`install.sh --game-fonts DIR`), jamais commité.
- `systeme/` ne contient que des liens vers les polices installées (`fc-list`).
- `perso/` est vide et appartient à l'utilisateur.

Remplacer une police : déposer le `.ttf`/`.otf`, mettre à jour `SHA256SUMS`, vérifier la licence
(OFL/Apache uniquement), relancer `tools/install.sh`.
