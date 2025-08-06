using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SphereHelpers.Extensions;
using SphServer.Packets;
using SphServer.Server.Config;
using SphServer.Shared.BitStream;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Server.Debug;

public class DebugConsole
{
    public static void SendSpherePacket (string input, Action<byte[]> sendPacketAction, bool generateNewId = true,
        Action<List<PacketPart>>? transformPacketPartValueAction = null, bool isPacketPart = true)
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
        var onClient = inputParams.Length == 3;
        if (onClient)
        {
            ChangeAllCoordsToFirstClient(packetParts);
        }

        transformPacketPartValueAction?.Invoke(packetParts);

        if (generateNewId)
        {
            foreach (var idPart in packetParts.Where(x => x.Name == "entity_id"))
            {
                var newIndex = WorldObjectIndex.New();
                var bits = BitStreamExtensions.IntToBits(newIndex, 16).ToList();
                idPart.Value = bits;
            }
        }

        var stream = SphBitStream.GetWriteBitStream();
        foreach (var part in packetParts)
        {
            stream.WriteBits(part.Value);
        }

        var streamBytes = stream.GetStreamData();
        var packetBytes = Packet.ToByteArray(streamBytes, 3);
        Console.WriteLine($"Sending {packetName} as {Convert.ToHexString(packetBytes)}");
        sendPacketAction(packetBytes);
    }

    private static void ChangeAllCoordsToFirstClient (List<PacketPart> list)
    {
        var client = ActiveClients.FirstOrDefault();
        PacketPart.UpdateCoordinates(list, client.CurrentCharacter.X, client.CurrentCharacter.Y,
            client.CurrentCharacter.Z);
    }
}