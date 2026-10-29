using Ultima.Saves.Model;

namespace Ultima.Daemon.Mirror;

/// <summary>
/// Vérification croisée save↔mémoire (skill sts2-memory-mirror : « Vérification croisée avec la save à
/// chaque salle ; divergence → mode dégradé annoncé à l'écran »). Faite à l'ENTRÉE d'une salle lue par le
/// miroir (marchand), moment où le jeu vient d'écrire la save (log « Wrote … » juste avant « Preloading »).
/// Champs : PV, PV max, or, deck (multiensemble id+amélioration), reliques (ordre).
/// </summary>
public static class CrossCheck
{
    /// <returns>Liste des écarts lisibles (vide = accord).</returns>
    public static IReadOnlyList<string> Compare(PlayerState save, MPlayer mem)
    {
        var diffs = new List<string>();
        if (save.CurrentHp != mem.Hp) diffs.Add($"PV {mem.Hp}≠{save.CurrentHp}");
        if (save.MaxHp != mem.MaxHp) diffs.Add($"PV max {mem.MaxHp}≠{save.MaxHp}");
        if (save.Gold != mem.Gold) diffs.Add($"or {mem.Gold}≠{save.Gold}");
        static string Key(string? id, int up) => up > 0 ? $"{id}+{up}" : $"{id}";
        var sDeck = save.Deck.Select(c => Key(c.Id, c.UpgradeLevel)).OrderBy(x => x, StringComparer.Ordinal);
        var mDeck = mem.Deck.Select(c => Key(c.Id, c.Upgrade)).OrderBy(x => x, StringComparer.Ordinal);
        if (!sDeck.SequenceEqual(mDeck)) diffs.Add($"deck {mem.Deck.Count}≠{save.Deck.Count} cartes");
        if (!save.Relics.Select(r => r.Id).SequenceEqual(mem.Relics.Select(r => r ?? "")))
            diffs.Add($"reliques {mem.Relics.Count}≠{save.Relics.Count}");
        return diffs;
    }
}
