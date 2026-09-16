using System.Text;
using BitStreams;
using LiteDB;
using SphereHelpers.Extensions;

public struct ObjectPacket
{
    [BsonId] public int DbId { get; set; }
    public ushort Id { get; set; }
    public Bit[] _skip1 { get; set; }
    public ushort Type { get; set; }
    public Bit[] _skip2 { get; set; }
    public byte[] X { get; set; }
    public byte[] Y { get; set; }
    public byte[] Z { get; set; }
    public byte[] T { get; set; }
    public ushort GameId { get; set; }
    public ushort SuffixMod { get; set; }
    public Bit[] _skip3 { get; set; }
    public ushort BagId { get; set; }
    public Bit[] _skip4 { get; set; }
    public ushort Count { get; set; }
    public bool IsPremium { get; set; }
    public Bit[] _premiumSkip { get; set; }
    public Bit[] _strangeSkip { get; set; }
    public string FriendlyName { get; set; }
    public SphGameObject? GameObject { get; set; }
    public byte[] Packet { get; set; }
    public long BitsRead { get; set; }
    public bool FourBitShiftedSuffix { get; set; }
    public bool IsStrangeSuffix { get; set; }
    public ObjectPacketEncodingGroup EncodingGroup { get; set; }
    public ObjectType ObjectType { get; set; }
    public byte ObjectSeparator => shorterObjectSeparator ? (byte)(0x7E >> 1) : (byte)0x7E;
    public int ObjectSeparatorLength => shorterObjectSeparator ? 7 : noObjectSeparator ? 0 : 8;

    private static readonly byte[] premiumByteMarker = { 0x05, 0x0F, 0x08 };
    private const int premiumIntMarker = 0x050F08;

    private bool shorterObjectSeparator =>
        EncodingGroup is ObjectPacketEncodingGroup.WeaponArmor;

    private bool noObjectSeparator => EncodingGroup is ObjectPacketEncodingGroup.FourSlotBag;

