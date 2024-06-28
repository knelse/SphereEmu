using System;
using System.Collections.Generic;
using System.Linq;
using BitStreams;
using Godot;
using SphereHelpers.Extensions;
using SphServer;
using SphServer.DataModels;
using SphServer.Helpers;
using SphServer.Packets;

public enum NpcType
{
    TradeMagic,
    TradeAlchemy,
    TradeWeapon,
    TradeJewelry,
    TradeArmor,
    TradeTravelGeneric,
    TradeTravelTokens,
    TradeTavernkeeper,
    QuestTitle,
    QuestDegree,
    QuestKarma,
    Guilder,
    Banker,
    Prefix,
    Tournament
}

public enum VendorLocation
{
    Sunpool,
    Shipstone,
    Torweal,
    Bangville,
    Nomrad,
    Gifes,
    Anhelm,
    Outside,
    Castle
}

public static class NpcInteractableMappings
{
    public static int NpcTypeToNpcTradeTypeSph (NpcType npcType)
    {
        return npcType switch
        {
            NpcType.TradeMagic => 9,
            NpcType.TradeAlchemy => 6,
            NpcType.TradeWeapon => 11,
            NpcType.TradeJewelry => 8,
            NpcType.TradeArmor => 7,
            NpcType.TradeTavernkeeper => 5,
            NpcType.TradeTravelGeneric => 10,
            NpcType.TradeTravelTokens => 10,
            NpcType.QuestTitle => 4,
            NpcType.QuestDegree => 2,
            NpcType.QuestKarma => 3,
            NpcType.Guilder => 1,
            NpcType.Banker => 0,
            NpcType.Prefix => 12,
            NpcType.Tournament => 13,
            _ => 0
        };
    }
}

public partial class NpcInteractable : WorldObject
{
    [Export] public int NameID { get; set; } = 4016;
    [Export] public string ModelName { get; set; } = string.Empty;
    public string ModelNameSph => ModelName + "\0";
    [Export] public string IconName { get; set; } = string.Empty;
    public string IconNameSph => IconName + "\0";
    public int IconNameLength => IconNameSph.Length;

    [Export] public NpcType NpcType { get; set; }
    [Export] public int VendorItemTierMin { get; set; }
    [Export] public int VendorItemTierMax { get; set; }
    [Export] public VendorLocation VendorLocation { get; set; }
    private readonly List<Item> ItemsOnSale = [];

    private static readonly int[] AlchemyItemsOnSale =
    [
        900, 901, 902, 903, 905, 906, 907, 908, 909, 910, 911, 912, 913, 914, 915, 916, 917, 918, 919, 920, 921, 922,
        923, 924, 925, 926, 927, 928, 929, 930, 931, 940, 941, 942, 943, 944, 945, 946, 947, 948, 949, 950, 951, 952,
        953, 954, 955, 972, 973, 979, 980, 933, 934, 966, 967,
        // metals
        974, 975, 976, 977, 978, 970, 971
    ];

    private static readonly Dictionary<int, int[]> MagicItemsOnSalePerMinTier = new ()
    {
        [1] =
        [
            501, 509, 502, 510, 521, 503, 511, 522, 543, 504, 601, 607, 608, 635, 602, 609, 636, 610, 603, 646, 647,
            648, 701, 702, 703, 722, 704, 713, 705, 714, 801, 812, 807, 802, 813, 822, 836, 808, 841
        ],
        [4] =
        [
            512, 523, 550, 505, 513, 524, 529, 514, 525, 530, 611, 637, 641, 612, 619, 604, 613, 620, 626, 649, 723,
            734, 740, 706, 715, 727, 707, 716, 803, 814, 818, 823, 837, 809, 804, 815, 819
        ]
    };

    public override void _Ready ()
    {
        base._Ready();
        if (VendorItemTierMax == 0 || VendorItemTierMin == 0)
        {
            Console.WriteLine($"Vendor [{ID}] ({NpcType}) has no item tiers set");
        }
        else
        {
            GenerateItemsForSale();
        }
    }

    public override List<PacketPart> GetPacketParts ()
    {
        return PacketPart.LoadDefinedPartsFromFile(NpcType);
    }

