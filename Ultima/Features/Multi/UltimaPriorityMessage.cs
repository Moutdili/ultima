using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace Ultima.Features.Multi;

/// <summary>
/// Voie B — une carte prioritaire de la main de l'émetteur (F4).
/// Émission au changement de l'ensemble local : un message <see cref="CardClass"/> vide (« effacer »)
/// puis un message par alerte (canal Reliable). Réception : version différente ou
/// <see cref="TurnNumber"/> périmé → jeté. Read-only : aucune action de jeu transportée.
/// </summary>
public sealed class UltimaPriorityMessage : INetMessage
{
    public ulong PlayerNetId;
    public string CardClass = "";    // "" = l'émetteur n'a plus d'alerte (efface les précédentes)
    public string CardName = "";
    public int TurnNumber;
    public int ProtocolVersion;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(ProtocolVersion);   // version en tête : lisible même si la suite change
        writer.WriteULong(PlayerNetId);
        writer.WriteString(CardClass ?? "");
        writer.WriteString(CardName ?? "");
        writer.WriteInt(TurnNumber);
    }

    public void Deserialize(PacketReader reader)
    {
        ProtocolVersion = reader.ReadInt();
        PlayerNetId = reader.ReadULong();
        CardClass = reader.ReadString();
        CardName = reader.ReadString();
        TurnNumber = reader.ReadInt();
    }
}
