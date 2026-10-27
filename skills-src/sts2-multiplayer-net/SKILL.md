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