    public override List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
    {
        PacketPart.UpdateValue(packetParts, "name_id", NameID - 4000, 11);
        var modelName = ModelNameSph;
        if (NpcType is NpcType.Guilder)
        {
            modelName = modelName.PadRight(16, '\0');
        }

        PacketPart.UpdateValue(packetParts, "entity_type_name_length", modelName.Length, 8);
        PacketPart.UpdateValue(packetParts, "entity_type_name", modelName);
        PacketPart.UpdateValue(packetParts, "icon_name_length", IconNameLength, 8);
        PacketPart.UpdateValue(packetParts, "icon_name", IconNameSph);
        var tradeType = NpcInteractableMappings.NpcTypeToNpcTradeTypeSph(NpcType);
        PacketPart.UpdateValue(packetParts, "npc_trade_type", tradeType, 4);
        return packetParts;
    }

    public override byte[] PostprocesPacketBytes (byte[] packet)
    {
        packet[^1] = 0;
        return packet;
    }

    public override void ClientInteract (ushort clientID,
        ClientInteractionType interactionType = ClientInteractionType.Unknown)
    {
        Console.WriteLine($"FROM NPC: Client [{clientID}] interacts with [{ID}] {ObjectType} -- {interactionType}");
        switch (interactionType)
        {
            case ClientInteractionType.OpenTrade:
                ShowItemList(clientID);
                ShowItemContents(clientID);
                break;
            default:
                break;
        }
    }

    private void GenerateItemsForSale ()
    {
        var itemsOnSale = new List<Item>();

        switch (NpcType)
        {
            case NpcType.TradeJewelry:
                GenerateItemsForSaleJewelry(itemsOnSale);
                break;
            case NpcType.TradeTravelGeneric:
                GenerateItemsForSaleTravelGeneric(itemsOnSale);
                break;
            case NpcType.TradeWeapon:
                GenerateItemsForSaleWeapon(itemsOnSale);
                break;
            case NpcType.TradeArmor:
                GenerateItemsForSaleArmor(itemsOnSale);
                break;
            case NpcType.TradeAlchemy:
                GenerateItemsForSaleAlchemy(itemsOnSale);
                break;
            case NpcType.TradeMagic:
                GenerateItemsForSaleMagic(itemsOnSale);
                break;
        }

        // if (itemsOnSale.Count == 0)
        // {
        //     for (var i = 0; i < 20; i++)
        //     {
        //         var item = Item.CreateFromGameObject(SphObjectDb.GameObjectDataDb[3400 + i]);
        //         ItemsOnSale.Add(item);
        //     }
        // }

        foreach (var item in itemsOnSale)
        {
            item.ParentContainerId = ID;
            item.Id = MainServer.GetNewWorldObjectIndex();
            ItemsOnSale.Add(item);
        }
    }

