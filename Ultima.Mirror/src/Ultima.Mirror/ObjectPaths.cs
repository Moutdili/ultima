namespace Ultima.Mirror;

/// <summary>
/// One field the mirror is allowed to read. <see cref="Field"/> is the REAL metadata name
/// (auto-properties compile to <c>&lt;Name&gt;k__BackingField</c>). <see cref="Source"/> points to the
/// declaration in the shipped source (~/sts2/game/pck/src/Core, v0.107.1).
/// </summary>
public sealed record FieldSpec(string Type, string Field, bool IsStatic, string Source, string Purpose);

/// <summary>
/// Single source of truth for every object path the mirror follows. The probe's <c>layout</c>
/// command checks each entry against sts2.dll metadata (no game process needed).
/// Anything not listed here is not read — <see cref="ReadGuard"/> enforces it at runtime.
/// </summary>
public static class ObjectPaths
{
    // ---- type names (namespace + nested with '+') ----
    public const string RunManager = "MegaCrit.Sts2.Core.Runs.RunManager";
    public const string RunState = "MegaCrit.Sts2.Core.Runs.RunState";
    public const string Player = "MegaCrit.Sts2.Core.Entities.Players.Player";
    public const string Creature = "MegaCrit.Sts2.Core.Entities.Creatures.Creature";
    public const string CardPile = "MegaCrit.Sts2.Core.Entities.Cards.CardPile";
    public const string AbstractModel = "MegaCrit.Sts2.Core.Models.AbstractModel";
    public const string ModelId = "MegaCrit.Sts2.Core.Models.ModelId";
    public const string CardModel = "MegaCrit.Sts2.Core.Models.CardModel";
    public const string LocalContext = "MegaCrit.Sts2.Core.Context.LocalContext";
    public const string RewardsSetSynchronizer = "MegaCrit.Sts2.Core.Multiplayer.Game.RewardsSetSynchronizer";
    public const string PlayerRewardState = "MegaCrit.Sts2.Core.Multiplayer.Game.RewardsSetSynchronizer+PlayerRewardState";
    public const string RewardsSetState = "MegaCrit.Sts2.Core.Multiplayer.Game.RewardsSetSynchronizer+RewardsSetState";
    public const string RewardsSet = "MegaCrit.Sts2.Core.Rewards.RewardsSet";
    public const string Reward = "MegaCrit.Sts2.Core.Rewards.Reward";
    public const string CardReward = "MegaCrit.Sts2.Core.Rewards.CardReward";
    public const string CardCreationResult = "MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult";
    public const string MerchantRoom = "MegaCrit.Sts2.Core.Rooms.MerchantRoom";
    public const string MerchantInventory = "MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory";
    public const string MerchantEntry = "MegaCrit.Sts2.Core.Entities.Merchant.MerchantEntry";
    public const string MerchantCardEntry = "MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardEntry";
    public const string MerchantRelicEntry = "MegaCrit.Sts2.Core.Entities.Merchant.MerchantRelicEntry";
    public const string MerchantPotionEntry = "MegaCrit.Sts2.Core.Entities.Merchant.MerchantPotionEntry";
    public const string MerchantCardRemovalEntry = "MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardRemovalEntry";
    public const string CombatManager = "MegaCrit.Sts2.Core.Combat.CombatManager";
    public const string CombatState = "MegaCrit.Sts2.Core.Combat.CombatState";
    public const string PlayerCombatState = "MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState";
    // ---- miroir 2 (2026-10-29) ----
    public const string EventSynchronizer = "MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer";
    public const string EventRoom = "MegaCrit.Sts2.Core.Rooms.EventRoom";
    public const string EventModel = "MegaCrit.Sts2.Core.Models.EventModel";
    public const string AncientEventModel = "MegaCrit.Sts2.Core.Models.AncientEventModel";
    public const string EventOption = "MegaCrit.Sts2.Core.Events.EventOption";
    public const string LocString = "MegaCrit.Sts2.Core.Localization.LocString";
    public const string EnchantmentModel = "MegaCrit.Sts2.Core.Models.EnchantmentModel";

