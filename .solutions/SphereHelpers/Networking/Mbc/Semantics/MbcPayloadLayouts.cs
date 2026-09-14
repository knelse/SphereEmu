namespace SphServer.Helpers.Networking;

public enum MbcValueKind
{
    UInt,
    ObjectId,
    SignedMinus30000,
    Text,
    Bytes
}

public sealed class MbcLayoutField
{
    public required string Name { get; init; }
    public int Bytes { get; init; }
    public MbcValueKind Kind { get; init; } = MbcValueKind.UInt;
}

/// <summary>
///     Hand-tuned payload layouts. This is the file to edit when a command's
///     inner bytes are understood better than the decompile dump.
///     Lookup order: module.region.cmd, handler.cmd, region.cmd, module.region.
/// </summary>
public sealed class MbcCommandLayout
{
    public IReadOnlyList<MbcLayoutField>? Fields { get; init; }
    public IReadOnlyDictionary<int, IReadOnlyList<MbcLayoutField>>? ByLength { get; init; }
    public IReadOnlyList<string>? SchemaFieldNames { get; init; }
    public Func<byte[], int, int, List<MbcDecodedField>>? Custom { get; init; }
}

public static class MbcPayloadLayouts
{
    public static readonly IReadOnlyDictionary<string, MbcCommandLayout> Map;

    static MbcPayloadLayouts()
    {
        var map = new Dictionary<string, MbcCommandLayout>(StringComparer.Ordinal);

        // ContMan / item region 4
        map["r4c1"] = Empty();
        map["r4c2"] = Mux(
            [U8("slot"), U24("item_id"), U32("count")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [5] = [U8("slot"), U32("count")]
            });
        map["r4c3"] = Mux([U8("clear_flag")]);
        map["r4c7"] = Mux(
            [U24("target_id")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [6] = [U24("target_id"), U24("extra_id")]
            });
        map["r4c9"] = Mux(
            [U32("amount")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [6] = [Text("display_name")]
            });
        map["r4c10"] = Mux([U24("player_id")]);
        map["r4c11"] = Mux([U24("parent_id")]);
        map["r4c12"] = Mux([U32("amount")]);
        map["r4c13"] = Mux(
            [U16("hp")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [4] = [U16("hp"), U16("hp2")],
                [5] = [U24("killer_id"), Hp16("hp_delta")],
                [6] = [U16("hp"), U16("hp2")]
            });
        map["r4c14"] = Mux([U8("state")]);
        map["r4c15"] = Mux([U24("container_id")]);

        map["r4c4"] = Mux(
            [U8("slot_a"), U8("slot_b")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [6] = [U8("slot_a"), U8("slot_b"), U32("split_count")]
            });
        map["r4c5"] = Mux([U8("slot")]);
        map["r4c6"] = Mux(
            [U8("slot")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [5] = [U8("slot"), U32("count")]
            });

        // TradeMan / item region 9
        map["r9c0"] = Mux([U8("slot")]);
        map["r9c1"] = Mux([U8("slot"), U24("item_id"), U32("count"), U32("count2")]);
        map["r9c2"] = Mux(
            [U8("slot"), U32("count")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [8] = [U8("slot"), U32("count"), U24("extra_id")]
            });
        map["r9c3"] = Mux(
            [U32("gold")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [6] = [U16("hp"), U16("hp2")],
                [5] = [U24("killer_id"), Hp16("hp_delta")]
            });
        map["r9c5"] = Mux([U8("gmsg_id"), U32("color")]);
        map["r9c7"] = Mux(
            [U8("title_level"), U8("degree_level")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [1] = [U8("karma_type")]
            });
        map["r9c11"] = Mux([U32("specab")]);
        map["r9c12"] = Mux([U8("gxp_flag")]);
        map["r9c14"] = Mux(
            [U8("suffix_a"), U8("suffix_b")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [1] = [U8("suffix_id")]
            });
        map["r9c15"] = new MbcCommandLayout { Custom = UnpackIndexedStat };

        // _player Manager region 12
        map["Manager.c1"] = Mux([U32("actor_id"), U32("color")]);
        map["Manager.c2"] = new MbcCommandLayout { Custom = UnpackChat };
        map["_player.r12c2"] = map["Manager.c2"];
        map["Manager.c4"] = Mux([U24("weapon_process")]);
        map["Manager.c5"] = Mux([U8("anim_id")]);
        map["Manager.c8"] = Mux([U24("item_id"), U24("merchant_id"), U32("price"), U32("count")]);
        map["Manager.c10"] = Mux([U8("enabled")]);
        map["Manager.c11"] = Mux([Hp16("clan_x"), Hp16("clan_z")]);
        map["Manager.c17"] = new MbcCommandLayout { Custom = UnpackSkillAdds };
        map["_player.r12c17"] = map["Manager.c17"];
        map["Manager.c18"] = Mux(
            [U8("effect_flag")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [2] = [U16("effect_id")]
            });
        map["Manager.c19"] = Mux([U24("process_id")]);
        map["Manager.c20"] = Mux([U8("action_flag")]);
        map["Manager.c21"] = Mux([U24("target_id")]);
        map["Manager.c26"] = Mux([U24("target_id"), U16("shot")]);
        map["Manager.c31"] = Mux([U24("item_id")]);
        map["Manager.c36"] = Mux([U24("target_id"), Text("name")]);
        map["Manager.c44"] = Mux([Text("console")]);
        map["Manager.c48"] = Mux([Hp16("mark_x"), Hp16("mark_z")]);

        map["Owner.c7"] = Mux([Text("character_name")]);
        map["Owner.c10"] = Mux([Text("login_blob")]);
        map["Owner.c12"] = Mux([U8("slot")]);

        map["RcvInfo.c0"] = Schema("hit_type", "hp_delta", "killer_kind", "flags");
        map["RcvInfo.c1"] = map["RcvInfo.c0"];
        map["RcvInfo.c2"] = map["RcvInfo.c0"];
        map["RcvInfo.c3"] = map["RcvInfo.c0"];
        map["RcvInfo.c4"] = map["RcvInfo.c0"];
        map["_player.r8"] = Schema("hit_type", "hp_delta", "killer_kind", "flags");

        map["_player.r6"] = Schema(
            "link0", "link1", "link2",
            "wear0", "wear1", "wear2", "wear3", "wear4",
            "wear5", "wear6", "wear7", "wear8");
        map["_player.r7"] = Schema(
            "name", "title", "name_style", "model", "gender",
            "x", "y", "z", "yaw", "bag_total",
            "slot_item_ids", "slot_counts",
            "hp", "hp2", "karma_type", "title_degree",
            "continent", "block_equip", "stat_i22", "money");
        map["_player.r11"] = Schema("position_words");
        map["_player.r1"] = Schema("rel_x", "rel_y", "rel_z", "angle");
        map["_player.r10"] = Schema("cmd", "value");

        map["NManager.c2"] = Mux([U8("flag")]);
        map["NAManager.c2"] = Mux([U8("overhead_mark")]);
        map["RegMReceiver.c1"] = Mux([Text("npc_title")]);
        map["RegMReceiver.c2"] = Mux([U32("class_gmsg_id")]);
        map["RegionManager.c1"] = Mux([U32("aim_x"), U32("aim_y"), U32("aim_z")]);
        map["Capture_Progress_on_client.c2"] = Mux([Text("caption")]);
        map["RegTmntReceiver.c3"] = Mux([Text("name")]);
        map["RegTmntReceiver.c8"] = Mux([U8("status")]);
        map["RegTmntReceiver.c11"] = Mux([U8("control")]);
        map["receiveArtisanMonitor.c0"] = Mux([U24("artisan_id")]);
        map["receiveArtisanMonitor.c4"] = Mux([U24("workshop_id")]);
        map["receiveOwnerArtisanMonitor.c4"] = Mux([U24("workshop_id")]);
        map["receiveRegExpTime.c0"] = Mux([U32("expire_time")]);

        Map = map;
    }