    public static ObjectPacket FromStream(BitStream stream, Locale localeForFriendlyName = Locale.Russian)
    {
        var result = new ObjectPacket
        {
            Id = stream.ReadUInt16(),
            _skip1 = stream.ReadBits(2),
            Type = stream.ReadUInt16(10),
            _skip2 = stream.ReadBits(10),
            X = stream.ReadBytes(4, true),
            Y = stream.ReadBytes(4, true),
            Z = stream.ReadBytes(4, true)
        };
        var longTailTest = stream.ReadBytes(4, true);
        var hasLongTail = longTailTest[2] != 0x16;
        stream.SeekBack(32);
        result.T = stream.ReadBytes(4, true);
        result.GameId = stream.ReadUInt16(14);

        var suffix = stream.ReadUInt16(12) >> 8;
        result.IsStrangeSuffix = suffix is 0xE or 0x9;
        result.FourBitShiftedSuffix = suffix == 0x4;
        stream.SeekBack(12);

        if (result.IsStrangeSuffix)
        {
            result._strangeSkip = stream.ReadBits(suffix is 0xE ? 31 : 26);
        }

        result.ObjectType = ObjectType.Unknown;
        if (Enum.IsDefined(typeof(ObjectType), result.Type))
        {
            result.ObjectType = (ObjectType)result.Type;
        }

        switch (result.ObjectType)
        {
            case ObjectType.Sack: // 4 bag slot
                result.GetStreamDataAs4SlotBag(stream);
                break;
            case ObjectType.Mantra_Book_Small:
            case ObjectType.Mantra_Book_Large:
            case ObjectType.Mantra_Book_Great:
                result.GetStreamDataAsMantraBook(stream);
                break;
            case ObjectType.Mantra_White:
            case ObjectType.Mantra_Black:
                result.GetStreamDataAsMantra(stream);
                break;
            case ObjectType.Alchemy_Mineral:
            case ObjectType.Alchemy_Plant:
            case ObjectType.Alchemy_Metal:
            case ObjectType.Powder_Single_Target:
            case ObjectType.Powder_Ao_E:
            case ObjectType.Elixir_Castle:
            case ObjectType.Elixir_Trap:
            case ObjectType.Monster_Part:
            case ObjectType.Arrow:
                result.GetStreamDataAsMaterialPowderElixir(stream);
                break;
            case ObjectType.Ring:
            case ObjectType.Ring_Golem:
                result.GetStreamDataAsRing(stream);
                break;
            case ObjectType.Ring_Ruby:
            case ObjectType.Ring_Diamond:
            case ObjectType.Ring_Gold:
            case ObjectType.Food_Apple:
            case ObjectType.Food_Pear:
            case ObjectType.Food_Meat:
            case ObjectType.Food_Bread:
            case ObjectType.Food_Fish2:
            case ObjectType.Alchemy_Brushwood:
                result.GetStreamDataAsBrushwoodFood(stream);
                break;
            case ObjectType.Token:
            case ObjectType.Token_Multiuse:
            case ObjectType.Token_Island:
            case ObjectType.Token_Island_Guest:
            case ObjectType.Token_Tutorial_Torweal:
                result.GetStreamDataAsToken(stream);
                break;
            case ObjectType.Blueprint: // craft formula
                result.GetStreamDataAsCraftFormula(stream);
                break;
            // case 0: // TBD: figure out, some chests get there
            // case 210: // chest, container
            case ObjectType.Chest2: // chest
                // case 415: // container with different shift
                result.GetStreamDataAsChestContainer(stream);
                break;
            // case 407:
            case ObjectType.Scroll_Legend:
            case ObjectType.Scroll_Recipe:
                result.GetStreamDataAsScroll(stream);
                break;
            // case 30: // mut added
            case ObjectType.Armor_Amulet:
            case ObjectType.Armor_Belt:
            case ObjectType.Armor_Boots:
            case ObjectType.Armor_Bracelet:
            case ObjectType.Armor_Chest:
            case ObjectType.Armor_Gloves:
            case ObjectType.Armor_Helmet:
            case ObjectType.Armor_Pants:
            case ObjectType.Armor_Robe:
            case ObjectType.Armor_Shield:
            case ObjectType.Quest_Armor_Chest2:
            case ObjectType.Quest_Armor_Belt:
            case ObjectType.Quest_Armor_Boots:
            case ObjectType.Quest_Armor_Bracelet:
            case ObjectType.Quest_Armor_Chest:
            case ObjectType.Quest_Armor_Gloves:
            case ObjectType.Quest_Armor_Helmet:
            case ObjectType.Quest_Armor_Pants:
            case ObjectType.Quest_Armor_Ring:
            case ObjectType.Quest_Armor_Robe:
            case ObjectType.Quest_Armor_Shield:
            case ObjectType.Weapon_Axe:
            case ObjectType.Weapon_Crossbow:
            case ObjectType.Weapon_Sword:
            case ObjectType.Quest_Weapon_Axe:
            case ObjectType.Quest_Weapon_Crossbow:
            case ObjectType.Quest_Weapon_Sword:
            case ObjectType.Armor_Helmet_Premium:

            case ObjectType.Unknown:
            default:
                result.GetStreamDataAsWeaponArmor(stream, hasLongTail);
                break;
        }

        // no support for seeking forth from the current position?
        try
        {
            var premiumLevelSequence = (stream.ReadByte() << 16) + (stream.ReadByte() << 8) + stream.ReadByte();
            result.IsPremium = premiumLevelSequence == premiumIntMarker;

            if (result.IsPremium)
            {
                result._premiumSkip = stream.ReadBits(3);
            }
            else
            {
                stream.SeekBack(24);
            }
        }
        catch (IOException)
        {
            // expected for non-premium at the end of the packet
        }

        if (SphObjectDb.GameObjectDataDb.ContainsKey(result.GameId))
        {
            result.FriendlyName = SphObjectDb.GameObjectDataDb[result.GameId].Localisation[localeForFriendlyName];
            result.GameObject = SphObjectDb.GameObjectDataDb[result.GameId];
            var type = result.GameObject.GameObjectType;

            if (GameObjectDataHelper.ObjectTypeToSuffixLocaleMap.TryGetValue(type, out var value))
            {
                result.GameObject.Suffix = value.GetSuffixById(result.SuffixMod);
            }
        }
        else
        {
            result.FriendlyName = ObjectPacketTools.GetFriendlyNameByObjectType(result.ObjectType);
        }

        result.BitsRead = stream.Offset * 8 + stream.Bit;
        return result;
    }

