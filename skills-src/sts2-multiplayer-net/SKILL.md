---
name: sts2-multiplayer-net
description: Use when sending custom data between players in a Slay the Spire 2 co-op mod (INetMessage, NetService, handlers, versioning). Covers what the game already syncs and what not to duplicate.
---

# STS2 multiplayer networking

## Fait vérifié : les mods peuvent déclarer des messages

```csharp
// Core/Multiplayer/Serialization/MessageTypes.cs
list.AddRange(INetMessageSubtypes.All);
list.AddRange(ReflectionHelper.GetSubtypesInMods<INetMessage>());   // ← mods auto-découverts
```

## Voie recommandée d'abord : pas de message custom du tout

`RunManager.Instance.HoveredModelTracker` (propriété publique, `Runs/RunManager.cs:155`) :

```csharp
public AbstractModel? GetHoveredModel(ulong playerId);
public event Action<ulong>? HoverChanged;
```

Le jeu synchronise déjà la carte survolée/sélectionnée par chaque joueur
(`NPlayerHand` → `OnLocalCardHovered`, consommé par `NMultiplayerPlayerIntentHandler`).
→ Pour une alerte « ce joueur joue une carte prioritaire », s'abonner à `HoverChanged`
et filtrer sur le catalogue. Zéro message custom, zéro risque.

Aucun tracker multi public n'utilise `INetMessage` : cette voie est **non éprouvée**.
Ne la prendre que si l'info n'existe pas dans l'état synchronisé (ex. main privée).

## IDs de message : dépendants du mod-set

Les IDs sont dérivés du `FullName` des types, ordonnés sur **l'ensemble des mods chargés**.
BaseLib contourne ça en réécrivant `MessageTypes._cache` avec des IDs fixes 128-254.
→ Conséquence obligatoire : **handshake `UltimaHello`** (version + capabilities) et ne
broadcaster qu'aux peers qui ont répondu. Un client vanilla ne connaît pas l'ID.

## Contrat

```csharp
[GenerateSubtypes]
public interface INetMessage : IPacketSerializable
{
    bool ShouldBroadcast { get; }   // le host relaie
    NetTransferMode Mode { get; }   // Reliable / Unreliable
    LogLevel LogLevel { get; }
    bool ShouldBuffer { get; }
    void Serialize(PacketWriter writer);
    void Deserialize(PacketReader reader);
}
```

## Envoi / réception

```csharp
INetGameService net = RunManager.Instance.NetService;        // Runs/RunManager.cs:121
net.SendMessage(new UltimaPriorityMessage { ... });          // broadcast
net.RegisterMessageHandler<UltimaPriorityMessage>((msg, senderId) => { ... });
```

## Règles

1. **Ne transmettre que des observations** (jamais une action de jeu) — le mod est read-only.
2. **Handshake** : `UltimaHello { version, capabilities }` ; ne broadcaster qu'aux peers
   qui ont répondu. Évite tout problème avec des joueurs vanilla.
3. **Version** dans chaque message, ignorer une majeure différente.
4. **Anti-spam** : un message par changement d'état (diff), pas par frame.
5. **TurnNumber obligatoire**, jeter les messages périmés.

## Ne pas dupliquer ce que le jeu fait déjà

`Core/Multiplayer/Game/PeerInput/` (`PeerInputSynchronizer`, `HoveredModelTracker`) +
`NMultiplayerCardIntent` / `NMultiplayerPlayerIntentHandler` : le jeu affiche déjà la carte
que l'autre joueur s'apprête à jouer. Notre apport = **priorité** (« joue en premier »),
pas la visualisation de l'intent.

## À lire avant d'implémenter

`PacketWriter` / `PacketReader` (méthodes exactes : `WriteInt`, `WriteULong`, `WriteString`…)
et `MessageHandlerDelegate<T>` dans `Core/Multiplayer/NetMessageBus.cs`.
Ne pas copier un exemple de blog : vérifier dans le source v0.107.1.
