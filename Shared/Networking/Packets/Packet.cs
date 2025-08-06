using System;
using System.Collections.Generic;
using SphServer.Shared.__ToSortOut;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Networking.Serializers;

namespace SphServer.Packets;

public static class Packet
{
    private static readonly ushort PacketValidationCodeOK = 0x2C01;
    private static readonly byte[] EmptyPacketByteArray = [0x04, 0x00, 0xF4, 0x01];

    public static byte[] ToByteArray (byte[]? content = null, int padZeros = 2)
    {
        if (content is null)
        {
            return EmptyPacketByteArray;
        }

        var packetSize = (ushort) (content.Length + 4 + padZeros);

        var result = new byte [content.Length + 4 + padZeros];

        result[0] = SphereDbEntrySerializerBase.MinorByte(packetSize);
        result[1] = SphereDbEntrySerializerBase.MajorByte(packetSize);
        result[2] = SphereDbEntrySerializerBase.MajorByte(PacketValidationCodeOK);
        result[3] = SphereDbEntrySerializerBase.MinorByte(PacketValidationCodeOK);

        for (var i = 0; i < padZeros; i++)
        {
            result[4 + i] = 0x00;
        }

        content.CopyTo(result, 4 + padZeros);

        return result;
    }

    public static byte[] ItemsToPacket (ushort clientId, int bagId, List<ItemDbEntry> items)
    {
        var stream = SphBitStream.GetWriteBitStream();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var similarPacket = FindSimilarObjectPacketInDb(clientId, bagId, item);
            if (similarPacket is null)
            {
                continue;
            }

            similarPacket.Value.ToStream(stream);
            if (i >= items.Count - 1)
            {
                break;
            }

            if (GameObjectDataHelper.WeaponsAndArmor.Contains(similarPacket.Value.GameObject!.GameObjectType))
            {
                stream.WriteByte(0x7E >> 1, 7);
            }
            else
            {
                var seekBack = GameObjectDataHelper.Mantras.Contains(similarPacket.Value.GameObject.GameObjectType)
                               || GameObjectDataHelper.Powders.Contains(similarPacket.Value.GameObject.GameObjectType)
                               || GameObjectDataHelper.AlchemyMaterials.Contains(
                                   similarPacket.Value.GameObject.GameObjectType)
                    ? 1
                    : 0;
                stream.SeekBack(seekBack);
                stream.WriteByte(0x7E);
            }
        }

        return ToByteArray(stream.GetStreamData(), 3);
    }

    private static ObjectPacket? FindSimilarObjectPacketInDb (ushort clientId, int bagId, ItemDbEntry? item)
    {
        if (item is null)
        {
            return null;
        }

        Console.WriteLine(item.ToDebugString());
        var weaponArmorNotShiftedId = 243;
        var weaponArmorShiftedId = 153;
        var ringNotShiftedId = 666;
        var ringShiftedId = 637;
        var mantraId = 354;
        var alchemyId = 374;
        var powderId = 147;
        var foodAppleId = 2161;
        // var keyId = 296;
        // var mantraBookId = 317;
        // var tokenId = 330;
        // var diamondRingId = 569;

        ObjectPacket result;
        var objectType = item.GameObjectType.GetPacketObjectType();
        // Console.WriteLine(Enum.GetName(objectType));
        var suffixMod = item.Suffix == ItemSuffix.None
            ? (ushort) 81
            : (ushort) GameObjectDataHelper.ObjectTypeToSuffixLocaleMap[item.GameObjectType][item.Suffix].value;

        var dbId = -1;
        if (GameObjectDataHelper.WeaponsAndArmor.Contains(item.GameObjectType))
        {
            dbId = suffixMod > 1000 ? weaponArmorShiftedId : weaponArmorNotShiftedId;
        }

        else if (GameObjectDataHelper.Mantras.Contains(item.GameObjectType))
        {
            dbId = mantraId;
        }

        else if (GameObjectDataHelper.Powders.Contains(item.GameObjectType))
        {
            dbId = powderId;
        }

        else if (GameObjectDataHelper.AlchemyMaterials.Contains(item.GameObjectType))
        {
            dbId = alchemyId;
        }

        else if (item.GameObjectType is GameObjectType.Ring)
        {
            dbId = suffixMod > 1000 ? ringShiftedId : ringNotShiftedId;
        }

        else if (item.GameObjectType is GameObjectType.FoodApple)
        {
            dbId = foodAppleId;
        }

        if (dbId == -1)
        {
            Console.WriteLine(
                $"NOT FOUND: Type: {Enum.GetName(item.GameObjectType)} Suffix: {suffixMod} {Enum.GetName(item.Suffix)}");
            dbId = 4;
        }

        // result = MainServer.LiveServerObjectPacketCollection.FindOne(x => x.DbId == dbId);
        return null;
        // var client = MainServer.ActiveClients!.GetValueOrDefault(clientId, null);
        // if (client is null)
        // {
        //     return null;
        // }
        //
        // result.Id = client.GetLocalObjectId(item.Id);
        // if (objectType is not ObjectType.FoodApple)
        // {
        //     result.GameId = (ushort) item.GameId;
        //     result.SuffixMod = suffixMod;
        // }
        //
        // var bagLocalId = Client.GetLocalObjectId(clientId, bagId);
        // result.BagId = bagLocalId;
        // result.Count = (ushort) item.ItemCount;
        //
        // result.GameObject = MainServer.GameObjectCollection.FindById(item.GameObjectDbId);
        // if (objectType is not ObjectType.FoodApple)
        // {
        //     result.FriendlyName =
        //         MainServer.GameObjectCollection.FindById((int) result.GameId)!.Localisation[Locale.Russian];
        //     var type = result.GameObject.ObjectType;
        //     result.Type = (ushort) objectType;
        //     if (GameObjectDataHelper.ObjectTypeToSuffixLocaleMap.ContainsKey(type))
        //     {
        //         result.GameObject.Suffix = GameObjectDataHelper.ObjectTypeToSuffixLocaleMap[type]
        //             .GetSuffixById(result.SuffixMod);
        //     }
        // }
        //
        // return result;
    }
}