    private void ShowItemList (ushort clientId)
    {
        var localId = Client.GetLocalObjectId(clientId, ID);
        var stream = BitHelper.GetWriteBitStream();

        // return;
        stream.WriteUInt16(localId);
        stream.WriteByte(0, 2);
        stream.WriteUInt16((ushort) ObjectType, 10);
        stream.WriteByte(0, 1);
        // interaction
        stream.WriteByte(0x0A, 8);
        // open container
        stream.WriteUInt16(0x0103, 16);
        stream.WriteByte(0, 8);

        var itemSeparator = (ushort) 0b110000000001010;

        var packetBytes = new List<byte>();
        for (var i = 0; i < GetMaxItemsOnSale(); i++)
        {
            var item = ItemsOnSale[i];
            var slotId = i + 1;
            stream.WriteUInt16(itemSeparator, 15);
            stream.WriteByte((byte) slotId, 8);

            var itemLocalId = Client.GetLocalObjectId(clientId, item.Id);
            stream.WriteUInt16(itemLocalId);
            stream.WriteBytes([0x00, 0x00, 0x00, 0x00, 0x00], 5, true);
            stream.WriteUInt32((uint) item.VendorCost, 32);
            // 74 seems to be max amount vendor can display
            if (slotId % 28 != 0 && slotId != 74)
            {
                continue;
            }

            // split
            var packetPiece2 = Packet.ToByteArray(stream.GetStreamData(), 3);
            packetPiece2[^1] = 0;
            Console.WriteLine("---");
            Console.WriteLine(Convert.ToHexString(packetPiece2));
            packetBytes.AddRange(packetPiece2);
            stream.CutStream(0, 0);
            if (i == ItemsOnSale.Count - 1)
            {
                break;
            }

            stream.WriteUInt16(localId);
            stream.WriteByte(0, 2);
            stream.WriteUInt16((ushort) ObjectType, 10);
            stream.WriteByte(0, 2);
        }

        stream.WriteByte(0x3F, 7);
        stream.WriteUInt16(clientId);
        stream.WriteUInt32(0x62A34008);
        stream.WriteByte(0x0, 5);
        stream.WriteUInt16(localId);
        stream.WriteByte(0x0, 7);
        var streamData = stream.GetStreamData();
        streamData[^1] = 0;
        var packet = Packet.ToByteArray(streamData, 3);
        packetBytes.AddRange(packet);
        Console.WriteLine("---");
        Console.WriteLine(Convert.ToHexString(packet));
        Console.WriteLine("----FINAL----");
        var packetByteArray = packetBytes.ToArray();
        Console.WriteLine(Convert.ToHexString(packetByteArray));
        Client.TryFindClientByIdAndSendData(clientId, packetByteArray);
    }

    private int GetMaxItemsOnSale ()
    {
        return Math.Min(ItemsOnSale.Count, 74);
    }

    private void ShowItemContents (ushort clientId)
    {
        var stream = BitHelper.GetWriteBitStream();
        var packetList = new List<byte>();
        for (var i = 0; i < GetMaxItemsOnSale(); i++)
        {
            var item = ItemsOnSale[i];
            WriteItemPacketToStream(clientId, item, stream);
            // if (i > 0 && i % 5 == 0)
            // {
            // live splits items into batches of 5 and client seems to break if we send more than 10 at a time
            // but for now we'll send one at a time so we don't have to stitch them properly
            // if (stream.Bit != 0)
            // {
            //     // 1s would be left at the end if we don't fill
            //     stream.WriteByte(0, 8 - stream.Bit);
            // }
            if (stream.Bit != 0)
            {
                stream.WriteByte(0, 8 - stream.Bit);
            }

            var packet = Packet.ToByteArray(stream.GetStreamData(), 3);
            stream.CutStream(0, 0);
            Console.WriteLine(Convert.ToHexString(packet));
            // Client.TryFindClientByIdAndSendData(clientId, packet);
            packetList.AddRange(packet);
            continue;
            // }
            //
            // if (i != ItemsOnSale.Count - 1)
            // {
            //     var delimiter =
            //         GameObjectDataHelper.WeaponsAndArmor.Contains(item.GameObjectType) ||
            //         item.ObjectType is ObjectType.MantraBookSmall or ObjectType.MantraBookLarge
            //             or ObjectType.MantraBookGreat
            //             ? 0x7F
            //             : 0x7E;
            //     stream.WriteByte((byte) delimiter);
            // }
        }

        Client.TryFindClientByIdAndSendData(clientId, packetList.ToArray());

        // if (stream.Bit != 0)
        // {
        //     // 1s would be left at the end if we don't fill
        //     stream.WriteByte(0, 8 - stream.Bit);
        // }
        //
        // var packet = Packet.ToByteArray(stream.GetStreamData(), 3);
        // Console.WriteLine(Convert.ToHexString(packet));
        // Client.TryFindClientByIdAndSendData(clientId, packet);
    }

