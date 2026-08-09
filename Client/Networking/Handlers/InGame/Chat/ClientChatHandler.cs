using Godot;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Server;
using SphServer.Server.Broadcast;
using SphServer.Server.Config;
using SphServer.Server.Debug;
using SphServer.Server.Debug.Parser;
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

public class ClientChatHandler(ClientConnection clientConnection)
    : ISphereClientNetworkingHandler

{
    private static readonly PackedScene FireworkScene =
        (PackedScene)ResourceLoader.Load("res://Godot/Scenes/firework.tscn");

    public static ushort lastPlayerSpawned = 0;

    private static int nextChatServerSeq = 0xA800;

    /// <summary>Live median client→ACK is ~176ms; local RTT is ~0 so we pace the reply.</summary>
    private static readonly TimeSpan ChatReplyDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>Parts of the send being assembled; cleared when it completes or is abandoned.</summary>
    private readonly List<byte> pendingChatBytes = [];

    /// <summary>Far above any real chat send without being unbounded.</summary>
    private const int MaxPendingChatSendBytes = 4096;

    private string? lastHandledChatMessage;
    private DateTime lastHandledChatAt;
    private PendingChatReply? pendingReply;

    private sealed class PendingChatReply(
        DateTime sendAfterUtc,
        byte[] ack,
        byte[] nameBlock,
        byte[] body,
        string chatString,
        string message,
        string name,
        int chatTypeVal,
        bool skipBroadcast)
    {
        public DateTime SendAfterUtc { get; } = sendAfterUtc;
        public byte[] Ack { get; } = ack;
        public byte[] NameBlock { get; } = nameBlock;
        public byte[] Body { get; } = body;
        public string ChatString { get; } = chatString;
        public string Message { get; } = message;
        public string Name { get; } = name;
        public int ChatTypeVal { get; } = chatTypeVal;
        public bool SkipBroadcast { get; } = skipBroadcast;
    }

    public void FlushPendingReply()
    {
        if (pendingReply is null || DateTime.UtcNow < pendingReply.SendAfterUtc)
        {
            return;
        }

        var reply = pendingReply;
        pendingReply = null;

        clientConnection.MaybeScheduleNetworkPacketSend(reply.Ack);
        clientConnection.MaybeScheduleNetworkPacketSend(reply.NameBlock);
        clientConnection.MaybeScheduleNetworkPacketSend(reply.Body);

        if (!reply.SkipBroadcast)
        {
            ChatBroadcast.MaybeScheduleBroadcastToClients(reply.ChatString, reply.Message, reply.Name,
                reply.ChatTypeVal, clientConnection);
        }
    }

    /// <summary>
    ///     One frame of a send, trigger or part. Retail replies only after the full send is in —
    ///     answering early causes lost/duped lines.
    /// </summary>
    public async Task Accept(byte[] frame, double delta)
    {
        if (IsChatHeaderFrame(frame))
        {
            pendingChatBytes.Clear();
        }
        else if (pendingChatBytes.Count == 0)
        {
            SphLogger.Debug($"Chat: {frame.Length}B part with no open send. Skipped.");
            return;
        }

        pendingChatBytes.AddRange(frame);

        // A send that never completes would grow without limit.
        if (pendingChatBytes.Count > MaxPendingChatSendBytes)
        {
            SphLogger.Warning(
                $"Chat: send reached {pendingChatBytes.Count} bytes without completing; abandoning it.");
            pendingChatBytes.Clear();
            return;
        }

        if (IsChatSendComplete(pendingChatBytes))
        {
            var complete = pendingChatBytes.ToArray();
            pendingChatBytes.Clear();
            await Handle(complete, delta);
        }
    }

    public async Task Handle(byte[] frame, double delta)
    {
        try
        {
            if (!TryParseChatSend(frame, out var firstPacket, out var decodeList,
                    out var totalLength))
            {
                SphLogger.Warning(
                    $"Chat: incomplete/broken client chat packet {Convert.ToHexString(frame)}");
                return;
            }

            var chatTypeVal = ((firstPacket[18] & 0b11111) << 3) + (firstPacket[17] >> 5);

            var msgBytes = new List<byte>();
            foreach (var decoded in decodeList)
            {
                var messagePart = decoded[21..];
                for (var j = 0; j < messagePart.Length - 1; j++)
                {
                    var msgByte = ((messagePart[j + 1] & 0b11111) << 3) + (messagePart[j] >> 5);
                    msgBytes.Add((byte)msgByte);
                }
            }

            var chatString = SphEncoding.Win1251.GetString(msgBytes.ToArray());
            var nameClosingTagIndex = chatString.IndexOf("</l>: ", StringComparison.OrdinalIgnoreCase);
            var nameStart = chatString.IndexOf("\\]\"", nameClosingTagIndex - 30, StringComparison.OrdinalIgnoreCase);
            if (nameClosingTagIndex < 0 || nameStart < 0)
            {
                SphLogger.Warning($"Chat: failed to parse chat string from {totalLength}-byte send");
                return;
            }

            var name = chatString[(nameStart + 4)..nameClosingTagIndex];
            var message = chatString[(nameClosingTagIndex + 6)..].TrimEnd((char)0); // weird but necessary

            var serverSeq = NextChatServerSeq();
            var isDuplicate = lastHandledChatMessage == message &&
                              DateTime.UtcNow - lastHandledChatAt < TimeSpan.FromSeconds(2);
            if (!isDuplicate)
            {
                lastHandledChatMessage = message;
                lastHandledChatAt = DateTime.UtcNow;
            }

            // Defer reply onto the next Process ticks (don't block the Godot main thread).
            pendingReply = new PendingChatReply(
                DateTime.UtcNow + ChatReplyDelay,
                BuildChatSendAck(firstPacket[11..], serverSeq),
                BuildChatEchoFromClientFrame(decodeList[0], serverSeq, patchNameDisplayFlag: true),
                BuildChatEchoFromClientFrame(decodeList[1], serverSeq, patchNameDisplayFlag: false),
                chatString,
                message,
                name,
                chatTypeVal,
                skipBroadcast: isDuplicate);

            SphLogger.Info($"CLI: [{chatTypeVal}] {name}: {message}");

            // GM command intercept (DebugMode-gated): route '/'-prefixed chat to the command
            // parser. A recognised command is handled here (return); anything else falls through
            // to the legacy inline commands below.
            if (ServerConfig.AppConfig.DebugMode && message.StartsWith('/') &&
                clientConnection.GetSelectedCharacter() is { } gmCharacter &&
                ConsoleCommandParser.Get(gmCharacter).Parse(message) == ConsoleCommandParseResult.OK)
            {
                return;
            }

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
                        tpCoords = SavedCoords.TeleportPoints[Гиперион][CityCenter][nameof(Шипстоун)];
                    }
                    else if (coords[1].Equals("Bangville", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Гиперион][CityCenter][nameof(Бангвиль)];
                    }
                    else if (coords[1].Equals("Torweal", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Гиперион][CityCenter][nameof(Торвил)];
                    }
                    else if (coords[1].Equals("Sunpool", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Гиперион][CityCenter][nameof(Санпул)];
                    }
                    else if (coords[1].Equals("Umrad", StringComparison.InvariantCultureIgnoreCase))
                    {
                        tpCoords = SavedCoords.TeleportPoints[Гиперион][CityCenter][nameof(Умрад)];
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
                var character = clientConnection.GetSelectedCharacter();
                if (character is null)
                {
                    return;
                }

                var firework = FireworkScene.Instantiate<WorldObject>();
                firework.Angle = 0;
                firework.ObjectType = ObjectType.Firework;
                var origin = character.Origin;
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
                    parts => { PacketPart.UpdateValue(parts, "clan_name", (char)1 + "Зеленый Слоник\0", true, 8); });
            }

            else if (message.StartsWith("gates"))
            {
                clientConnection.MaybeScheduleNetworkPacketSend(
                    CommonPackets.DespawnEntity((ushort)WorldObjectIndex.GetCurrentIndex));
                // skip (char) 1 to make client think it has no owner
                DebugConsole.SendSpherePacket("/packet castle_gates_t onme",
                    clientConnection.MaybeScheduleNetworkPacketSend, true,
                    parts => { PacketPart.UpdateValue(parts, "clan_name", (char)1 + "Зеленый Слоник\0", true, 8); }
                );
            }

            else if (message.StartsWith("cdoor"))
            {
                clientConnection.MaybeScheduleNetworkPacketSend(
                    CommonPackets.DespawnEntity((ushort)WorldObjectIndex.GetCurrentIndex));
                DebugConsole.SendSpherePacket("/packet castle_entrance_aris",
                    clientConnection.MaybeScheduleNetworkPacketSend
                );
            }

            else if (message.StartsWith("keydoor"))
            {
                clientConnection.MaybeScheduleNetworkPacketSend(
                    CommonPackets.DespawnEntity((ushort)WorldObjectIndex.GetCurrentIndex));
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

    private static ushort NextChatServerSeq()
    {
        return (ushort)Interlocked.Increment(ref nextChatServerSeq);
    }

    public static bool IsChatHeaderFrame(byte[] frame) =>
        frame.Length == 0x1A && frame.Length >= 16 &&
        frame[13] == 0x08 && frame[14] == 0x40 && frame[15] == 0x43;

    public static bool IsChatSendComplete(IReadOnlyList<byte> buffer)
    {
        return TryParseChatSend(buffer is byte[] arr ? arr : buffer.ToArray(), out _, out _, out _);
    }

    private static bool TryParseChatSend(byte[] buffer, out byte[] firstPacket, out List<byte[]> decodeList,
        out int totalLength)
    {
        firstPacket = [];
        decodeList = [];
        totalLength = 0;

        if (buffer.Length < 26 || !IsChatHeaderFrame(buffer.AsSpan(0, 26).ToArray()))
        {
            return false;
        }

        firstPacket = buffer[..26];
        var packetCount = (firstPacket[23] >> 5) + ((firstPacket[24] & 0b11111) << 3);
        if (packetCount < 2)
        {
            return false;
        }

        var packetStart = 26;
        for (var i = 0; i < packetCount; i++)
        {
            if (packetStart + 2 > buffer.Length)
            {
                return false;
            }

            var packetLength = buffer[packetStart] | (buffer[packetStart + 1] << 8);
            var packetEnd = packetStart + packetLength;
            if (packetLength < 13 || packetEnd > buffer.Length)
            {
                return false;
            }

            decodeList.Add(buffer[packetStart..packetEnd]);
            packetStart = packetEnd;
        }

        totalLength = packetStart;
        return true;
    }

    /// <summary>
    ///     Echo of client.chat.send header content (id + 08 40 43 + fields).
    ///     Matches retail SRV 0x16 responses captured in source/chat*.txt.
    /// </summary>
    private static byte[] BuildChatSendAck(byte[] clientHeaderContent, ushort serverSeq)
    {
        var responseBytes = new byte[clientHeaderContent.Length + 7];
        responseBytes[0] = (byte)(responseBytes.Length % 256);
        responseBytes[1] = (byte)(responseBytes.Length / 256);
        responseBytes[2] = 0x2C;
        responseBytes[3] = 0x01;
        responseBytes[4] = 0x00;
        responseBytes[5] = (byte)(serverSeq & 0xFF);
        responseBytes[6] = (byte)(serverSeq >> 8);
        Array.Copy(clientHeaderContent, 0, responseBytes, 7, clientHeaderContent.Length);
        return responseBytes;
    }

    /// <summary>
    ///     Retail name/body reply: client continuation with the 4 encrypt bytes removed
    ///     and client seq replaced by the shared server seq (verified on live 44910→44914).
    ///     Name block also patches the display flag byte (client E07F40 → server E0BF40).
    /// </summary>
    private static byte[] BuildChatEchoFromClientFrame(byte[] clientFrame, ushort serverSeq,
        bool patchNameDisplayFlag)
    {
        if (clientFrame.Length < 13)
        {
            return clientFrame;
        }

        var payloadFromPlayerId = clientFrame[11..];
        var response = new byte[7 + payloadFromPlayerId.Length];
        response[0] = (byte)(response.Length % 256);
        response[1] = (byte)(response.Length / 256);
        response[2] = 0x2C;
        response[3] = 0x01;
        response[4] = 0x00;
        response[5] = (byte)(serverSeq & 0xFF);
        response[6] = (byte)(serverSeq >> 8);
        Array.Copy(payloadFromPlayerId, 0, response, 7, payloadFromPlayerId.Length);

        // Layout: len(2) 2C01(2) 00 seq(2) id(2) 08 40 43 E0 xx 40...
        // Live server uses high-bit form of xx (client E07F40 → server E0BF40).
        if (patchNameDisplayFlag && response.Length > 14 && response[12] == 0xE0)
        {
            response[13] = (byte)((response[13] | 0x80) & ~0x40);
        }

        return response;
    }
}