    public static MbcCommandLayout? Find(string module, string handler, int region, int? command)
    {
        if (command is { } cmd)
        {
            if (Map.TryGetValue($"{module}.r{region}c{cmd}", out var layout))
            {
                return layout;
            }

            if (!string.IsNullOrEmpty(handler) && Map.TryGetValue($"{handler}.c{cmd}", out layout))
            {
                return layout;
            }

            if (Map.TryGetValue($"r{region}c{cmd}", out layout))
            {
                return layout;
            }
        }

        if (Map.TryGetValue($"{module}.r{region}", out var regionLayout))
        {
            return regionLayout;
        }

        if (!string.IsNullOrEmpty(handler) && Map.TryGetValue($"{handler}.c{command}", out var handlerLayout))
        {
            return handlerLayout;
        }

        return null;
    }

    private static MbcCommandLayout Empty() => Mux([]);

    private static MbcCommandLayout Mux(
        IReadOnlyList<MbcLayoutField> fields,
        Dictionary<int, IReadOnlyList<MbcLayoutField>>? byLength = null) =>
        new() { Fields = fields, ByLength = byLength };

    private static MbcCommandLayout Schema(params string[] names) =>
        new() { SchemaFieldNames = names };

    private static MbcLayoutField U8(string name) => new() { Name = name, Bytes = 1 };
    private static MbcLayoutField U16(string name) => new() { Name = name, Bytes = 2 };
    private static MbcLayoutField U24(string name) =>
        new() { Name = name, Bytes = 3, Kind = MbcValueKind.ObjectId };
    private static MbcLayoutField U32(string name) => new() { Name = name, Bytes = 4 };
    private static MbcLayoutField Hp16(string name) =>
        new() { Name = name, Bytes = 2, Kind = MbcValueKind.SignedMinus30000 };
    private static MbcLayoutField Text(string name) =>
        new() { Name = name, Bytes = 0, Kind = MbcValueKind.Text };