    private void WriteItemPacketToStream (ushort clientId, Item item, BitStream stream)
    {
        var actualObjectType = item.ObjectType == ObjectType.Unknown
            ? item.GameObjectType.GetPacketObjectType()
            : item.ObjectType;
        var packetParts = PacketPart.LoadDefinedPartsFromFile(actualObjectType);
        PacketPart.UpdateCoordinates(packetParts, 1000000, 0, 0, 0);
        var localId = Client.GetLocalObjectId(clientId, item.Id);
        PacketPart.UpdateEntityId(packetParts, localId);
        PacketPart.UpdateValue(packetParts, "object_type", (int) actualObjectType, 10);
        PacketPart.UpdateValue(packetParts, "game_object_id", item.GameId, 14);
        PacketPart.UpdateValue(packetParts, "container_id", item.ParentContainerId ?? 0xFF00, 16);
        if (item.ItemCount > 1)
        {
            PacketPart.UpdateValue(packetParts, "count", item.ItemCount, 15);
        }

        if (item.Suffix != ItemSuffix.None)
        {
            PacketPart.UpdateValue(packetParts, "__hasSuffix", 0, 1);
            var suffixLengthValue = (int) item.Suffix < 7 ? 0 : 1;
            PacketPart.UpdateValue(packetParts, "suffix_length", suffixLengthValue, 2);
            var suffixLength = suffixLengthValue == 0 ? 3 : 7;
            PacketPart.UpdateValue(packetParts, "suffix",
                GameObjectDataHelper.ObjectTypeToSuffixLocaleMapActual[item.GameObjectType][item.Suffix].value,
                suffixLength);
        }
        else
        {
            PacketPart.UpdateValue(packetParts, "__hasSuffix", 1, 1);
            PacketPart.UpdateValue(packetParts, "suffix_length", 0, 2);
            PacketPart.UpdateValue(packetParts, "suffix", 2, 3);
        }

        if (item.ContentsData.TryGetValue("scroll_id", out var value))
        {
            PacketPart.UpdateValue(packetParts, "subtype_id", (int) value, 15);
        }

        foreach (var part in packetParts)
        {
            stream.WriteBits(part.Value);
        }
    }

    private void GenerateItemsForSaleWeapon (List<Item> itemsOnSale)
    {
        for (var i = VendorItemTierMin; i <= VendorItemTierMax; i++)
        {
            var weaponsForTier = SphObjectDb.GameObjectDataDb.Where(x =>
                x.Value.GameId > 1000 &&
                x.Value.GameObjectType is GameObjectType.Sword or GameObjectType.Crossbow or GameObjectType.Axe &&
                x.Value.SuffixSetName.Length == 1 && x.Value.SuffixSetName != "-" &&
                x.Value.Tier == i).GroupBy(x => x.Value.GameObjectType).ToList();
            var output = new List<Item>();
            foreach (var weapons in weaponsForTier.Select(weapons => weapons.ToList()))
            {
                weapons.Sort((a, b) => GameObjectComparator(a.Value, b.Value));
                if (weapons.Count == 0)
                {
                    continue;
                }

                for (var j = 0; j < weapons.Count; j++)
                {
                    if (weaponsForTier.Count > 2 && weapons[j].Value.GameId % 2 == 0)
                    {
                        // skipping every item with even gameid
                        continue;
                    }

                    output.Add(GetItemForGameObject(weapons[j].Value, i));
                }
            }

            itemsOnSale.AddRange(output);
        }

        itemsOnSale.Add(new Item
        {
            ObjectType = ObjectType.Arrows,
            Weight = 75,
            VendorCost = 4,
            ItemCount = 1000
        });

        itemsOnSale.Sort(ItemComparator);
    }

    private void GenerateItemsForSaleArmor (List<Item> itemsOnSale)
    {
        for (var i = VendorItemTierMin; i <= VendorItemTierMax; i++)
        {
            var armorForTier = SphObjectDb.GameObjectDataDb.Where(x =>
                x.Value.GameId > 1000 &&
                x.Value.GameObjectType is GameObjectType.Chestplate or GameObjectType.Pants or GameObjectType.Gloves
                    or GameObjectType.Boots or GameObjectType.Belt or GameObjectType.Helmet
                    or GameObjectType.Shield
                && x.Value.SuffixSetName.Length == 1 && x.Value.SuffixSetName != "-" &&
                x.Value.Tier == i).GroupBy(x => x.Value.GameObjectType).ToList();
            var output = new List<Item>();
            foreach (var armorTypeList in armorForTier.Select(armorType => armorType.ToList()))
            {
                armorTypeList.Sort((a, b) => GameObjectComparator(a.Value, b.Value));
                if (armorTypeList.Count == 0)
                {
                    continue;
                }

                var type = armorTypeList[0].Value.GameObjectType;
                for (var j = 0; j < armorTypeList.Count; j++)
                {
                    if (type is GameObjectType.Chestplate or GameObjectType.Shield && armorTypeList.Count > 2 &&
                        armorTypeList[j].Value.GameId % 2 == 0)
                    {
                        // skipping every 2nd item in tier for armor and shields
                        continue;
                    }

                    output.Add(GetItemForGameObject(armorTypeList[j].Value, i));
                }
            }

            itemsOnSale.AddRange(output);
        }

        itemsOnSale.Sort(ItemComparator);
    }

