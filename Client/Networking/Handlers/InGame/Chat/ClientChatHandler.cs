using Godot;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Server;
using SphServer.Server.Broadcast;
using SphServer.Server.Debug;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;
using SphServer.System;
using static SphServer.Helpers.Continents;
using static SphServer.Helpers.PoiType;
using static SphServer.Helpers.Cities;

namespace SphServer.Client.Networking.Handlers.InGame.Chat;

public class ClientChatHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler

{
    private static readonly PackedScene FireworkScene =
        (PackedScene) ResourceLoader.Load("res://Godot/Scenes/firework.tscn");

    public static ushort lastPlayerSpawned = 0;

    private string previousMessageContent = string.Empty;

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

            if (message == previousMessageContent)
            {
                // SphLogger.Info($"Skipping client message (same content): {message}. Client ID: {localId}");
                // return;
            }

            previousMessageContent = message;

            ChatBroadcast.MaybeScheduleBroadcastToClients(chatString, message, name, chatTypeVal, clientConnection);

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
                        tpCoords = SavedCoords.TeleportPoints[Гиперион][CityCenter][nameof (Шипстоун)];
                    }
                    else if (coords[1].Equals("Bangville", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Гиперион][CityCenter][nameof (Бангвиль)];
                    }
                    else if (coords[1].Equals("Torweal", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Гиперион][CityCenter][nameof (Торвил)];
                    }
                    else if (coords[1].Equals("Sunpool", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Гиперион][CityCenter][nameof (Санпул)];
                    }
                    else if (coords[1].Equals("Umrad", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Гиперион][CityCenter][nameof (Умрад)];
                    }
                    else if (coords[1].Equals("ChoiceIsland", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Гиперион][Other]["ChoiceIsland"];
                    }
                    else if (coords[1].Equals("Arena", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Гиперион][Other]["Arena"];
                    }
                    else
                    {
                        SphLogger.Warning($"Unknown teleport destination: {coords[1]}");
                        return;
                    }

                    clientConnection.MaybeScheduleNetworkPacketSend(
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
                    new WorldCoords(double.Parse(coords[1]), double.Parse(coords[2]), double.Parse(coords[3]));

                clientConnection.MaybeScheduleNetworkPacketSend(
                    new CharacterDbEntrySerializer(clientConnection.GetSelectedCharacter()!).GetTeleportByteArray(
                        teleportCoords));
            }

            else if (message.StartsWith("/buff"))
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
                clientConnection.MaybeScheduleNetworkPacketSend(Convert.FromHexString(jumpx4));
                // StreamPeer.PutData(Convert.FromHexString(runSpeed));
                clientConnection.MaybeScheduleNetworkPacketSend(Convert.FromHexString(test));
            }

            else if (message.StartsWith("/fire"))
            {
                var firework = FireworkScene.Instantiate<WorldObject>();
                firework.Angle = 0;
                firework.ObjectType = ObjectType.Firework;
                var origin = clientConnection.GetSelectedCharacter().Origin;
                SphLogger.Info($"Spawning firework at: {origin.X:F1} | {origin.Y:F1} | {origin.Z:F1}");
                SphereServer.ServerNode.CallDeferred(Node.MethodName.AddChild, firework);
                firework.Transform = new Transform3D(Basis.Identity, origin);
            }

            else if (message.StartsWith("/randplayer"))
            {
                DebugConsole.SendRandomPlayerPacket(clientConnection.MaybeScheduleNetworkPacketSend);
            }

            else if (message.StartsWith("/moveplayer"))
            {
                DebugConsole.MoveEntity(clientConnection.MaybeScheduleNetworkPacketSend);
            }

            else if (message.StartsWith("tablet"))
            {
                // skip (char) 1 to make client think it has no owner
                DebugConsole.SendSpherePacket("/packet castle_tablet onme",
                    clientConnection.MaybeScheduleNetworkPacketSend, true,
                    parts => { PacketPart.UpdateValue(parts, "clan_name", (char) 1 + "Зеленый Слоник\0", true, 8); });
            }

            else if (message.StartsWith("gates"))
            {
                clientConnection.MaybeScheduleNetworkPacketSend(
                    CommonPackets.DespawnEntity((ushort) WorldObjectIndex.GetCurrentIndex));
                // skip (char) 1 to make client think it has no owner
                DebugConsole.SendSpherePacket("/packet castle_gates_t onme",
                    clientConnection.MaybeScheduleNetworkPacketSend, true,
                    parts => { PacketPart.UpdateValue(parts, "clan_name", (char) 1 + "Зеленый Слоник\0", true, 8); }
                );
            }

            else if (message.StartsWith("cdoor"))
            {
                clientConnection.MaybeScheduleNetworkPacketSend(
                    CommonPackets.DespawnEntity((ushort) WorldObjectIndex.GetCurrentIndex));
                DebugConsole.SendSpherePacket("/packet castle_entrance_aris",
                    clientConnection.MaybeScheduleNetworkPacketSend
                );
            }

            else if (message.StartsWith("keydoor"))
            {
                clientConnection.MaybeScheduleNetworkPacketSend(
                    CommonPackets.DespawnEntity((ushort) WorldObjectIndex.GetCurrentIndex));
                DebugConsole.SendSpherePacket("/packet door_entrance_with_key_t onme",
                    clientConnection.MaybeScheduleNetworkPacketSend
                );
            }

            else if (message.StartsWith("key_test"))
            {
                DebugConsole.SendSpherePacket("/packet item_key_single_use",
                    clientConnection.MaybeScheduleNetworkPacketSend
                );
            }

            else if (message.StartsWith("test"))
            {
                // clientConnection.MaybeScheduleNetworkPacketSend(
                //     CommonPackets.DespawnEntity((ushort) WorldObjectIndex.GetCurrentIndex));
                DebugConsole.SendSpherePacket("/packet dungeon_test", clientConnection.MaybeScheduleNetworkPacketSend);
                // DebugConsole.SendSpherePacket("/packet container_test",
                //     clientConnection.MaybeScheduleNetworkPacketSend
                // );
                // clientConnection.MaybeScheduleNetworkPacketSend(
                //     Convert.FromHexString(
                //         "EA002C01008C9387F800C06F710630F00100B6130180321601008A120120B2C800507810B0BBF829CB200D3E05BB79228F56C622EE40512280041594F20350802000000000F84DCBF0193ED4B878229056C6223423532260441619000A1004000000008507818380C203036A5C3C11482B63119A912951E1A1817CD912080000000000000000E0A738C367F80000DB890040198B00004589001059640028401000000000141E0402040A0F0CDF1B70C55DA089443FA06FC5858706000000000000000000000000203C305481BE1523832612F50ED615171E1A000000000000000000000000000A0F0CD0"));
                // clientConnection.MaybeScheduleNetworkPacketSend(
                //     Convert.FromHexString(
                //         "9D002C01008C93E48AEF6A1389D07AE18A0B0F0D00000000000000000000000000850786C6F1B7E278D044223630B8E2C2430300000000000000000000000040E181A12EB9AEB8993691889E52AEB8F0D0000000000000000000000000005078607098862BCEB14C244AF79F2B2E3C3400000000000000000000000000141E18949FED8AE16913897070E58A0B0F0D0000000000000000000000000000"));
                // clientConnection.MaybeScheduleNetworkPacketSend(
                //     Convert.FromHexString(
                //         "CB002C01008C938A337C46E181019B5DAEB8BC3291A8A407AEB8F0D000000000000000000000000000507860B85A872B3EAA4D241247882B2E3C3400000000000000000000000000FC8A65F80C1F00603B110028631100A0281100228B0C0005080200000080C283808040E181C123A8AE38253491880F04AEB8F0D0000000000000000000000000005078602086AD2B6EAA4D249266942B2E3C3400000000000000000000000000141E180C00EB8AC1691389BC18E88A0B0F0D000000000000000000000000203C30E0FE"));
                // clientConnection.MaybeScheduleNetworkPacketSend(
                //     Convert.FromHexString(
                //         "BF002C01008C93E08A4B691389D8AFE78A0B0F0D000000000000000000000000008507065AC3B8E2E7CC44A28259B8E2C2430300000000000000000000000040E1818129F4AD1895369168E4F7ADB8F0D000000000000000000000000000F03B96E1337C0080ED4400A08C450080A24400882C3200142008000000000A0F0203028507865118B8E266DA44A247BBBAE2C2430300000000000000000000000040E181E139BDAE989F3691C8117FAEB8F0D00000000000000000000000000000"));
                // clientConnection.MaybeScheduleNetworkPacketSend(
                //     Convert.FromHexString(
                //         "E9002C01008C93C8327C860F00B09D080094B10800509408009145068002040100000040E1418040A0F0C000B35E576C4F9B48C44338575C786800000000000000000000000000F851CBF0193E00C076220050C6220040512200441619000A1004000000008507810381C20383D3CB5D713D6D2291147F5C71E1A101000000000000000000000000A0F0C0203DFE566C0E9A483404FA565C786800000000000000000000000000283C30406DD615E3D226127D62CC15171E1A00000000000000000000000000203C30E078C1154B8226120DF7D615171E1A000000000000000000000000000A0F0CFC"));
                // clientConnection.MaybeScheduleNetworkPacketSend(
                //     Convert.FromHexString(
                //         "61002C01008C934BBA6223DA44A2D97EB7E2C2430300000000000000000000000040E18101D55FAED8973691680AECADB8F0D000000000000000000000000000F0B396A1077CB264F04467948C451E9FA04440882C3200141E0474170A0F010100"));
                // clientConnection.MaybeScheduleNetworkPacketSend(
                //     Convert.FromHexString(
                //         "17002C01000F0F8B6C41A17152590669107011F5511400"));
                // DebugConsole.SendSpherePacket("/packet door_entrance_with_key_t onme ",
                //     clientConnection.MaybeScheduleNetworkPacketSend
                // );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}