    public void ToStream(BitStream stream, bool withSeparator = false)
    {
        stream.WriteUInt16(Id);
        stream.WriteBits(_skip1);
        stream.WriteUInt16(Type, 10);
        stream.WriteBits(_skip2);
        stream.WriteBytes(X, 4, true);
        stream.WriteBytes(Y, 4, true);
        stream.WriteBytes(Z, 4, true);
        stream.WriteBytes(T, 4, true);
        stream.WriteUInt16(GameId, 14);

        if (FourBitShiftedSuffix)
        {
            stream.WriteUInt16(SuffixMod, 12);
        }
        else
        {
            stream.WriteByte((byte)SuffixMod);
        }

        stream.WriteBits(_skip3);
        stream.WriteUInt16(BagId);
        stream.WriteBits(_skip4);

        if (ObjectType is ObjectType.Arrow or ObjectType.Bead or ObjectType.Ruby or ObjectType.Token
            or ObjectType.Alchemy_Brushwood or ObjectType.Alchemy_Metal or ObjectType.Alchemy_Mineral
            or ObjectType.Alchemy_Plant or ObjectType.Elixir_Castle or ObjectType.Elixir_Trap or ObjectType.Food_Apple
            or ObjectType.Food_Bread or ObjectType.Food_Fish2 or ObjectType.Food_Meat or ObjectType.Food_Pear
            or ObjectType.Mantra_Black or ObjectType.Mantra_White or ObjectType.Monster_Part or ObjectType.Powder_Amilus
            or ObjectType.Powder_Finale or ObjectType.Powder_Single_Target or ObjectType.Ring_Diamond or ObjectType.Ring_Ruby
            or ObjectType.Seed_Castle or ObjectType.Token_Island or ObjectType.Powder_Ao_E)
        {
            stream.WriteUInt16(Count);
        }

        // ignore for now, ideally forever
        // if (IsPremium)
        // {
        //     stream.WriteBytes(premiumByteMarker, premiumByteMarker.Length);
        //     stream.WriteBits(_premiumSkip);
        // }
    }

    private void GetSuffixModSkip3BagId(BitStream stream)
    {
        SuffixMod = FourBitShiftedSuffix ? stream.ReadUInt16(12) : stream.ReadByte();

        _strangeSkip = stream.ReadBits(SuffixMod switch
        {
            0x11 => 28,
            0xD1 => 22,
            0xBA => 22,
            0xC2 => 27,
            _ => 0
        });

        if (_strangeSkip.Length != 0)
        {
            var suffix = stream.ReadUInt16(12) >> 8;
            FourBitShiftedSuffix = suffix == 0x4;
            stream.SeekBack(12);
            SuffixMod = FourBitShiftedSuffix ? stream.ReadUInt16(12) : stream.ReadByte();
        }

        _skip3 = stream.ReadBits(21);
        BagId = stream.ReadUInt16();
    }

    private void GetStreamDataAsMantra(BitStream stream)
    {
        GetSuffixModSkip3BagId(stream);
        _skip4 = stream.ReadBits(27);
        Count = stream.ValidPosition ? stream.ReadUInt16() : (ushort)1;
        EncodingGroup = ObjectPacketEncodingGroup.Mantra;
    }