    private void GenerateItemsForSaleTravelGeneric (List<Item> itemsOnSale)
    {
        itemsOnSale.Add(new Item
        {
            ObjectType = ObjectType.BackpackSmall,
            Weight = 200,
            VendorCost = 120
        });
        itemsOnSale.Add(new Item
        {
            ObjectType = ObjectType.BackpackLarge,
            Weight = 200,
            VendorCost = 240
        });
    }

    private void GenerateItemsForSaleAlchemy (List<Item> itemsOnSale)
    {
        foreach (var alchemyItemId in AlchemyItemsOnSale)
        {
            var go = SphObjectDb.GameObjectDataDb[alchemyItemId];
            var item = GetItemForGameObject(go, 1);
            item.ItemCount = 1000;
            itemsOnSale.Add(item);
        }
    }

    private void GenerateItemsForSaleMagic (List<Item> itemsOnSale)
    {
        if (VendorItemTierMin == 1)
        {
            itemsOnSale.Add(new Item
            {
                ObjectType = ObjectType.AlchemyPot,
                Weight = 500,
                VendorCost = 330
            });
            itemsOnSale.Add(new Item
            {
                ObjectType = ObjectType.RecipeBook,
                Weight = 200,
                VendorCost = 120
            });
            for (var i = 1; i <= 2; i++)
            {
                var itemId = 570 + i - 1;
                var go = SphObjectDb.GameObjectDataDb[itemId];
                var item = GetItemForGameObject(go, i);
                item.ItemCount = 1000;
                itemsOnSale.Add(item);
            }

            itemsOnSale.Add(new Item
            {
                ObjectType = ObjectType.PowderAmilus,
                Weight = 1,
                VendorCost = 5,
                ItemCount = 1000
            });
            itemsOnSale.Add(new Item
            {
                ObjectType = ObjectType.PowderFinale,
                Weight = 1,
                VendorCost = 3,
                ItemCount = 1000
            });
        }

        foreach (var magicId in MagicItemsOnSalePerMinTier[VendorItemTierMin])
        {
            var go = SphObjectDb.GameObjectDataDb[magicId];
            var item = GetItemForGameObject(go, 1);
            item.ItemCount = 1000;
            itemsOnSale.Add(item);
        }
    }

