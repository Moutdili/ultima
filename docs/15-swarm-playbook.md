# 15 — Harnais de swarm (Claude Code, mode ultra, budget 2 h)

Objectif : transformer un travail de 3-4 jours en **un run de 2 h** en découpant en
milestones à propriété de fichiers stricte, exécutés par des sous-agents qui poussent
en atomique sur des branches isolées.

Trois principes :
1. **Aucun sous-agent ne cherche** : toute la connaissance est dans `docs/13-prior-art-playbook.md`
   et `docs/11-api-catalog.md`. Chercher = brûler du budget.
2. **Un fichier, un propriétaire.** Les conflits de merge sont éliminés par construction.
3. **Chaque milestone a une porte de validation binaire** (build + tests + log). Pas de porte,
   pas de milestone suivant.

---

## 1. Topologie git

```
main                      protégée, reçoit les merges du chef d'orchestre
├── m0/bootstrap          (chef) outillage, vérif overlay
├── m1/reader             A1  Ultima/GameApi/**
├── m2/ui-shell           A2  Ultima/UI/**
├── m3/catalog            A3  tools/, data/, docs/
├── m4/core-hardening     A4  Ultima.Tests/**, .github/**
├── m5/forecast           B1  Ultima/Features/Forecast/**
├── m6/score              B2  Ultima/Features/Score/**
└── m7/multi              B3  Ultima/Features/Multi/**
```

Règles :
- Un sous-agent = un worktree = une branche. Jamais deux agents sur la même branche.
- `main` n'est modifiée que par le chef d'orchestre (merge `--no-ff` ou squash).
- Chaque branche est poussée dès le premier commit : la CI tourne et sert de juge.

## 2. Propriété des fichiers (contrat anti-conflit)

| Agent | Possède (écrit) | Ne touche jamais |
|---|---|---|
| **Chef** | `ModEntry.cs`, `Patches/**`, `Ultima/Ultima.csproj`, `SPEC.md`, `STATUS.md` | le reste |
| **A1 Reader** | `Ultima/GameApi/**`, `Ultima/Model/**` | UI, Patches, tests des autres |
| **A2 UI** | `Ultima/UI/**` | GameApi, Patches |
| **A3 Catalog** | `tools/index_source.py`, `data/catalog/**`, `docs/13*` | code du mod |
| **A4 Hardening** | `Ultima.Tests/**`, `.github/**` | code du mod |
| **B1 Forecast** | `Ultima/Features/Forecast/**` | UI, GameApi |
| **B2 Score** | `Ultima/Features/Score/**` | UI, GameApi |
| **B3 Multi** | `Ultima/Features/Multi/**` | UI, GameApi |

Si un agent a besoin d'une modification hors de son périmètre : il **n'édite pas**, il écrit
une ligne dans `status/<agent>.md` et le signale au chef. Le chef fait la modification
lui-même (il possède le point de composition).

## 3. DAG d'exécution

```
M0 bootstrap (chef, 10 min)
 │  doctor vert + overlay injecté + tag v0.0.1
 ├── A1 Reader      (25 min)  ─┐
 ├── A2 UI shell    (25 min)   │  parallèle : le contrat DTO est figé avant
 ├── A3 Catalog     (20 min)   │
 └── A4 Hardening   (20 min)  ─┘
        │  merge A1 (contrat disponible)
        ├── B1 Forecast  (20 min)
        ├── B2 Score     (20 min)
        └── B3 Multi     (20 min)
                 │
        M8 intégration + vérif (chef, 15 min)
```

Chemin critique : M0 → A1 → B1 → M8 ≈ 70 min. Les autres branches remplissent les creux.
Budget total visé : **110-130 min**, avec 20 min de marge.

## 4. Contrat DTO figé avant le parallélisme

A2 (UI) et B1/B2 (features) codent contre ce contrat, sans attendre A1 :

```csharp
// Ultima/Model/CombatSnapshot.cs  (propriétaire : A1, figé dès M0)
public sealed record CombatSnapshot(
    int Turn,
    string PlayerName,
    int Hp, int MaxHp, int Block, int Energy,
    IReadOnlyList<EnemySnapshot> Enemies,
    IReadOnlyList<CardSnapshot> Hand,
    IReadOnlyList<CardSnapshot> DrawPile,
    IReadOnlyList<CardSnapshot> DiscardPile);

public sealed record EnemySnapshot(
    string Name, int Hp, int MaxHp, int Block,
    IReadOnlyList<IntentSnapshot> Intents);

public sealed record IntentSnapshot(
    string Kind,      // "Attack", "Defend", "Buff", ...
    int PerHit, int Repeats, int TotalDamage,
    string Label);    // texte localisé fourni par le jeu

public sealed record CardSnapshot(
    string ModelId, string Name, int Cost, string Type,
    bool IsUpgraded, string? Enchantment);
```

**Toute évolution du contrat passe par le chef**, jamais par un sous-agent.

## 5. Protocole de commit atomique

Format :

```
<type>(<scope>): <résumé impératif, <= 72 car>

- ce que ça fait
- fichier(s) touché(s)
- preuve: commande exécutée + résultat
```