    private void GetStreamDataAsMaterialPowderElixir(BitStream stream)
    {
        GetSuffixModSkip3BagId(stream);
        _skip4 = stream.ReadBits(86);
        Count = stream.ReadUInt16(16);

        if ((ushort)(Count & 0xFFF) == 0xFFF)
        {
            Count = 1;
        }
        else if (Count >> 15 == 1)
        {
            Count &= 0b111111111111;
            stream.SeekBack(12);
        }
        else
        {
            stream.SeekBack(1);
        }

        EncodingGroup = ObjectPacketEncodingGroup.MatPowderEli;
    }

    private void GetStreamDataAsRing(BitStream stream)
    {
        GetSuffixModSkip3BagId(stream);
        _skip4 = stream.ReadBits(197);
        EncodingGroup = ObjectPacketEncodingGroup.Ring;
    }

    private void GetStreamDataAsMantraBook(BitStream stream)
    {
        SuffixMod = stream.ReadByte();
        _skip3 = stream.ReadBits(6);
        BagId = stream.ReadUInt16();
        _skip4 = stream.ReadBits(34); //93
        EncodingGroup = ObjectPacketEncodingGroup.MantraBook;
    }

    private void GetStreamDataAs4SlotBag(BitStream stream)
    {
        SuffixMod = stream.ReadByte();

        if (SuffixMod != 192)
        {
            stream.SeekBack(22);
            _strangeSkip = stream.ReadBits(33);
            GameId = stream.ReadUInt16(14);
            SuffixMod = stream.ReadByte();
        }

        _skip3 = stream.ReadBits(6);
        BagId = stream.ReadUInt16();
        EncodingGroup = ObjectPacketEncodingGroup.FourSlotBag;
        _skip4 = stream.ReadBits(int.MaxValue);
    }

    private void GetStreamDataAsBrushwoodFood(BitStream stream)
    {
        SuffixMod = stream.ReadByte();
        _skip3 = stream.ReadBits(6);
        BagId = stream.ReadUInt16();
        _skip4 = stream.ReadBits(86);
        Count = stream.ReadUInt16(15);
        EncodingGroup = ObjectPacketEncodingGroup.BrushwoodFood;
    }

    private void GetStreamDataAsToken(BitStream stream)
    {
        SuffixMod = stream.ReadByte();
        _skip3 = stream.ReadBits(6);
        BagId = stream.ReadUInt16();
        _skip4 = stream.ReadBits(86);
        Count = stream.ReadUInt16(15);
        _skip4 = stream.ReadBits(51); // 58 total
        EncodingGroup = ObjectPacketEncodingGroup.Token;
    }

    private void GetStreamDataAsWeaponArmor(BitStream stream, bool hasLongTail)
    {
        GetSuffixModSkip3BagId(stream);
        //         _skip4 = stream.ReadBits(isPremium ? 86 : 71),
        _skip4 = stream.ReadBits(67);
        var skipBytes = BitStream.BitArrayToBytes(_skip4);

        if (skipBytes.Length > 1 && skipBytes[1] == 0xB)
        {
            var skipList = new List<Bit>(_skip4);
            var _skip = stream.ReadBits(29);
            skipList.AddRange(_skip);
            _skip4 = skipList.ToArray();
        }

        if (hasLongTail)
        {
            var skipList = new List<Bit>(_skip4);
            var _skip = stream.ReadBits(31);
            skipList.AddRange(_skip);
            _skip4 = skipList.ToArray();
        }

        EncodingGroup = ObjectPacketEncodingGroup.WeaponArmor;
    }

    private void GetStreamDataAsCraftFormula(BitStream stream)
    {
        SuffixMod = stream.ReadByte();
        _skip3 = stream.ReadBits(6);
        BagId = stream.ReadUInt16();
        _skip4 = stream.ReadBits(414);
        EncodingGroup = ObjectPacketEncodingGroup.CraftFormula;
    }

