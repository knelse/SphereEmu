using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SphServer;
using SphServer.Helpers;
using SphServer.Packets;

public enum NpcType
{
    TradeMagic,
    TradeAlchemy,
    TradeWeapon,
    TradeJewelry,
    TradeArmor,
    TradeTravelGeneric,
    TradeTravelTokens,
    TradeTavernkeeper,
    QuestTitle,
    QuestDegree,
    QuestKarma,
    Guilder,
    Banker,
    Prefix,
    Tournament
}

public static class NpcInteractableMappings
{
    public static int NpcTypeToNpcTradeTypeSph (NpcType npcType)
    {
        return npcType switch
        {
            NpcType.TradeMagic => 9,
            NpcType.TradeAlchemy => 6,
            NpcType.TradeWeapon => 11,
            NpcType.TradeJewelry => 8,
            NpcType.TradeArmor => 7,
            NpcType.TradeTavernkeeper => 5,
            NpcType.TradeTravelGeneric => 10,
            NpcType.TradeTravelTokens => 10,
            NpcType.QuestTitle => 4,
            NpcType.QuestDegree => 2,
            NpcType.QuestKarma => 3,
            NpcType.Guilder => 1,
            NpcType.Banker => 0,
            NpcType.Prefix => 12,
            NpcType.Tournament => 13,
            _ => 0
        };
    }
}

public partial class NpcInteractable : Node3D
{
    [Export] public int Angle { get; set; }
    [Export] public ushort ID { get; set; } = 0x1234;
    [Export] public int NameID { get; set; } = 4016;
    [Export] public string ModelName { get; set; } = string.Empty;
    public string ModelNameSph => ModelName + "\0";
    [Export] public string IconName { get; set; } = string.Empty;
    public string IconNameSph => IconName + "\0";
    public int IconNameLength => IconNameSph.Length;

    [Export] public ObjectType ObjectType { get; set; } = ObjectType.Unknown;
    [Export] public NpcType NpcType { get; set; }

    public readonly Dictionary<ushort, DateTime> ShownForClients = new ();

    // Called when the node enters the scene tree for the first time.
    public override void _Ready ()
    {
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process (double delta)
    {
        foreach (var clientInRange in MainServer.ActiveClients.Where(x =>
                     x.Value.IsReadyForGameLogic && !ShownForClients.ContainsKey(x.Key) &&
                     x.Value.DistanceTo(GlobalTransform.Origin) <= 100))
        {
            ShownForClients.Add(clientInRange.Key, DateTime.UtcNow);
            ShowForClient(clientInRange.Value);
        }

        foreach (var clientOutOfRange in MainServer.ActiveClients.Where(x =>
                     ShownForClients.ContainsKey(x.Key) && x.Value.DistanceTo(GlobalTransform.Origin) > 100))
        {
            clientOutOfRange.Value.StreamPeer.PutData(CommonPackets.DespawnEntity(ID));
            ShownForClients.Remove(clientOutOfRange.Key);
        }
    }

    public void ShowForClient (Client client)
    {
        var packetParts = PacketPart.LoadDefinedPartsFromFile(NpcType);
        PacketPart.UpdateCoordinates(packetParts, GlobalTransform.Origin.X, GlobalTransform.Origin.Y,
            GlobalTransform.Origin.Z, Angle);
        var localId = client.GetLocalObjectId(ID);
        PacketPart.UpdateEntityId(packetParts, localId);
        PacketPart.UpdateValue(packetParts, "name_id", NameID - 4000, 11);
        var modelName = ModelNameSph;
        if (NpcType is NpcType.Guilder)
        {
            modelName = modelName.PadRight(16, '\0');
        }

        PacketPart.UpdateValue(packetParts, "entity_type_name_length", modelName.Length, 8);
        PacketPart.UpdateValue(packetParts, "entity_type_name", modelName);
        PacketPart.UpdateValue(packetParts, "icon_name_length", IconNameLength, 8);
        PacketPart.UpdateValue(packetParts, "icon_name", IconNameSph);
        var tradeType = NpcInteractableMappings.NpcTypeToNpcTradeTypeSph(NpcType);
        PacketPart.UpdateValue(packetParts, "npc_trade_type", tradeType, 4);

        var npcPacket = PacketPart.GetBytesToWrite(packetParts);
        npcPacket[^1] = 0;

        client.StreamPeer.PutData(npcPacket);
    }
}