    private void GenerateItemsForSaleJewelry (List<Item> itemsOnSale)
    {
        itemsOnSale.Add(new Item
        {
            ObjectType = ObjectType.MantraBookSmall,
            Weight = 200,
            VendorCost = 350
        });
        for (var i = VendorItemTierMin; i < VendorItemTierMax; i++)
        {
            itemsOnSale.Add(GetItemForTier(ObjectType.Ring, i, true));
            itemsOnSale.Add(GetItemForTier(ObjectType.Ring, i, true));
        }

        itemsOnSale.Add(GetItemForTier(ObjectType.Ring, VendorItemTierMax, true));
        if (VendorItemTierMin != 1)
        {
            itemsOnSale.Add(GetItemForTier(ObjectType.Ring, VendorItemTierMin, true));
        }

        for (var i = VendorItemTierMin; i < VendorItemTierMax; i++)
        {
            itemsOnSale.Add(GetItemForTier([ObjectType.ArmorAmulet, ObjectType.ArmorBracelet], i, true));
            if (i == VendorItemTierMin && i != 1)
            {
                continue;
            }

            itemsOnSale.Add(GetItemForTier([ObjectType.ArmorAmulet, ObjectType.ArmorBracelet], i, true));
        }

        itemsOnSale.Add(GetItemForTier([ObjectType.ArmorAmulet, ObjectType.ArmorBracelet], VendorItemTierMax,
            true));
        if (VendorItemTierMin == 1)
        {
            for (var i = 0; i <= 2; i++)
            {
                var scroll = new Item
                {
                    ObjectType = ObjectType.ScrollLegend,
                    Weight = 25,
                    VendorCost = 50,
                    ItemCount = 1000,
                    ContentsData =
                    {
                        ["scroll_id"] = i
                    }
                };
                itemsOnSale.Add(scroll);
            }
        }

        for (var i = VendorItemTierMin; i < VendorItemTierMax; i++)
        {
            itemsOnSale.Add(GetItemForTier(ObjectType.ArmorRobe, i, true));
            itemsOnSale.Add(GetItemForTier(ObjectType.ArmorRobe, i, true));
        }

        itemsOnSale.Add(GetItemForTier(ObjectType.ArmorRobe, VendorItemTierMax, true));

        if (VendorItemTierMin != 1)
        {
            // trap elixir
            var tierMin = VendorItemTierMin switch
            {
                4 => 3,
                7 => 5,
                10 => 7,
                _ => 0
            };
            for (var i = tierMin; i <= tierMin + 1; i++)
            {
                var itemId = 570 + i - 1;
                var go = SphObjectDb.GameObjectDataDb[itemId];
                var item = GetItemForGameObject(go, i);
                item.ItemCount = 1000;
                itemsOnSale.Add(item);
            }
        }
    }

    private static bool ShouldHaveSuffix (ObjectType objectType, int tier)
    {
        if (objectType is ObjectType.Ring)
        {
            return true;
        }

        if (!LootHelper.ObjectTypesWithSuffixes.Contains(objectType))
        {
            return false;
        }

        // dumb rng to have higher tiers have suffixes less often
        var rand = MainServer.Rng.Next(tier * 2 + 1);
        return rand == tier * 2;
    }

    private static Item GetItemForTier (ObjectType objectType, int tier, bool withSuffixMaybe = false)
    {
        return GetItemForTier([objectType], tier, withSuffixMaybe);
    }

    private static Item GetItemForTier (HashSet<ObjectType> objectTypes, int tier, bool withSuffixMaybe = false)
    {
        var candidates = SphObjectDb.GameObjectDataDb.Where(x =>
                objectTypes.Contains(x.Value.GameObjectType.GetPacketObjectType()) && x.Value.Tier == tier
                && (!withSuffixMaybe || (x.Value.SuffixSetName.Length == 1 && x.Value.SuffixSetName != "-")))
            .ToList();
        var randomObjectId = MainServer.Rng.Next(candidates.Count);
        var randomGameObject = candidates.ElementAt(randomObjectId).Value;
        return GetItemForGameObject(randomGameObject, tier, withSuffixMaybe);
    }

    private static Item GetItemForGameObject (SphGameObject gameObject, int tier, bool withSuffixMaybe = false)
    {
        var clone = SphGameObject.CreateFromGameObject(gameObject);
        var withSuffix = withSuffixMaybe && ShouldHaveSuffix(clone.GameObjectType.GetPacketObjectType(), tier);
        if (withSuffix)
        {
            var suffixes =
                GameObjectDataHelper.ObjectTypeToSuffixLocaleMapActual.GetValueOrDefault(clone.GameObjectType, []);
            if (suffixes.Any())
            {
                var randSuffixId = MainServer.Rng.Next(suffixes.Count);
                var randSuffix = suffixes.ElementAt(randSuffixId);
                clone.Suffix = randSuffix.Key;
            }
        }

        return Item.CreateFromGameObject(clone);
    }