    private void GetStreamDataAsChestContainer(BitStream stream)
    {
        SuffixMod = stream.ReadByte(5);

        if (SuffixMod == 0x1A)
        {
            _skip3 = stream.ReadBits(42);
        }
        else if (SuffixMod == 0xA)
        {
            // happens with Type = 0, TBD
            _skip3 = stream.ReadBits(37);
        }
        else if (SuffixMod == 0x2)
        {
            // happens with Type = 415, TBD
            _skip3 = stream.ReadBits(64);
        }

        EncodingGroup = ObjectPacketEncodingGroup.ChestContainer;
    }

    private void GetStreamDataAsScroll(BitStream stream)
    {
        SuffixMod = stream.ReadByte();
        _skip3 = stream.ReadBits(6);
        BagId = stream.ReadUInt16();
        // _skip4 = stream.ReadBits(414);
        EncodingGroup = ObjectPacketEncodingGroup.Scroll;
    }

    public string ToDebugString()
    {
        var typeName = $"({Enum.GetName(ObjectType)!})";
        string tier;

        if (GameObject?.GameObjectType == GameObjectType.Ring)
        {
            tier = GameObject.TitleMinusOne > 0
                ? $"{GameObject.TitleMinusOne + 1}т"
                : GameObject.DegreeMinusOne > 0
                    ? $"{GameObject.DegreeMinusOne + 1}с"
                    : GameObject.ToRomanTierLiteral();
        }
        else
        {
            tier = GameObject?.ToRomanTierLiteral() ?? string.Empty;
        }

        var suffix = string.Empty;

        try
        {
            suffix = (GameObject?.Suffix ?? ItemSuffix.None) == ItemSuffix.None
                ? string.Empty
                : GameObjectDataHelper.ObjectTypeToSuffixLocaleMap.TryGetValue(GameObject!.GameObjectType,
                      out var value) &&
                  value.ContainsKey(GameObject!.Suffix)
                    ? @$" {value[GameObject!.Suffix]
                        .localization[Locale.Russian]}"
                    : string.Empty;
        }
        catch
        {
            Console.WriteLine($"No suffix for {GameObject?.GameObjectType} and ID {GameObject?.Suffix}");
        }

        var count = Count > 1 ? $" ({Count})" : string.Empty;
        var name = $"{FriendlyName}" + suffix + (string.IsNullOrEmpty(tier) ? tier : $" {tier}") + count;
        return $"{name,-44}ID: {Id:X4}  GMID: {GameId.ToString(),5}  " +
               $"Type: {Type.ToString(),4} {typeName,-24} Suff: {SuffixMod.ToString(),4}  Bag: {BagId:X4}  " +
               $"X: {Convert.ToHexString(X)}  Y: {Convert.ToHexString(Y)}  Z: {Convert.ToHexString(Z)}  T: " +
               $"{Convert.ToHexString(T)}  PA: {IsPremium.ToString(),5} {_skip1.ToByteString()}\t{_skip2.ToByteString()}\t" +
               $"{_skip3.ToByteString()}\t{_skip4.ToByteString()}\tPA: {_premiumSkip.ToByteString()}";
    }
}

