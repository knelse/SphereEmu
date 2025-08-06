using System;
using System.Collections.Generic;
using System.Linq;
using SphereHelpers.Extensions;
using SphServer.Client;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Helpers;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Networking.Chat.Encoders;
using SphServer.Shared.Networking.DataModel.Serializers;
using SphServer.Shared.WorldState;
using SphServer.System;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Server.Debug.Parser;

public enum ConsoleCommandParseResult
{
    OK,
    ERROR
}

public class ConsoleCommandParser
{
    private static readonly Dictionary<int, ConsoleCommandParser> ParserCache = new ();
    private readonly Dictionary<string, Action<string>> RegisteredCommands = new ();
    private readonly CharacterDbEntry currentCharacterDbEntry;
    private readonly SphereClient? sphereClient;

    private ConsoleCommandParser (CharacterDbEntry characterDbEntry)
    {
        currentCharacterDbEntry = characterDbEntry;
        sphereClient = ActiveClients.Get(characterDbEntry.ClientIndex);
    }

    public static ConsoleCommandParser Get (CharacterDbEntry characterDbEntry)
    {
        if (!ParserCache.ContainsKey(characterDbEntry.ClientIndex))
        {
            ParserCache.Add(characterDbEntry.ClientIndex, new ConsoleCommandParser(characterDbEntry));
            ParserCache[characterDbEntry.ClientIndex].InitCommands();
        }

        return ParserCache[characterDbEntry.ClientIndex];
    }

    private void InitCommands ()
    {
        RegisteredCommands["stats"] = UpdateStats;
        RegisteredCommands["money"] = UpdateMoney;
        RegisteredCommands["msg"] = SendMessage;
        RegisteredCommands["clan"] = UpdateClan;
        RegisteredCommands["packethex"] = SendPacketHex;
        RegisteredCommands["packet"] = SendPacket;
        RegisteredCommands["buff"] = Buff;
        RegisteredCommands["mob"] = Mob;
        RegisteredCommands["mobid"] = MobById;
        RegisteredCommands["loot"] = Loot;
        RegisteredCommands["tp"] = Teleport;
    }

    public ConsoleCommandParseResult Parse (string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ConsoleCommandParseResult.ERROR;
        }

        if (!input.StartsWith('/'))
        {
            return ConsoleCommandParseResult.ERROR;
        }

        var split = input[1..].Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (split.Length < 2)
        {
            return ConsoleCommandParseResult.ERROR;
        }

        var command = split[0];
        var args = split[1];

        if (!RegisteredCommands.TryGetValue(command, out var value))
        {
            return ConsoleCommandParseResult.ERROR;
        }

        value(args);