    private static int GameObjectComparator (SphGameObject a, SphGameObject b)
    {
        var sortOrder = new Dictionary<GameObjectType, int>
        {
            [GameObjectType.Chestplate] = 1,
            [GameObjectType.Chestplate_Unique] = 1,
            [GameObjectType.Chestplate_Quest] = 1,
            [GameObjectType.Shield] = 2,
            [GameObjectType.Shield_Quest] = 2,
            [GameObjectType.Shield_Unique] = 2,
            [GameObjectType.Pants] = 3,
            [GameObjectType.Pants_Quest] = 3,
            [GameObjectType.Pants_Unique] = 3,
            [GameObjectType.Helmet] = 4,
            [GameObjectType.Helmet_Premium] = 4,
            [GameObjectType.Helmet_Quest] = 4,
            [GameObjectType.Helmet_Unique] = 4,
            [GameObjectType.Belt] = 5,
            [GameObjectType.Belt_Quest] = 5,
            [GameObjectType.Belt_Unique] = 5,
            [GameObjectType.Boots] = 6,
            [GameObjectType.Boots_Quest] = 6,
            [GameObjectType.Boots_Unique] = 6,
            [GameObjectType.Gloves] = 7,
            [GameObjectType.Gloves_Quest] = 7,
            [GameObjectType.Gloves_Unique] = 7,
            [GameObjectType.Sword] = 8,
            [GameObjectType.Sword_Quest] = 8,
            [GameObjectType.Sword_Unique] = 8,
            [GameObjectType.Sword_Unique] = 8,
            [GameObjectType.Crossbow] = 9,
            [GameObjectType.Crossbow_Quest] = 9,
            [GameObjectType.Axe] = 10,
            [GameObjectType.Axe_Quest] = 10
        };
        var sortOrderA = sortOrder.GetValueOrDefault(a.GameObjectType, int.MaxValue);
        var sortOrderB = sortOrder.GetValueOrDefault(b.GameObjectType, int.MaxValue);
        var typeCompare = sortOrderA.CompareTo(sortOrderB);
        if (typeCompare != 0)
        {
            return typeCompare;
        }

        typeCompare = string.Compare(a.ModelNameInventory, b.ModelNameInventory,
            StringComparison.OrdinalIgnoreCase);
        return typeCompare != 0 ? typeCompare : a.GameId.CompareTo(b.GameId);
    }

    private static int ItemComparator (Item a, Item b)
    {
        var sortOrder = new Dictionary<GameObjectType, int>
        {
            [GameObjectType.Chestplate] = 1,
            [GameObjectType.Chestplate_Unique] = 1,
            [GameObjectType.Chestplate_Quest] = 1,
            [GameObjectType.Shield] = 2,
            [GameObjectType.Shield_Quest] = 2,
            [GameObjectType.Shield_Unique] = 2,
            [GameObjectType.Pants] = 3,
            [GameObjectType.Pants_Quest] = 3,
            [GameObjectType.Pants_Unique] = 3,
            [GameObjectType.Helmet] = 4,
            [GameObjectType.Helmet_Premium] = 4,
            [GameObjectType.Helmet_Quest] = 4,
            [GameObjectType.Helmet_Unique] = 4,
            [GameObjectType.Belt] = 5,
            [GameObjectType.Belt_Quest] = 5,
            [GameObjectType.Belt_Unique] = 5,
            [GameObjectType.Boots] = 6,
            [GameObjectType.Boots_Quest] = 6,
            [GameObjectType.Boots_Unique] = 6,
            [GameObjectType.Gloves] = 7,
            [GameObjectType.Gloves_Quest] = 7,
            [GameObjectType.Gloves_Unique] = 7,
            [GameObjectType.Sword] = 8,
            [GameObjectType.Sword_Quest] = 8,
            [GameObjectType.Sword_Unique] = 8,
            [GameObjectType.Axe] = 9,
            [GameObjectType.Axe_Quest] = 9,
            [GameObjectType.Crossbow] = 10,
            [GameObjectType.Crossbow_Quest] = 10
        };
        var sortOrderA = sortOrder.GetValueOrDefault(a.GameObjectType, int.MaxValue);
        var sortOrderB = sortOrder.GetValueOrDefault(b.GameObjectType, int.MaxValue);
        var typeCompare = sortOrderA.CompareTo(sortOrderB);
        if (typeCompare != 0)
        {
            return typeCompare;
        }

        typeCompare = string.Compare(a.ModelNameInventory, b.ModelNameInventory,
            StringComparison.OrdinalIgnoreCase);
        return typeCompare != 0 ? typeCompare : a.GameId.CompareTo(b.GameId);
    }
}