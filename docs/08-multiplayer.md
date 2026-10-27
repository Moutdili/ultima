# 08 — Multiplayer : messages custom et broadcast

## Ce que le jeu expose (vérifié v0.107.1)

```csharp
// Multiplayer/Serialization/INetMessage.cs
[GenerateSubtypes]
public interface INetMessage : IPacketSerializable
{
    bool ShouldBroadcast { get; }
    NetTransferMode Mode { get; }
    LogLevel LogLevel { get; }
    bool ShouldBuffer { get; }
}

// Multiplayer/Serialization/MessageTypes.cs
public static void Initialize()
{
    var list = new List<Type>();
    list.AddRange(INetMessageSubtypes.All);
    list.AddRange(ReflectionHelper.GetSubtypesInMods<INetMessage>());  // ← MODS
    _cache = new NetTypeCache<INetMessage>(list);
}
```

→ **Un mod peut déclarer ses propres `INetMessage`** : le loader les découvre par réflexion
et leur attribue un ID. C'est la voie officielle pour broadcaster nos alertes.

## Envoi

```csharp
INetGameService net = RunManager.Instance.NetService;   // Runs/RunManager.cs:121
net.SendMessage(new UltimaPriorityMessage { ... });      // broadcast (host relaie)
```

- `NetHostGameService.SendMessage<T>(T message)` → boucle sur les peers prêts et envoie.
- `NetClientGameService.SendMessage<T>(T message)` → envoie au host.
- `ShouldBroadcast = true` → le host relaie aux autres clients.

## Réception

```csharp
net.RegisterMessageHandler<UltimaPriorityMessage>((msg, senderId) => { ... });
```

`NetMessageBus.RegisterMessageHandler<T>` (Multiplayer/NetMessageBus.cs:110).

## Messages proposés (V1)

```csharp
public sealed class UltimaPriorityMessage : INetMessage
{
    public int TurnNumber;          // pour ignorer les messages périmés
    public ulong PlayerId;          // qui a la carte
    public string CardId;           // id du modèle (stable)
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => true;
    public void Serialize(PacketWriter w) { w.WriteInt(TurnNumber); w.WriteULong(PlayerId); w.WriteString(CardId); }
    public void Deserialize(PacketReader r) { TurnNumber = r.ReadInt(); PlayerId = r.ReadULong(); CardId = r.ReadString(); }
}
```

⚠️ **À vérifier à l'implémentation** : les méthodes exactes de `PacketWriter`/`PacketReader`
(`WriteInt`, `WriteULong`, `WriteString`...) et la signature du delegate
`MessageHandlerDelegate<T>`. Ne pas copier ces noms à l'aveugle : les lire dans
`Multiplayer/Serialization/PacketWriter.cs` et `NetMessageBus.cs`.

## Règles de sûreté

1. **Read-only** : on ne transmet que des observations (jamais une action de jeu).
2. **Compatibility** : si un client n'a pas le mod, il ne reçoit rien et ne casse rien
   (les messages inconnus sont ignorés côté vanilla ? à vérifier — sinon ne broadcaster
   qu'entre clients ULTIMA, via un handshake `UltimaHello`).
3. **Version** : inclure `ModVersion` dans chaque message, ignorer une version majeure
   différente (évite les désérialisations foireuses après patch).
4. **Anti-spam** : un message par changement d'état, pas par frame. Buffer + diff.
5. **Ordre** : `TurnNumber` obligatoire ; un message dont le tour est périmé est jeté.

## Handshake (recommandé)

1. Au combat, chaque client envoie `UltimaHello { version, capabilities }`.
2. Les peers qui répondent sont marqués `ultimaPeer`.
3. On ne broadcast qu'aux `ultimaPeer` → zéro risque avec des joueurs vanilla.

## Ce que le jeu synchronise déjà (à ne pas dupliquer)

- **Intention de carte des autres joueurs** : `Multiplayer/Game/PeerInput/`
  (`PeerInputSynchronizer`, `HoveredModelTracker`) + UI `NMultiplayerCardIntent`,
  `NMultiplayerPlayerIntentHandler`. Le jeu affiche déjà « ce que l'autre s'apprête à jouer ».
  → Notre F3 doit **s'ajouter** à ça (priorité + première carte), pas le refaire.
- Cursor, map drawing, états de screen : déjà gérés.