    private static List<MbcDecodedField> UnpackIndexedStat(byte[] data, int bitOffset, int countWidth)
    {
        if (data.Length < 1)
        {
            return [];
        }

        var index = data[0];
        var name = MbcStatFields.Name(index);
        long value;
        int valueBytes;
        if (data.Length >= 5)
        {
            value = ReadLe(data, 1, 4);
            valueBytes = 4;
        }
        else if (data.Length >= 3)
        {
            value = ReadLe(data, 1, 2);
            valueBytes = 2;
        }
        else
        {
            return
            [
                Field(name + "_index", index, bitOffset + countWidth, 8, MbcValueKind.UInt)
            ];
        }

        return
        [
            Field("stat_index", index, bitOffset + countWidth, 8, MbcValueKind.UInt),
            Field(name, value, bitOffset + countWidth + 8, valueBytes * 8, MbcValueKind.UInt)
        ];
    }

    private static List<MbcDecodedField> UnpackSkillAdds(byte[] data, int bitOffset, int countWidth)
    {
        var fields = new List<MbcDecodedField>();
        if (data.Length < 32)
        {
            if (data.Length > 0)
            {
                fields.Add(Field("payload_len", data.Length, bitOffset + countWidth, 0, MbcValueKind.UInt));
            }

            return fields;
        }

        for (var i = 0; i < 8; i++)
        {
            var value = ReadLe(data, i * 4, 4);
            fields.Add(Field(MbcStatFields.SkillAdds[i], value,
                bitOffset + countWidth + i * 32, 32, MbcValueKind.UInt));
        }

        return fields;
    }

    private static List<MbcDecodedField> UnpackChat(byte[] data, int bitOffset, int countWidth)
    {
        var fields = new List<MbcDecodedField>();
        if (data.Length < 2)
        {
            if (data.Length == 1)
            {
                fields.Add(Field("channel", data[0], bitOffset + countWidth, 8, MbcValueKind.UInt));
            }

            return fields;
        }

        var start = bitOffset + countWidth;
        fields.Add(Field("channel", data[0], start, 8, MbcValueKind.UInt));
        fields.Add(Field("kind", data[1], start + 8, 8, MbcValueKind.UInt));
        if (data[1] == 1 && data.Length >= 8)
        {
            fields.Add(Field("text_length", ReadLe(data, 2, 4), start + 16, 32, MbcValueKind.UInt));
            fields.Add(Field("chunk_count", ReadLe(data, 6, 2), start + 48, 16, MbcValueKind.UInt));
            return fields;
        }

        if (data[1] == 2 && data.Length >= 4)
        {
            fields.Add(Field("chunk_index", ReadLe(data, 2, 2), start + 16, 16, MbcValueKind.UInt));
            var text = MbcPayloadDecoder.DecodeText(data.AsSpan(4));
            fields.Add(new MbcDecodedField
            {
                Kind = "text",
                Name = "text",
                StringValue = text,
                BitOffset = start + 32,
                BitLength = Math.Max(0, (data.Length - 4) * 8),
                Display = text
            });
            return fields;
        }

        var rest = MbcPayloadDecoder.DecodeText(data.AsSpan(1));
        fields.Add(new MbcDecodedField
        {
            Kind = "text",
            Name = "text",
            StringValue = rest,
            BitOffset = start + 8,
            BitLength = Math.Max(0, (data.Length - 1) * 8),
            Display = rest
        });
        return fields;
    }

    internal static long ReadLe(byte[] data, int offset, int width)
    {
        long value = 0;
        for (var i = 0; i < width && offset + i < data.Length; i++)
        {
            value |= (long)data[offset + i] << (8 * i);
        }

        return value;
    }

    internal static MbcDecodedField Field(string name, long value, int bitOffset, int bitLength, MbcValueKind kind)
    {
        var display = kind == MbcValueKind.ObjectId ? $"0x{value:X}" : value.ToString();
        if (kind == MbcValueKind.SignedMinus30000)
        {
            display = (value - 30000).ToString();
        }

        return new MbcDecodedField
        {
            Kind = kind == MbcValueKind.ObjectId ? "object_id" : "int",
            Name = name,
            IntValue = kind == MbcValueKind.SignedMinus30000 ? value - 30000 : value,
            BitOffset = bitOffset,
            BitLength = bitLength,
            Display = $"{name}={display}"
        };
    }
}