        return ConsoleCommandParseResult.OK;
    }

    private void UpdateStats (string args)
    {
        var stats = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        currentCharacterDbEntry.MaxHP = ushort.Parse(stats[0]);
        currentCharacterDbEntry.MaxMP = ushort.Parse(stats[1]);
        currentCharacterDbEntry.CurrentSatiety = ushort.Parse(stats[2]);
        currentCharacterDbEntry.MaxSatiety = ushort.Parse(stats[3]);
        currentCharacterDbEntry.CurrentStrength = ushort.Parse(stats[4]);
        currentCharacterDbEntry.CurrentAgility = ushort.Parse(stats[5]);
        currentCharacterDbEntry.CurrentAccuracy = ushort.Parse(stats[6]);
        currentCharacterDbEntry.CurrentEndurance = ushort.Parse(stats[7]);
        currentCharacterDbEntry.CurrentEarth = ushort.Parse(stats[8]);
        currentCharacterDbEntry.CurrentAir = ushort.Parse(stats[9]);
        currentCharacterDbEntry.CurrentWater = ushort.Parse(stats[10]);
        currentCharacterDbEntry.CurrentFire = ushort.Parse(stats[11]);
        currentCharacterDbEntry.PDef = ushort.Parse(stats[12]);
        currentCharacterDbEntry.MDef = ushort.Parse(stats[13]);
        currentCharacterDbEntry.TitleMinusOne = ushort.Parse(stats[14]);
        currentCharacterDbEntry.DegreeMinusOne = ushort.Parse(stats[15]);
        currentCharacterDbEntry.Karma = (KarmaTier) ushort.Parse(stats[16]);
        currentCharacterDbEntry.KarmaCount = ushort.Parse(stats[17]);
        currentCharacterDbEntry.TitleXP = uint.Parse(stats[18]);
        currentCharacterDbEntry.DegreeXP = uint.Parse(stats[19]);
        currentCharacterDbEntry.AvailableTitleStats = ushort.Parse(stats[20]);
        currentCharacterDbEntry.AvailableDegreeStats = ushort.Parse(stats[21]);
        currentCharacterDbEntry.ClanRank = (ClanRank) ushort.Parse(stats[22]);
        currentCharacterDbEntry.Money = int.Parse(stats[23]);
        currentCharacterDbEntry.PAtk = int.Parse(stats[24]);
        currentCharacterDbEntry.MAtk = int.Parse(stats[25]);
        NetworkedStatsUpdater.Update(currentCharacterDbEntry);
    }

    private void UpdateMoney (string args)
    {
        var stats = args.Split(" ", StringSplitOptions.RemoveEmptyEntries);
        currentCharacterDbEntry.Money = int.Parse(stats[0]);
        NetworkedStatsUpdater.Update(currentCharacterDbEntry);
    }

    private void SendMessage (string args)
    {
        var chatData = args.Split(" ", StringSplitOptions.RemoveEmptyEntries);
        if (chatData.Length < 3)
        {
            Console.WriteLine("usage: /msg chat_type name message");
            return;
        }

        var chatType = int.Parse(chatData[0]);
        Console.WriteLine(chatType);

        var name = chatData[1].Replace("_", " ");
        var message = string.Join(" ", chatData[2..]);

        message = name + ": " + message;
        // <l="player://Обычный мул\[br\]\[img=\"sep,mid,0,4,0,2\"\]\[br\]\[t=\"#UISTR_TT_IW32a\"\]\[img=\"inf_32,mid,0,2,6,2\"\] \[cl=EEEEEE\]странник (2)\[cl=EEEEEE\]\[/t\]\[br\]\[t=\"#UISTR_TT_IW33a\"\]\[img=\"inf_33,mid,0,2,6,2\"\] \[cl=EEEEEE\]неучёный (1) \[cl=EEEEEE\]\[/t\]\[br\]Клан разный шмот (Сеньор)\[br\]\[img=\"sep,mid,0,4,0,2\"\]">Обычный мул</l>: abc 
        var response = MessageEncoder.EncodeToSendFromServer(message, name, chatType);

        sphereClient?.MaybeQueueNetworkPacketSend(response);
    }

    private void UpdateClan (string args)
    {
        var chatData = args.Split(" ", StringSplitOptions.RemoveEmptyEntries);
        if (chatData.Length < 1)
        {
            Console.WriteLine("usage: /clan action value");
            return;
        }

        var action = chatData[0].ToLowerInvariant();
        var targetRank = int.Parse(chatData[1]);
        switch (action)
        {
            case "rank":
                var responseStream = SphBitStream.GetWriteBitStream();
                var nameBytes = SphEncoding.Win1251.GetBytes(currentCharacterDbEntry.Clan.Name);
                responseStream.WriteBytes([
                    (byte) (24 + nameBytes.Length), 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00,
                    MajorByte(currentCharacterDbEntry.ClientIndex),
                    MinorByte(currentCharacterDbEntry.ClientIndex), 0x08,
                    0x40, 0xE3, 0xA2, 0xA0, (byte) (targetRank << 5)
                ]);

                // 0x3E, 0x1B, 0xA0, 0x61, 0xD1, 0x20}, 1, true);
                responseStream.WriteByte(0x0, 5);
                responseStream.WriteByte(MajorByte(currentCharacterDbEntry.ClientIndex));
                responseStream.WriteByte(MinorByte(currentCharacterDbEntry.ClientIndex));
                responseStream.WriteByte(0x0, 7);
                responseStream.WriteByte(0x1A);
                responseStream.WriteByte(0x16);
                responseStream.WriteByte((byte) (nameBytes.Length + 2));
                responseStream.WriteByte((byte) targetRank);
                responseStream.WriteBytes(nameBytes, nameBytes.Length, true);
                responseStream.WriteByte(0x0, 4);
                responseStream.WriteByte(0x0);

                var response = responseStream.GetStreamData();
                Console.WriteLine(Convert.ToHexString(response));
                sphereClient?.MaybeQueueNetworkPacketSend(response);
                break;
        }
    }

    private void SendPacketHex (string args)
    {
        var chatData = args.Split(" ", StringSplitOptions.RemoveEmptyEntries);
        if (chatData.Length < 1)
        {
            Console.WriteLine("usage: /sendpackethex packet");
        }
        else
        {
            try
            {
                var content = Convert.FromHexString(chatData[1]);
                sphereClient?.MaybeQueueNetworkPacketSend(content);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Not a hex string: " + ex.Message);
            }
        }
    }

    private void SendPacket (string args)
    {
        DebugConsole.SendSpherePacket($"/packet {args}", bytes => sphereClient.MaybeQueueNetworkPacketSend(bytes));
    }

    private void Buff (string args)
    {
        var jumpx4 =
            "3F002C01006AF6B98878800F80842E090000000000000000409145068002C0400903C0010000000000000000000044EDF9994D83C00A0F07F70391E1004F6F";
        //	 03C0120080DE7E0D8307F80048E8920000000000000000001459640028000C9430001C0000000000000000000040D49E9FD93408ACF070703F10F90D50C200
        var runSpeed =
            "3F002C0100720A2EC278800F80842E0900000000000000004091450680020C3CBD011C0000000000000000000040D49ECFE13408A8F00704046C28004F6F00";
        //   3F002C01002CEF8F9578800F80842E090000000000000000409145068002C0400903C0010000000000000000000044EDF91C4E83800A0F0704046C2800250C
        // var test =
        // 	"3F002C010012DF127E78800F80842E090000000000000000409145068002C0C0DB13C0010000000000000000000044ED799B4D83000A0F07E80304AF044F6F";
        sphereClient?.MaybeQueueNetworkPacketSend(Convert.FromHexString(jumpx4));
        sphereClient?.MaybeQueueNetworkPacketSend(Convert.FromHexString(runSpeed));
        // StreamPeer.PutData(Convert.FromHexString(test));
    }

    private void Mob (string args)
    {
        var split = args.Split(' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mobPacketName = split.Length == 0
            ? "mob"
            : "mob_" + split[0];
        DebugConsole.SendSpherePacket($"/packet {mobPacketName} onme",
            bytes => sphereClient.MaybeQueueNetworkPacketSend(bytes));
    }

    private void MobById (string args)
    {
        var split = args.Split(' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (split.Length != 1 || !int.TryParse(split[0], out var mobId))
        {
            Console.WriteLine(
                "Usage: /spawn_mob_id <id>. For the list of IDs check entityNamesCollected file");
        }
        else
        {
            DebugConsole.SendSpherePacket($"/packet mob_assassin onme",
                bytes => sphereClient.MaybeQueueNetworkPacketSend(bytes), true,
                list =>
                {
                    foreach (var idPart in list.Where(x => x.Name == "mob_type"))
                    {
                        var bits = BitStreamExtensions.IntToBits(mobId, 16).ToList();
                        idPart.Value = bits;
                    }
                });
        }
    }

    private void Loot (string args)
    {
        ItemContainerDbEntry.CreateHierarchyWithContents(currentCharacterDbEntry.X, currentCharacterDbEntry.Y,
            currentCharacterDbEntry.Z + 1,
            1,
            LootRatity.DEFAULT_MOB);
        ItemContainerDbEntry.CreateHierarchyWithContents(currentCharacterDbEntry.X, currentCharacterDbEntry.Y,
            currentCharacterDbEntry.Z + 2,
            1,
            LootRatity.DEFAULT_MOB);
        ItemContainerDbEntry.CreateHierarchyWithContents(currentCharacterDbEntry.X, currentCharacterDbEntry.Y,
            currentCharacterDbEntry.Z + 3,
            1,
            LootRatity.DEFAULT_MOB);
    }

    private void Teleport (string args)
    {
        var split = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (split.Length > 1)
        {
            try
            {
                var coords = split
                    .Select(double.Parse)
                    .ToArray();
                sphereClient?.MaybeQueueNetworkPacketSend(
                    new CharacterDbEntrySerializer(currentCharacterDbEntry).GetTeleportByteArray(new WorldCoords(
                        coords[0], coords[1], coords[2],
                        0)));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        else
        {
            try
            {
                // continent:type:point
                var location = split[0].Split(':');
                if (Enum.TryParse<Continents>(location[0], out var continent))
                {
                    if (Enum.TryParse<PoiType>(location[1], out var poiType))
                    {
                        var coords = SavedCoords.TeleportPoints[continent][poiType][location[2]];
                        sphereClient?.MaybeQueueNetworkPacketSend(
                            new CharacterDbEntrySerializer(currentCharacterDbEntry).GetTeleportByteArray(coords));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}