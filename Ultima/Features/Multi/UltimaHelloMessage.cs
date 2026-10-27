using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace Ultima.Features.Multi;

/// <summary>
/// Voie B — poignée de main ULTIMA (modèle : <c>Ultima.Net.UltimaPingMessage</c>).
/// Un pair n'est « capable » qu'après avoir envoyé un hello de même version majeure ;
/// aucune priorité n'est émise tant qu'un pair connu n'est pas capable.
/// N'est envoyé/écouté que si <c>UltimaConfig.CustomNetMessages</c> (R8).
/// Read-only : aucune action de jeu transportée.
/// </summary>
public sealed class UltimaHelloMessage : INetMessage
{
    public int ProtocolVersion;
    public string ModVersion = "";
    public ulong SenderNetId;

    public bool ShouldBroadcast => true;              // le host relaie aux autres clients
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(ProtocolVersion);
        writer.WriteString(ModVersion ?? "");
        writer.WriteULong(SenderNetId);
    }

    public void Deserialize(PacketReader reader)
    {
        ProtocolVersion = reader.ReadInt();
        ModVersion = reader.ReadString();
        SenderNetId = reader.ReadULong();
    }
}
