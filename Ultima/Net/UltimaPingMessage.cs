using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace Ultima.Net;

/// <summary>
/// Message de test ULTIMA. Sert à PROUVER que le transport custom fonctionne entre
/// plusieurs joueurs (le jeu découvre les INetMessage des mods par réflexion).
///
/// Read-only : ce message ne transporte aucune action de jeu, uniquement du texte.
/// </summary>
public sealed class UltimaPingMessage : INetMessage
{
    public int Turn;
    public ulong SenderId;
    public string Text = "";

    public bool ShouldBroadcast => true;              // le host relaie aux autres clients
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(Turn);
        writer.WriteULong(SenderId);
        writer.WriteString(Text ?? "");
    }

    public void Deserialize(PacketReader reader)
    {
        Turn = reader.ReadInt();
        SenderId = reader.ReadULong();
        Text = reader.ReadString();
    }
}