    static string K(string prop) => $"<{prop}>k__BackingField";

    public static readonly IReadOnlyList<FieldSpec> All = new FieldSpec[]
    {
        // ---- run ----
        new(RunManager, K("Instance"), true, "Runs/RunManager.cs:64", "singleton"),
        new(RunManager, K("State"), false, "Runs/RunManager.cs:209", "RunState (null hors run)"),
        new(RunManager, K("RewardsSetSynchronizer"), false, "Runs/RunManager.cs:143", "racine des écrans de récompense"),
        new(RunState, "_players", false, "Runs/RunState.cs:25", "List<Player>"),
        new(RunState, "_currentRooms", false, "Runs/RunState.cs:46", "pile de salles ; dernière = CurrentRoom (l.175)"),
        new(RunState, "_currentActIndex", false, "Runs/RunState.cs:27", "acte courant (0-based)"),
        new(RunState, K("AscensionLevel"), false, "Runs/RunState.cs:210", "ascension"),
        new(LocalContext, K("NetId"), true, "Context/LocalContext.cs:19", "ulong? du joueur local"),
        new(Player, K("NetId"), false, "Entities/Players/Player.cs:48", "id réseau"),
        new(Player, K("Character"), false, "Entities/Players/Player.cs:44", "CharacterModel"),
        new(Player, K("Creature"), false, "Entities/Players/Player.cs:46", "Creature (PV)"),
        new(Player, "_gold", false, "Entities/Players/Player.cs:38", "or"),
        new(Player, "_relics", false, "Entities/Players/Player.cs:32", "List<RelicModel>"),
        new(Player, "_potionSlots", false, "Entities/Players/Player.cs:34", "List<PotionModel?> (null = slot vide)"),
        new(Player, K("Deck"), false, "Entities/Players/Player.cs:198", "CardPile du deck"),
        new(Player, K("MaxEnergy"), false, "Entities/Players/Player.cs:200", "énergie max"),
        new(Player, K("PlayerCombatState"), false, "Entities/Players/Player.cs:114", "null hors combat"),
        new(Creature, "_currentHp", false, "Entities/Creatures/Creature.cs:30", "PV"),
        new(Creature, "_maxHp", false, "Entities/Creatures/Creature.cs:32", "PV max"),
        new(Creature, "_block", false, "Entities/Creatures/Creature.cs:28", "armure"),
        new(Creature, K("Monster"), false, "Entities/Creatures/Creature.cs:101", "MonsterModel (ennemis)"),
        new(CardPile, "_cards", false, "Entities/Cards/CardPile.cs:16", "List<CardModel>"),
        new(AbstractModel, K("Id"), false, "Models/AbstractModel.cs:31", "ModelId"),
        new(ModelId, K("Category"), false, "Models/ModelId.cs:9", "ex. CARD"),
        new(ModelId, K("Entry"), false, "Models/ModelId.cs:11", "ex. STRIKE_SILENT"),
        new(CardModel, "_currentUpgradeLevel", false, "Models/CardModel.cs:91", "niveau d'amélioration"),

        // ---- card reward (rooted, no heap scan) ----
        new(RewardsSetSynchronizer, "_rewardStates", false, "Multiplayer/Game/RewardsSetSynchronizer.cs:115", "List<PlayerRewardState>, ordre = joueurs"),
        new(RewardsSetSynchronizer, "_localPlayerId", false, "Multiplayer/Game/RewardsSetSynchronizer.cs:107", "joueur local"),
        new(PlayerRewardState, "rewardsStack", false, "Multiplayer/Game/RewardsSetSynchronizer.cs:66", "pile d'écrans de récompense ouverts"),
        new(RewardsSetState, "set", false, "Multiplayer/Game/RewardsSetSynchronizer.cs:93", "RewardsSet"),
        new(RewardsSet, K("Player"), false, "Rewards/RewardsSet.cs:47", "propriétaire"),
        new(RewardsSet, K("Rewards"), false, "Rewards/RewardsSet.cs:49", "List<Reward>"),
        new(Reward, K("SuccessfullySelected"), false, "Rewards/Reward.cs:51", "déjà pris"),
        new(CardReward, "_cards", false, "Rewards/CardReward.cs:37", "List<CardCreationResult> = cartes proposées"),
        new(CardCreationResult, "originalCard", false, "Entities/Cards/CardCreationResult.cs:8", "carte tirée"),
        new(CardCreationResult, "_modifiedCard", false, "Entities/Cards/CardCreationResult.cs:10", "carte modifiée par relique (prioritaire, l.14)"),

        // ---- shop ----
        new(MerchantRoom, K("Inventories"), false, "Rooms/MerchantRoom.cs:27", "List<MerchantInventory>, index = slot joueur (l.46-50)"),
        new(MerchantInventory, "_characterCardEntries", false, "Entities/Merchant/MerchantInventory.cs:30", "cartes du personnage"),
        new(MerchantInventory, "_colorlessCardEntries", false, "Entities/Merchant/MerchantInventory.cs:32", "cartes incolores"),
        new(MerchantInventory, "_relicEntries", false, "Entities/Merchant/MerchantInventory.cs:34", "reliques"),
        new(MerchantInventory, "_potionEntries", false, "Entities/Merchant/MerchantInventory.cs:36", "potions"),
        new(MerchantInventory, K("CardRemovalEntry"), false, "Entities/Merchant/MerchantInventory.cs:46", "retrait de carte"),
        new(MerchantInventory, K("Player"), false, "Entities/Merchant/MerchantInventory.cs:48", "propriétaire"),
        new(MerchantEntry, "_cost", false, "Entities/Merchant/MerchantEntry.cs:17", "prix AVANT hooks (MembershipCard, TheCourier : l.26)"),
        new(MerchantCardEntry, K("CreationResult"), false, "Entities/Merchant/MerchantCardEntry.cs:32", "null = vendu"),
        new(MerchantCardEntry, K("IsOnSale"), false, "Entities/Merchant/MerchantCardEntry.cs:34", "soldé (prix déjà /2, l.125-128)"),
        new(MerchantRelicEntry, K("Model"), false, "Entities/Merchant/MerchantRelicEntry.cs:21", "null = vendu"),
        new(MerchantPotionEntry, K("Model"), false, "Entities/Merchant/MerchantPotionEntry.cs:24", "null = vendu"),
        new(MerchantCardRemovalEntry, K("Used"), false, "Entities/Merchant/MerchantCardRemovalEntry.cs:16", "retrait déjà utilisé"),

        // ---- combat ----
        new(CombatManager, K("Instance"), true, "Combat/CombatManager.cs:80", "singleton"),
        new(CombatManager, "_state", false, "Combat/CombatManager.cs:67", "CombatState (null hors combat)"),
        new(CombatManager, K("IsInProgress"), false, "Combat/CombatManager.cs:153", "combat en cours"),
        new(CombatState, "_enemies", false, "Combat/CombatState.cs:29", "List<Creature>"),
        new(CombatState, K("RoundNumber"), false, "Combat/CombatState.cs:102", "tour"),
        new(PlayerCombatState, "_energy", false, "Entities/Players/PlayerCombatState.cs:21", "énergie"),
        new(PlayerCombatState, K("Hand"), false, "Entities/Players/PlayerCombatState.cs:60", "main"),

        // ---- miroir 2 : piles de combat (la pioche est à part : UnorderedOnly) ----
        new(PlayerCombatState, K("DiscardPile"), false, "Entities/Players/PlayerCombatState.cs:64", "défausse (rendue comptée)"),
        new(PlayerCombatState, K("ExhaustPile"), false, "Entities/Players/PlayerCombatState.cs:66", "épuisement (rendu compté)"),
        new(PlayerCombatState, K("PlayPile"), false, "Entities/Players/PlayerCombatState.cs:68", "carte en cours de jeu (rendue comptée)"),

        // ---- miroir 2 : enchantements (toute carte lue : deck, main, récompense, shop, piles) ----
        new(CardModel, K("Enchantment"), false, "Models/CardModel.cs:631", "EnchantmentModel? (null = pas d'enchantement)"),
        new(EnchantmentModel, "_amount", false, "Models/EnchantmentModel.cs:29", "montant (DisplayAmount == Amount : aucune surcharge dans Models/Enchantments)"),

        // ---- miroir 2 : événement courant ----
        new(RunManager, K("EventSynchronizer"), false, "Runs/RunManager.cs:139", "racine des événements (créé l.408)"),
        new(EventSynchronizer, "_localPlayerId", false, "Multiplayer/Game/EventSynchronizer.cs:26", "joueur local"),
        new(EventSynchronizer, "_events", false, "Multiplayer/Game/EventSynchronizer.cs:28", "List<EventModel> mutables, index = slot joueur (GetEventForPlayer l.311-315)"),
        new(EventRoom, K("CanonicalEvent"), false, "Rooms/EventRoom.cs:29", "événement canonique de la salle (contrôle croisé de l'id)"),
        new(EventModel, K("Owner"), false, "Models/EventModel.cs:77", "Player propriétaire (contrôle : == joueur local)"),
        new(EventModel, K("Description"), false, "Models/EventModel.cs:87", "LocString? de la page courante"),
        new(EventModel, "_currentOptions", false, "Models/EventModel.cs:42", "List<EventOption>? = options affichées (NEventRoom.cs:192)"),
        new(EventModel, "_isFinished", false, "Models/EventModel.cs:44", "fini → l'écran n'affiche que « Continuer » (NEventRoom.cs:193-196)"),
        new(EventOption, K("TextKey"), false, "Events/EventOption.cs:14", "id stable de l'option (préfixe de clé de localisation)"),
        new(EventOption, K("Title"), false, "Events/EventOption.cs:16", "LocString du libellé"),
        new(EventOption, K("Description"), false, "Events/EventOption.cs:18", "LocString de la description"),
        new(EventOption, K("IsLocked"), false, "Events/EventOption.cs:24", "verrouillée (clic ignoré, NEventRoom.cs:211)"),
        new(EventOption, K("IsProceed"), false, "Events/EventOption.cs:26", "bouton « continuer »"),
        new(EventOption, K("WasChosen"), false, "Events/EventOption.cs:28", "déjà choisie"),
        new(EventOption, K("Relic"), false, "Events/EventOption.cs:30", "RelicModel? affichée avec l'option (anciens)"),
        new(LocString, "<locTable>P", false, "Localization/LocString.cs:13", "table de localisation (paramètre de constructeur primaire)"),
        new(LocString, "<locEntryKey>P", false, "Localization/LocString.cs:13", "clé de localisation (paramètre de constructeur primaire)"),
    };