public static class ObjectPacketTools
{
    public static List<ObjectPacket> GetObjectsFromPacket(byte[] packet)
    {
        byte[] trimmedPacket;
        if (packet[2] == 0x2C && packet[3] == 0x01 && packet[4] == 0x00)
        {
            // start of network packet
            trimmedPacket = new byte[packet.Length - 7];
            Array.Copy(packet, 7, trimmedPacket, 0, packet.Length - 7);
        }
        else
        {
            trimmedPacket = packet;
        }

        var containerStream = new BitStream(trimmedPacket);
        var result = new List<ObjectPacket>();
        var offsets = new List<long>();

        try
        {
            while (containerStream.ValidPosition)
            {
                var test = containerStream.ReadBytes(4, true);

                if (!containerStream.ValidPosition)
                {
                    break;
                }

                containerStream.SeekBack(32);

                if (IsObjectPacket(test))
                {
                    var pos = (containerStream.Offset - 16) * 8 + containerStream.Bit;
                    var currentOffset = containerStream.Offset;
                    var currentBit = containerStream.Bit;

                    containerStream.Seek(currentOffset - 14, currentBit);
                    containerStream.ReadBits(2);
                    var typeCheck = containerStream.ReadUInt16(10);

                    if (Enum.IsDefined(typeof(ObjectType), typeCheck))
                    {
                        offsets.Add(pos);
                    }

                    // Console.WriteLine($"Unknown: {typeCheck} at {currentOffset} {currentBit}");
                    containerStream.Seek(currentOffset, currentBit);
                }

                containerStream.ReadBit();
            }
        }
        catch (IOException)
        {
        }
        finally
        {
            if (offsets.Count > 0)
            {
                containerStream.Seek(offsets[0] / 8, (int)(offsets[0] % 8));
            }
        }

        offsets.Add(containerStream.Length * 8);

        var packets = new List<byte[]>();

        for (var i = 1; i < offsets.Count; i++)
        {
            var bitLength = offsets[i] - offsets[i - 1];
            var bits = containerStream.ReadBits(bitLength);
            var objectPacket = BitStream.BitArrayToBytes(bits);
            packets.Add(objectPacket);
        }

        foreach (var objectPacket in packets)
        {
            var packetStream = new BitStream(objectPacket);
            var obj = ObjectPacket.FromStream(packetStream);
            obj.Packet = objectPacket;
            result.Add(obj);
        }

        containerStream.GetStream().Dispose();

        return result;
    }

    public static string GetTextOutput(List<ObjectPacket> objectPackets, bool writePacketsToConsole = false)
    {
        if (objectPackets.Count == 0)
        {
            return string.Empty;
        }

        objectPackets.Sort((a, b) => a.BagId.CompareTo(b.BagId));

        var currentBagId = -1;
        var sb = new StringBuilder();

        foreach (var objectPacket in objectPackets)
        {
            if (objectPacket.BagId != currentBagId)
            {
                currentBagId = objectPacket.BagId;

                if (writePacketsToConsole)
                {
                    Console.WriteLine($"[{currentBagId:X4}]");
                }

                sb.AppendLine($"[{currentBagId:X4}]");
            }

            if (writePacketsToConsole)
            {
                Console.WriteLine(objectPacket.BitsRead + " " + Convert.ToHexString(objectPacket.Packet));
            }

            sb.AppendLine(objectPacket.ToDebugString());
        }

        return sb.ToString();
    }

    public static int BitsToInt(Bit[] bits)
    {
        var result = 0;

        for (var i = bits.Length - 1; i >= 0; i--)
        {
            result <<= 1;
            result += bits[i];
        }

        return result;
    }

    public static Bit[] IntToBits(uint val, int length)
    {
        var result = new List<Bit>();

        while (val > 0)
        {
            result.Add((int)val & 0b1);
            val >>= 1;
        }

        while (result.Count < length)
        {
            result.Add(0);
        }

        return result.ToArray();
    }

    public static string ToByteString(this Bit[]? bits)
    {
        return Convert.ToHexString(BitStream.BitArrayToBytes(bits));
    }

    public static string RemainderToByteString(this BitStream bitStream)
    {
        return Convert.ToHexString(bitStream.GetStreamDataFromCurrentOffsetAndBit());
    }

    public static string BitArrayToString(Bit[] bits)
    {
        var sb = new StringBuilder();

        foreach (var bit in bits)
        {
            sb.Append((int)bit);
        }

        return sb.ToString();
    }

    public static bool IsObjectPacket(byte[] test)
    {
        return (test[0] & 0b1111) is 0x8 or 0x9 or 0x0 // or 0xF9 or 0x08 or 0xF8 or 0x78 or 0x98)
               && test[1] >> 4 is 0x4 or 0x5 //0 or 0x4F or 0x5F or 0x5E or 0x5C or 0x58 or 0x47 or 0x50)
               && (test[2] & 0b1) is 0x1
               && test[3] is 0x44 or 0x45;
    }

