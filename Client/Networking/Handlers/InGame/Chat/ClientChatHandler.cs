using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SphServer.Helpers;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking.Chat.Encoders;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.System;
using static SphServer.Helpers.Continents;
using static SphServer.Helpers.PoiType;
using static SphServer.Helpers.Cities;

namespace SphServer.Client.Networking.Handlers.InGame.Chat;

public class ClientChatHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    public async Task Handle (double delta)
    {
        try
        {
            var chatTypeVal = ((clientConnection.ReceiveBuffer[18] & 0b11111) << 3) +
                              (clientConnection.ReceiveBuffer[17] >> 5);

            var firstPacket = clientConnection.ReceiveBuffer[..26];
            var packetCount = (firstPacket[23] >> 5) + ((firstPacket[24] & 0b11111) << 3);
            var packetStart = 26;
            var decodeList = new List<byte[]>();

            if (packetCount < 2)
            {
                SphLogger.Warning(
                    $"Chat: broken client chat packet {Convert.ToHexString(clientConnection.ReceiveBuffer)}");
                return;
            }

            for (var i = 0; i < packetCount; i++)
            {
                var packetLength = clientConnection.ReceiveBuffer[packetStart + 1] * 256 +
                                   clientConnection.ReceiveBuffer[packetStart];
                var packetEnd = packetStart + packetLength;
                var packetDecode = clientConnection.ReceiveBuffer[packetStart..packetEnd];
                packetStart = packetEnd;
                decodeList.Add(packetDecode);
            }

            var msgBytes = new List<byte>();
            var clientMessageContentBytes = new List<byte[]>
            {
                firstPacket[11..]
            };

            foreach (var decoded in decodeList)
            {
                var messagePart = decoded[21..];
                clientMessageContentBytes.Add(decoded[11..]);
                for (var j = 0; j < messagePart.Length - 1; j++)
                {
                    var msgByte = ((messagePart[j + 1] & 0b11111) << 3) + (messagePart[j] >> 5);
                    msgBytes.Add((byte) msgByte);
                }
            }

            // var chatTypeOverride = MainServer.Rng.Next(0, 17);
            // clientMessageContentBytes[0][6] = (byte)((clientMessageContentBytes[0][6] & 0b11111) + ((chatTypeOverride & 0b111) << 5));
            // clientMessageContentBytes[0][7] = (byte)((clientMessageContentBytes[0][7] & 0b11100000) + (chatTypeOverride >> 3));

            var serverResponseBytes = new List<byte>();

            // client_id 084043 content
            byte[] GetResponseArray (byte[] clientBytes)
            {
                var responseBytes = new byte[clientBytes.Length + 7];
                responseBytes[0] = (byte) (responseBytes.Length % 256);
                responseBytes[1] = (byte) (responseBytes.Length / 256);
                responseBytes[2] = 0x2C;
                responseBytes[3] = 0x01;
                responseBytes[4] = 0x00;
                responseBytes[5] = 0x00;
                responseBytes[6] = 0x00;
                Array.Copy(clientBytes, 0, responseBytes, 7, clientBytes.Length);
                return responseBytes;
            }

            serverResponseBytes.AddRange(GetResponseArray(clientMessageContentBytes[0]));
            serverResponseBytes.AddRange(GetResponseArray(clientMessageContentBytes[1]));
            // StreamPeer.PutData(serverResponseBytes.ToArray());

            for (var i = 2; i < clientMessageContentBytes.Count; i++)
            {
                // StreamPeer.PutData(GetResponseArray(clientMessageContentBytes[i]));
                // Console.WriteLine(Convert.ToHexString(GetResponseArray(clientMessageContentBytes[i])));
            }

            var chatString = SphEncoding.Win1251.GetString(msgBytes.ToArray());
            var nameClosingTagIndex = chatString.IndexOf("</l>: ", StringComparison.OrdinalIgnoreCase);
            var nameStart = chatString.IndexOf("\\]\"", nameClosingTagIndex - 30, StringComparison.OrdinalIgnoreCase);
            var name = chatString[(nameStart + 4)..nameClosingTagIndex];
            var message = chatString[(nameClosingTagIndex + 6)..].TrimEnd((char) 0); // weird but necessary

            var response = MessageEncoder.EncodeToSendFromServer(chatString, name, chatTypeVal);
            clientConnection.MaybeQueueNetworkPacketSend(response);

            SphLogger.Info($"CLI: [{chatTypeVal}] {name}: {message}");

            if (message.StartsWith("/tp"))
            {
                // TODO: actual client commands
                var coords = message.Split(" ", StringSplitOptions.RemoveEmptyEntries);

                if (coords.Length < 2)
                {
                    SphLogger.Warning("Incorrect coods. Usage: /tp X Y Z OR /tp <name>");
                    return;
                }

                if (coords.Length == 2 && char.IsLetter(coords[1][0]))
                {
                    WorldCoords tpCoords;
                    if (coords[1].Equals("Shipstone", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][CityCenter][nameof (Shipstone)];
                    }
                    else if (coords[1].Equals("Bangville", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][CityCenter][nameof (Bangville)];
                    }
                    else if (coords[1].Equals("Torweal", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][CityCenter][nameof (Torweal)];
                    }
                    else if (coords[1].Equals("Sunpool", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][CityCenter][nameof (Sunpool)];
                    }
                    else if (coords[1].Equals("Umrad", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][CityCenter][nameof (Umrad)];
                    }
                    else if (coords[1].Equals("ChoiceIsland", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][Other]["ChoiceIsland"];
                    }
                    else if (coords[1].Equals("Arena", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Hyperion][Other]["Arena"];
                    }
                    else
                    {
                        SphLogger.Warning($"Unknown teleport destination: {coords[1]}");
                        return;
                    }

                    clientConnection.MaybeQueueNetworkPacketSend(
                        new CharacterDbEntrySerializer(clientConnection.GetSelectedCharacter()!).GetTeleportByteArray(
                            tpCoords));
                    return;
                }

                if (coords.Length < 4)
                {
                    SphLogger.Warning("Incorrect coords. Usage: /tp X Y Z OR /tp <name>");
                    return;
                }

                var teleportCoords =
                    new WorldCoords(double.Parse(coords[1]), -double.Parse(coords[2]), double.Parse(coords[3]));

                clientConnection.MaybeQueueNetworkPacketSend(
                    new CharacterDbEntrySerializer(clientConnection.GetSelectedCharacter()!).GetTeleportByteArray(
                        teleportCoords));
            }

            if (message.StartsWith("/buff"))
            {
                var jumpx4 =
                    "3F002C0100A01A29C678800F80842E0900000000000000004091450680020C3CBD011C0000000000000000000040D49E9FD93408ACF007F70391E0004F6F00";
                //	 3F002C0100500199AB78800F80842E090000000000000000409145068002C0C0D72AC0010000000000000000000044EDF9994D83C00A0F07F70391E1005FAB00
                // var runSpeed =
                // 	"3F002C0100720A2EC278800F80842E0900000000000000004091450680020C3CBD011C0000000000000000000040D49ECFE13408A8F00704046C28004F6F00";
                //   3F002C01002CEF8F9578800F80842E090000000000000000409145068002C0400903C0010000000000000000000044EDF91C4E83800A0F0704046C2800250C
                var test =
                    "3F002C010012DF127E78800F80842E090000000000000000409145068002C0C0DB13C0010000000000000000000044ED799B4D83000A0F07E80304AF044F6F";
                // working
                // var jumpx4 =
                // 	"3F002C010082EB07B278800F80842E0900000000000000004091450680020C3CBD011C0000000000000000000040D49E9FD93408ACF007F70391E0004F6F00";
                // var runSpeed =
                // 	"3F002C0100720A2EC278800F80842E0900000000000000004091450680020C3CBD011C0000000000000000000040D49ECFE13408A8F00704046C28004F6F00";
                clientConnection.MaybeQueueNetworkPacketSend(Convert.FromHexString(jumpx4));
                // StreamPeer.PutData(Convert.FromHexString(runSpeed));
                clientConnection.MaybeQueueNetworkPacketSend(Convert.FromHexString(test));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}