    /// <summary>
    /// Fields whose CONTENT is visible in game but whose ORDER is hidden. <see cref="ReadGuard.Check"/>
    /// still refuses them; only <see cref="MirrorSession.ReadUnorderedPile"/> may follow them, and it
    /// returns a counted multiset sorted by card key — the order never leaves the library.
    /// Justification: the game itself shows the draw pile SORTED (rarity, id), never in draw order
    /// (Nodes/Screens/NCardPileScreen.cs:122-125). Not part of <see cref="All"/> (so the
    /// whitelist self-check stays strict) but verified by <c>layout</c>.
    /// </summary>
    public static readonly IReadOnlyList<FieldSpec> UnorderedOnly = new FieldSpec[]
    {
        new(PlayerCombatState, K("DrawPile"), false, "Entities/Players/PlayerCombatState.cs:62", "pioche : CONTENU compté seulement, ordre jamais rendu"),
    };

    /// <summary>Everything <c>layout</c> checks against sts2.dll metadata.</summary>
    public static IEnumerable<FieldSpec> AllForLayout => All.Concat(UnorderedOnly);

    /// <summary>
    /// Fields that must NEVER be read (Mio decision 2026-10-28, "probabiliste seulement", docs/20 §8.2)
    /// plus hidden ordering (draw pile order). Matched case-insensitively on the field name.
    /// </summary>
    public static readonly IReadOnlyList<string> ForbiddenFieldFragments = new[]
    {
        "rng", "odds", "grabbag", "encounterid", "eventid", "seed", "drawpile", "_piles", "_runpiles",
    };
}
