using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SphereHelpers.Extensions;
using SphServer.DataModels;

namespace SphServer.Helpers.ConsoleCommands;

public enum ConsoleCommandParseResult
{
    OK,
    ERROR
}

public class ConsoleCommandParser
{
    private static readonly Dictionary<int, ConsoleCommandParser> ParserCache = new ();
    private readonly Dictionary<string, Action<string>> RegisteredCommands = new ();
    private readonly Character CurrentCharacter;
    private StreamPeerTcp StreamPeer => getStreamPeer();

    private ConsoleCommandParser (Character character)
    {
        CurrentCharacter = character;
    }

    public static ConsoleCommandParser Get (Character character)
    {
        if (!ParserCache.ContainsKey(character.ClientIndex))
        {
            ParserCache.Add(character.ClientIndex, new ConsoleCommandParser(character));
            ParserCache[character.ClientIndex].InitCommands();
        }

        return ParserCache[character.ClientIndex];
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

        if (!RegisteredCommands.ContainsKey(command))
        {
            return ConsoleCommandParseResult.ERROR;
        }

        RegisteredCommands[command](args);

        return ConsoleCommandParseResult.OK;
    }

    private void UpdateStats (string args)
    {
        var stats = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        CurrentCharacter.MaxHP = ushort.Parse(stats[0]);
        CurrentCharacter.MaxMP = ushort.Parse(stats[1]);
        CurrentCharacter.CurrentSatiety = ushort.Parse(stats[2]);
        CurrentCharacter.MaxSatiety = ushort.Parse(stats[3]);
        CurrentCharacter.CurrentStrength = ushort.Parse(stats[4]);
        CurrentCharacter.CurrentAgility = ushort.Parse(stats[5]);
        CurrentCharacter.CurrentAccuracy = ushort.Parse(stats[6]);
        CurrentCharacter.CurrentEndurance = ushort.Parse(stats[7]);
        CurrentCharacter.CurrentEarth = ushort.Parse(stats[8]);
        CurrentCharacter.CurrentAir = ushort.Parse(stats[9]);
        CurrentCharacter.CurrentWater = ushort.Parse(stats[10]);
        CurrentCharacter.CurrentFire = ushort.Parse(stats[11]);
        CurrentCharacter.PDef = ushort.Parse(stats[12]);
        CurrentCharacter.MDef = ushort.Parse(stats[13]);
        CurrentCharacter.TitleMinusOne = ushort.Parse(stats[14]);
        CurrentCharacter.DegreeMinusOne = ushort.Parse(stats[15]);
        CurrentCharacter.Karma = (KarmaTier) ushort.Parse(stats[16]);
        CurrentCharacter.KarmaCount = ushort.Parse(stats[17]);
        CurrentCharacter.TitleXP = uint.Parse(stats[18]);
        CurrentCharacter.DegreeXP = uint.Parse(stats[19]);
        CurrentCharacter.AvailableTitleStats = ushort.Parse(stats[20]);
        CurrentCharacter.AvailableDegreeStats = ushort.Parse(stats[21]);
        CurrentCharacter.ClanRank = (ClanRank) ushort.Parse(stats[22]);
        CurrentCharacter.Money = int.Parse(stats[23]);
        CurrentCharacter.PAtk = int.Parse(stats[24]);
        CurrentCharacter.MAtk = int.Parse(stats[25]);
        PacketHelper.UpdateStatsForClient(CurrentCharacter);
    }

    private void UpdateMoney (string args)
    {
        var stats = args.Split(" ", StringSplitOptions.RemoveEmptyEntries);
        CurrentCharacter.Money = int.Parse(stats[0]);
        PacketHelper.UpdateStatsForClient(CurrentCharacter);
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
        var response = ChatHelper.GetChatMessageBytesForServerSend(message, name, chatType);
        StreamPeer.PutData(response);
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
                var responseStream = BitHelper.GetWriteBitStream();
                var nameBytes = MainServer.Win1251.GetBytes(CurrentCharacter.Clan.Name);
                responseStream.WriteBytes([
                    (byte) (24 + nameBytes.Length), 0x00, 0x2C, 0x01, 0x00, 0x00, 0x00,
                    BitHelper.MajorByte(CurrentCharacter.ClientIndex),
                    BitHelper.MinorByte(CurrentCharacter.ClientIndex), 0x08,
                    0x40, 0xE3, 0xA2, 0xA0, (byte) (targetRank << 5)
                ]);

                // 0x3E, 0x1B, 0xA0, 0x61, 0xD1, 0x20}, 1, true);
                responseStream.WriteByte(0x0, 5);
                responseStream.WriteByte(BitHelper.MajorByte(CurrentCharacter.ClientIndex));
                responseStream.WriteByte(BitHelper.MinorByte(CurrentCharacter.ClientIndex));
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
                StreamPeer.PutData(response);
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
                StreamPeer.PutData(content);
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteLine("Not a hex string: " + ex.Message);
            }
        }
    }

    private void SendPacket (string args)
    {
        TestHelper.SendSpherePacketFromConsole(args, StreamPeer);
    }

    private void Buff (string args)
    {
        var jumpx4 =
            "3F002C01006AF6B98878800F80842E090000000000000000409145068002C0400903C0010000000000000000000044EDF9994D83C00A0F07F70391E1004F6F";
        //	 03C0120080DE7E0D8307F80048E8920000000000000000001459640028000C9430001C0000000000000000000040D49E9FD93408ACF070703F10F90D50C200
        // var runSpeed =
        // 	"3F002C0100720A2EC278800F80842E0900000000000000004091450680020C3CBD011C0000000000000000000040D49ECFE13408A8F00704046C28004F6F00";
        //   3F002C01002CEF8F9578800F80842E090000000000000000409145068002C0400903C0010000000000000000000044EDF91C4E83800A0F0704046C2800250C
        // var test =
        // 	"3F002C010012DF127E78800F80842E090000000000000000409145068002C0C0DB13C0010000000000000000000044ED799B4D83000A0F07E80304AF044F6F";
        StreamPeer.PutData(Convert.FromHexString(jumpx4));
        // StreamPeer.PutData(Convert.FromHexString(runSpeed));
        // StreamPeer.PutData(Convert.FromHexString(test));
    }

    private void Mob (string args)
    {
        var split = args.Split(' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mobPacketName = split.Length == 0
            ? "mob"
            : "mob_" + split[0];
        TestHelper.SendSpherePacketFromConsole($"/packet {mobPacketName} onme", StreamPeer);
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
            TestHelper.SendSpherePacketFromConsole($"/packet mob_assassin onme", StreamPeer, true,
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
        ItemContainer.Create(CurrentCharacter.X, CurrentCharacter.Y, CurrentCharacter.Z + 1,
            1,
            LootRatity.DEFAULT_MOB);
        ItemContainer.Create(CurrentCharacter.X, CurrentCharacter.Y, CurrentCharacter.Z + 2,
            1,
            LootRatity.DEFAULT_MOB);
        ItemContainer.Create(CurrentCharacter.X, CurrentCharacter.Y, CurrentCharacter.Z + 3,
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
                StreamPeer.PutData(
                    CurrentCharacter.GetTeleportByteArray(new WorldCoords(coords[0], coords[1], coords[2],
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
                        StreamPeer.PutData(
                            CurrentCharacter.GetTeleportByteArray(coords));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }

    private StreamPeerTcp getStreamPeer ()
    {
        var client = MainServer.GetClient(CurrentCharacter.ClientIndex);
        return client.StreamPeer;
    }
}