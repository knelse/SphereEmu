using SphereHelpers.Extensions;
using SphServer.Packets;
using SphServer.Server.Config;
using SphServer.Shared.BitStream;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using SphServer.System;

namespace SphServer.Server.Debug;

public static class DebugConsole
{
    public static void SendSpherePacket (string input, Action<byte[]> sendPacketAction, bool generateNewId = true,
        Action<List<PacketPart>>? transformPacketPartValueAction = null, bool isPacketPart = true,
        bool coordsForEntityMove = false)
    {
        if (!ServerConfig.AppConfig.DebugMode)
        {
            SphLogger.Info($"Debug mode disabled, skipping command: {input}");
            return;
        }

        SphLogger.Info($"Sending debug command: {input}");

        var inputParams = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (inputParams.Length < 2 || (inputParams.Length == 3 && inputParams[2] != "onme"))
        {
            SphLogger.Info("Usage: /packet <sphere packet definition name> [onme]");
            return;
        }

        var packetName = inputParams[1];
        var path = Path.Combine(
            ServerConfig.AppConfig.PacketDefinitionPath,
            packetName + (isPacketPart ? PacketPart.ExportedPartExtension : PacketPart.PacketDefinitionExtension));

        if (!Path.Exists(path))
        {
            SphLogger.Warning($"Definition not found via path: {path}");
            return;
        }

        var packetParts = PacketPart.LoadFromFile(path);
        var onClient = inputParams is [_, _, "onme"];
        if (onClient)
        {
            ChangeAllCoordsToFirstClient(packetParts, coordsForEntityMove);
        }

        if (generateNewId)
        {
            foreach (var idPart in packetParts.Where(x => x.Name == "entity_id"))
            {
                var newIndex = WorldObjectIndex.New();
                var bits = BitStreamExtensions.IntToBits(newIndex, 16).ToList();
                idPart.Value = bits;
            }
        }

        transformPacketPartValueAction?.Invoke(packetParts);

        var stream = SphBitStream.GetWriteBitStream();
        foreach (var part in packetParts)
        {
            stream.WriteBits(part.Value);
        }

        while (stream.Bit > 0)
        {
            stream.WriteBit(0);
        }

        var streamBytes = stream.GetStreamData();
        var packetBytes = isPacketPart ? Packet.ToByteArray(streamBytes, 3) : streamBytes;
        Console.WriteLine($"Sending {packetName} as {Convert.ToHexString(packetBytes)}");
        sendPacketAction(packetBytes);
    }

    public static void SendRandomPlayerPacket (Action<byte[]> sendPacketAction)
    {
        SendSpherePacket("/packet entity_character onme", sendPacketAction, true, RandomizePlayerPacket);
    }

    public static void MoveEntity (Action<byte[]> sendPacketAction)
    {
        SendSpherePacket("/packet server_move_entity", sendPacketAction, false, UpdateMoveEntityPacket, false, true);
    }

    private static void UpdateMoveEntityPacket (List<PacketPart> packetParts)
    {
        var client = ActiveClients.FirstOrDefault();

        PacketPart.UpdateValue(packetParts, "entity_id", 4502, 16);
        PacketPart.UpdateValue(packetParts, "x_plus_32768", (int) (client.CurrentCharacter.X + 32768), 16);
        PacketPart.UpdateValue(packetParts, "y_plus_1200", (int) (-client.CurrentCharacter.Y + 1200), 13);
        PacketPart.UpdateValue(packetParts, "z_plus_32768", (int) (-client.CurrentCharacter.Z + 32768), 16);
        PacketPart.UpdateValue(packetParts, "angle", client.Angle, 8);
        // PacketPart.UpdateValue(packetParts, "x_plus_32768", 420 + 32768, 16);
        // PacketPart.UpdateValue(packetParts, "y_plus_1200", 150 + 1200, 13);
        // PacketPart.UpdateValue(packetParts, "z_plus_32768", -1288 + 32768, 16);
    }

    private static void RandomizePlayerPacket (List<PacketPart> packetParts)
    {
        var randomNameVal = SphRng.Rng.Next(1000);
        var randomClanNameVal = SphRng.Rng.Next(100);
        var randomName = $"test_{randomNameVal}";
        var randomClanName = $"Noobs_{randomClanNameVal}";

        // PacketPart.UpdateValue(packetParts, "character_name", randomName, true, 8);
        // PacketPart.UpdateValue(packetParts, "clan_name", randomClanName, true, 4);
    }

    private static void ChangeAllCoordsToFirstClient (List<PacketPart> list, bool coordsForEntityMove)
    {
        var client = ActiveClients.FirstOrDefault();
        if (!coordsForEntityMove)
        {
            PacketPart.UpdateCoordinates(list, client.CurrentCharacter.X, -client.CurrentCharacter.Y,
                -client.CurrentCharacter.Z);
        }
    }
}