    public static string GetFriendlyNameByObjectType(ObjectType objectType)
    {
        return (objectType switch
        {
            ObjectType.Arrow => "Стрелы",
            ObjectType.Bead => "Бусинка",
            ObjectType.Blueprint => "Формула",
            ObjectType.Ear => "Ухо",
            ObjectType.Firecracker => "Петарда",
            ObjectType.Firework_Celebration => "Фейерверк",
            ObjectType.Inkpot => "Чернильница",
            ObjectType.Key => "Ключ",
            ObjectType.Key_Single_Use => "Ключ",
            ObjectType.Sack => "Мешочек",
            ObjectType.Token => "Жетон телепортации",
            ObjectType.Alchemy_Brushwood => "Хворост",
            ObjectType.Alchemy_Pot => "Алхимический котелок",
            ObjectType.Backpack_Large => "Большая торба",
            ObjectType.Backpack_Small => "Малая торба",
            ObjectType.Ear_String => "Нитка для ушей",
            ObjectType.Food_Apple => "Яблоко",
            ObjectType.Food_Bread => "Хлебная лепешка",
            ObjectType.Food_Fish2 => "Сушеная рыба",
            ObjectType.Food_Meat => "Вяленое мясо",
            ObjectType.Food_Pear => "Груша",
            ObjectType.Key_Barn => "Ключ от амбара",
            ObjectType.Map_Book => "Книга карт",
            ObjectType.Recipe_Book => "Книга рецептов",
            ObjectType.Scroll_Legend => "Свиток (легенда)",
            ObjectType.Scroll_Recipe => "Свиток, рецепт",
            ObjectType.Seed_Castle => "Замковое семя",
            ObjectType.Special_Guild => "Гильдия",
            ObjectType.Special_Ability => "Спецспособность",
            ObjectType.Special_Ability_Steal => "Спецспособность",
            ObjectType.Guild_Specialization => "Специализация",
            ObjectType.Token_Island => "Жетон телепортации на ЛО",
            ObjectType.Token_Multiuse => "Жетон телепортации",
            ObjectType.Trade_License => "Торговая лицензия",
            ObjectType.Mantra_Book_Great => "Великая книга мантр",
            ObjectType.Mantra_Book_Large => "Большая книга мантр",
            ObjectType.Mantra_Book_Small => "Малая книга мантр",
            ObjectType.Token_Island_Guest => "Гостевой жетон на ЛО",
            ObjectType.Xp_X2_Pill_Degree => "Пилюля опыта (степень)",
            ObjectType.Ring_Diamond => "Кольцо с алмазом",
            ObjectType.Ring_Gold => "Золотое кольцо",
            ObjectType.Ring_Ruby => "Кольцо с рубином",
            ObjectType.Ruby => "Рубин",
            ObjectType.Mutator => "Мутатор",
            ObjectType.Powder_Amilus => "Порошок Амилуса",
            ObjectType.Powder_Finale => "Порошок Файналя",
            _ => Enum.GetName(objectType)
        })!;
    }

    public static bool IsQuestItem(ObjectType objectType)
    {
        return objectType is
            ObjectType.Quest_Armor_Chest2
            or ObjectType.Quest_Armor_Belt
            or ObjectType.Quest_Armor_Boots
            or ObjectType.Quest_Armor_Chest
            or ObjectType.Quest_Armor_Gloves
            or ObjectType.Quest_Armor_Bracelet
            or ObjectType.Quest_Armor_Helmet
            or ObjectType.Quest_Armor_Pants
            or ObjectType.Quest_Armor_Robe
            or ObjectType.Quest_Armor_Shield
            or ObjectType.Quest_Weapon_Axe
            or ObjectType.Quest_Weapon_Crossbow
            or ObjectType.Quest_Weapon_Sword;
    }
}