Types : `feat`, `fix`, `test`, `docs`, `chore`, `refactor`.
Scopes : `reader`, `ui`, `forecast`, `score`, `multi`, `catalog`, `ci`, `core`.

Règles :
- Un commit = une intention, ≤ 250 lignes de diff.
- `git push` **immédiatement** après chaque commit (pas de batch en fin de branche).
- Jamais de `git add -A` : `git add <chemins possédés>` uniquement.
- Jamais de `--force`, jamais de rebase de `main`.
- Fin de milestone : la branche est poussée — c'est le livrable. `gh` n'est **pas installé
  sur fedora** : ne pas compter sur `gh pr create`. Si gh devient disponible, une PR draft
  est un bonus, jamais un prérequis. Le rapport final liste les branches et leurs hashes.

## 6. Portes de validation (binaires)

| Porte | Commande | Critère |
|---|---|---|
| G1 Compile | `dotnet build Ultima/Ultima.csproj -c Release` | 0 erreur |
| G2 Tests | `dotnet test Ultima.Tests/Ultima.Tests.csproj` | 100 % verts |
| G3 Read-only | `grep -rn "CardPileCmd\\|DamageCmd\\|PowerCmd.Apply\\|CreatureCmd" Ultima/` | aucun appel (hors commentaires) |
| G4 Charge | log `<jeu>/mods/Ultima/ultima.log` contient `overlay injected` | présent |
| G5 Exactitude | valeur affichée == valeur à l'écran pour 3 rencontres | identique |
| G6 CI | workflow GitHub sur la branche | vert |

Un agent ne rend pas la main sans G1+G2+G3 sur sa branche. G4-G5 sont du ressort du chef
(ils exigent le jeu lancé).

## 7. Format de rendu d'un sous-agent

Chaque agent écrit `status/<agent>.md` :

```markdown
# <agent> — <milestone>
- branche: mX/...
- commits: <sha> <résumé>
- fichiers: <liste>
- portes: G1 ✅ G2 ✅ G3 ✅
- bloqué sur: <rien | description>
- à faire par le chef: <rien | action précise>
```

Le chef agrège dans `STATUS.md` et décide du merge.

## 8. Politique de merge

Ordre : A4 → A3 → A1 → A2 → B1 → B2 → B3.

- Merge `--no-ff` pour garder l'historique des branches (le graphe raconte le run).
- Après chaque merge : `dotnet build` + `dotnet test` sur `main`. Si rouge → revert immédiat
  (`git revert -m 1 <sha>`), le chef corrige, la branche repart.
- Aucun merge d'une branche dont la CI est rouge.
- Les PR draft servent de revue ; le chef les ferme en mergant.

## 9. Gestion du temps (le vrai risque)

| Minute | Action |
|---|---|
| 0-10 | M0 : doctor, build, deploy, overlay vérifié |
| 10-15 | Création des worktrees + spawn A1-A4 |
| 15-40 | A1-A4 en parallèle ; le chef prépare le câblage des patches |
| 40-45 | Merge A1, contrat DTO disponible, spawn B1-B3 |
| 45-65 | B1-B3 en parallèle ; le chef merge A2/A3/A4 |
| 65-85 | Merge B1-B3, build + tests sur main |
| 85-105 | Test en jeu, corrections, polish UI |
| 105-120 | Rapport final, tag, STATUS.md |

Règle de coupe : à **T+90**, toute feature non committée est abandonnée. On livre ce qui
compile et ce qui est prouvé. Mieux vaut 4 features solides que 7 à moitié faites.

## 10. Escalade

Un sous-agent s'arrête et rend la main si :
- il est bloqué > 10 min sur une API introuvable,
- il devrait modifier un fichier hors périmètre,
- une porte G1/G2 reste rouge après 2 tentatives.

Il commite alors en `wip(scope): ...` (préfixe explicite), pousse, et écrit son blocage
dans `status/`. Le chef décide : corriger, réassigner, ou abandonner la feature.

## 11. Anti-patterns de swarm (à interdire explicitement)

- Deux agents qui éditent `Ultima.csproj` → conflit garanti. Seul le chef y touche.
- Un agent qui "améliore" le code d'un autre → il ouvre une note, il n'édite pas.
- Un agent qui refait une recherche sur le jeu → interdit, tout est dans `docs/`.
- Un agent qui attend le jeu lancé pour coder → l'UI et la logique se codent sans le jeu.
- Un commit fourre-tout en fin de branche → impossible de revert proprement.
- Un merge de `main` dans sa branche → jamais. On rebase seulement si le chef le demande.

## 12. Ce qui rend ce harnais efficace (pourquoi ça tient en 2 h)

1. **La logique mathématique est déjà écrite et testée** (`Ultima.Core`, 12 tests verts).
   Un sous-agent n'a plus qu'à brancher des lectures de jeu dessus.
2. **Le contrat DTO est figé** : UI et features avancent sans se parler.
3. **La CI juge à la place de l'humain** : chaque push est vérifié automatiquement.
4. **Les conflits sont structurels, pas accidentels** : propriété de fichiers stricte.
5. **Le temps est budgété par milestone**, avec une règle de coupe explicite.
6. **Aucune recherche** : `docs/13` contient les méthodes exactes des 10 dépôts analysés.
