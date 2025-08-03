using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SphServer.Helpers;

namespace SphServer.Nodes;

public enum NpcTypes
{
    TradeMagic,
    TradeAlchemy,
    TradeWeapon,
    TradeArmor,
    TradeTravelGeneric,
    TradeTravelTokens,
    TradeTavernkeeper,
    QuestTitle,
    QuestDegree,
    QuestKarma,
    Guilder
}

public partial class NpcNode : CharacterBody3D
{
    public double X = 390.99884033203125;
    public double Y = 153.36358642578125;
    public double Z = -1308.5076904296875;
    public int Angle = 193;
    public int NameID = 4004;
    public int ModelNameLength = 6;
    public string ModelName = "npc08\0";
    public int IconNameLength = 15;
    public string IconName = "npc_trade_wpon\0";
    public ushort ID = 0x1234;

    public Dictionary<ushort, DateTime> ShownForClients = new ();

    public override void _Ready ()
    {
        GlobalTransform = new Transform3D
        {
            Origin = new Vector3((float) X, (float) -Y, (float) Z),
            Basis = Basis.Identity
        };
    }

    public override void _Process (double delta)
    {
        // foreach (var clientInRange in SphereServer.ActiveClients.Where(x =>
        //              !ShownForClients.ContainsKey(x.Key) && x.Value.DistanceTo(GlobalTransform.Origin) <= 100))
        // {
        //     ShownForClients.Add(clientInRange.Key, DateTime.UtcNow);
        //     ShowForClient(clientInRange.Value);
        //     Console.WriteLine($"NPC spawn: {ID:X4} {X:F2} {Y:F2} {Z:F2} {Angle} {NameID} {ModelName} {IconName}");
        // }
        //
        // foreach (var clientOutOfRange in SphereServer.ActiveClients.Where(x =>
        //              ShownForClients.ContainsKey(x.Key) && x.Value.DistanceTo(GlobalTransform.Origin) > 100))
        // {
        //     clientOutOfRange.Value.StreamPeer.PutData(CommonPackets.DespawnEntity(ID));
        //     ShownForClients.Remove(clientOutOfRange.Key);
        //     Console.WriteLine($"Despawn: {ID:X4}");
        // }
    }

    public void ShowForClient (Client client)
    {
        // var packetParts = PacketPart.LoadDefinedPartsFromFile(PacketPartNames.Teleport);
        // PacketPart.UpdateCoordinates(packetParts, X, Y, Z, Angle, false);
        // var localId = client.GetLocalObjectId(ID);
        // PacketPart.UpdateEntityId(packetParts, localId);
        // // PacketPart.UpdateValue(packetParts, "name_id", NameID - 4000, 11);
        // // PacketPart.UpdateValue(packetParts, "entity_type_name_length", ModelNameLength, 8);
        // // PacketPart.UpdateValue(packetParts, "entity_type_name", ModelName);
        // // PacketPart.UpdateValue(packetParts, "icon_name_length", IconNameLength, 8);
        // // PacketPart.UpdateValue(packetParts, "icon_name", IconName);
        // // why is there 0xFE at the end again?
        // var npcPacket = PacketPart.GetBytesToWrite(packetParts);
        // // npcPacket[^1] = 0;
        // // var npcPacket =
        // //     Convert.FromHexString(
        // //         "4B002C0100abcd771454834FFB6F7888A22B63E80772943818140580020401000000806288000000A0F060E00637068303D0087870831BFBA2930B232BFBBA837B73036808106C0D030000");
        // Console.WriteLine(Convert.ToHexString(npcPacket));
        //
        // client.StreamPeer.PutData(npcPacket);
    }
}