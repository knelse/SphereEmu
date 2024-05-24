using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Helpers;
using SphServer.Helpers.Enums;

namespace SphServer.Packets;

public static class PacketPartNames
{
    public const string MobLootSack = "MobLootSack";
    public const string NpcTrade = "NpcTrade";
    public const string NpcTradeWeapon = "NpcTradeWeapon";
    public const string Teleport = "Teleport";
}

public class PacketPart
{
    // this is a server-side stub, main implementation in PacketLogViewer

    public const string UndefinedFieldValue = "__undef";
    public const string LengthFromPreviousFieldValue = "__fromPrevious";
    public readonly string? EnumName;
    public readonly PacketPartType PacketPartType;
    public int BitLength;
    public int BitPositionStart;
    public readonly string Name;
    public List<Bit> Value;

    public PacketPart (string name, PacketPartType partType, int bitPositionStart, int bitLength, string enumName,
        List<Bit> value)
    {
        Name = name;
        Value = value;
        PacketPartType = partType;
        BitPositionStart = bitPositionStart;
        BitLength = bitLength;
        EnumName = enumName;
        Value = value;
    }

    public static List<PacketPart> LoadDefinedPartsFromFile (ObjectType objectType)
    {
        var name = ObjectTypeToPacketNameMap.Mapping.GetValueOrDefault(objectType, "teleport");
        var partsPath = MainServer.AppConfig["PacketPartPath"];
        return LoadFromFile(Path.Combine(partsPath, name + ".spdp"));
    }

    public static List<PacketPart> LoadDefinedPartsFromFile (NpcType npcType)
    {
        var name = npcType switch
        {
            NpcType.TradeMagic => "npc_trade_magic_old",
            NpcType.TradeWeapon => "npc_trade_wpon",
            NpcType.QuestKarma => "npc_quest_karma",
            NpcType.QuestTitle => "npc_quest_title",
            NpcType.QuestDegree => "npc_quest_degree",
            NpcType.Guilder => "npc_guilder",
            _ => "npc_trade"
        };
        var partsPath = MainServer.AppConfig["PacketPartPath"];
        return LoadFromFile(Path.Combine(partsPath, name + ".spdp"));
    }

    public static List<PacketPart> LoadFromFile (string filePath)
    {
        var contents = File.ReadAllLines(filePath);
        var parts = new List<PacketPart>();

        foreach (var line in contents)
        {
            var fieldValues = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);

            if (fieldValues.Length < 9)
            {
                Console.WriteLine($"Missing fields in {filePath}, line: {line}");
            }

            var partName = fieldValues[0];

            var packetPartType = Enum.TryParse(fieldValues[1], out PacketPartType partType)
                ? partType
                : PacketPartType.BITS;
            var start = int.Parse(fieldValues[2]);
            var length = 0;
            length = fieldValues[3] == LengthFromPreviousFieldValue
                ? BitStreamExtensions.BitsToInt(parts.Last().Value)
                : int.Parse(fieldValues[3]);

            var enumName = fieldValues[4];

            // r g b a are fields 5 6 7 8

            var value = fieldValues[9].Select(x => (Bit) (x - '0')).Reverse().ToList();
            var part = new PacketPart(partName, packetPartType, start, length, enumName, value);
            parts.Add(part);
        }

        return parts;
    }

    public static void UpdateCoordinates (List<PacketPart> list, double X, double Y, double Z, double T = 0,
        bool flipYAxis = true)
    {
        var xValueBytes = CoordsHelper.EncodeServerCoordinate(X);
        var xValue = new BitStream(xValueBytes).ReadBits(int.MaxValue).ToList();
        var yValueBytes = CoordsHelper.EncodeServerCoordinate(flipYAxis ? -Y : Y);
        var yValue = new BitStream(yValueBytes).ReadBits(int.MaxValue).ToList();
        var zValueBytes = CoordsHelper.EncodeServerCoordinate(Z);
        var zValue = new BitStream(zValueBytes).ReadBits(int.MaxValue).ToList();
        foreach (var part in list)
        {
            part.Value = part.Name switch
            {
                "x" => xValue,
                "y" => yValue,
                "z" => zValue,
                _ => part.Value
            };
        }
    }

    public static void UpdateValue (List<PacketPart> list, string name, int val, int length = 32)
    {
        var part = list.FirstOrDefault(x => x.Name == name);
        if (part is not null)
        {
            part.Value = BitStreamExtensions.IntToBits(val, length).ToList();
        }
    }

    public static void UpdateValue (List<PacketPart> list, string name, string val)
    {
        var part = list.FirstOrDefault(x => x.Name == name);
        var valBytes = MainServer.Win1251.GetBytes(val);
        var stream = new BitStream(valBytes);
        var bits = stream.ReadBits(int.MaxValue);
        if (part is not null)
        {
            part.Value = bits.ToList();
        }
    }

    public static void UpdateEntityId (List<PacketPart> list, ushort id)
    {
        var idpart = list.FirstOrDefault(x => x.Name == "entity_id");
        if (idpart is not null)
        {
            idpart.Value = BitStreamExtensions.IntToBits(id, 16).ToList();
        }
    }

    public static byte[] GetBytesToWrite (List<PacketPart> list)
    {
        var stream = BitHelper.GetWriteBitStream();
        foreach (var part in list)
        {
            stream.WriteBits(part.Value);
        }

        return Packet.ToByteArray(stream.GetStreamData(), 3);
    }
}