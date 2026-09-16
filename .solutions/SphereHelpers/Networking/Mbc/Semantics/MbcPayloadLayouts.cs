namespace SphServer.Helpers.Networking;

public enum MbcValueKind
{
    UInt,
    ObjectId,
    SignedMinus30000,
    Float32,
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
        map["r4c8"] = Empty();
        map["r4c9"] = Mux(
            [U16("current_hp")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [5] = [U24("killer_id"), Hp16("hp_delta")],
                [6] = [Text("display_name")]
            });
        // _player ContMan cmd9: len6 name query; len5 killer+delta; else absolute HP (not monster ApplyDamageOrHp).
        map["_player.r4c8"] = map["r4c8"];
        map["_player.r4c9"] = map["r4c9"];
        // ContMan cmd0: PutHere(container_process) — u24 parent process id.
        map["r4c0"] = Mux([U24("container_id")]);
        // guild / st_map ContMan cmd9 (MBC getBLkType/getBLkID): len6 name; len5 u8+u32; else u32.
        var itemContManCmd9 = Mux(
            [U32("blk_id")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [5] = [U8("blk_type"), U32("blk_id")],
                [6] = [Text("display_name")]
            });
        map["guild.r4c9"] = itemContManCmd9;
        map["st_map.r4c9"] = itemContManCmd9;
        // Item-like region 61 (EInit): xyz floats, angle8, g_01A8, durability (MBC SetHealth/GetHealth
        // on g_rec_0464; no separate durability path), game_id, suffix_id.
        var itemSpawnSnapshot = Schema(
            "x", "y", "z", "angle", "contain_state",
            "current_durability", "max_durability", "game_id", "suffix_id");
        map["guild.r61"] = itemSpawnSnapshot;
        map["st_map.r61"] = itemSpawnSnapshot;
        map["r4c10"] = Mux([U24("player_id")]);
        map["r4c11"] = Mux([U24("parent_id")]);
        map["r4c12"] = Mux([U32("amount")]);
        map["r4c13"] = Mux(
            [U16("current_hp")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [4] = [U16("current_hp"), U16("max_hp")],
                [5] = [U24("killer_id"), Hp16("hp_delta")],
                [6] = [U16("current_hp"), U16("max_hp")]
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
                [6] = [U16("current_hp"), U16("max_hp")],
                [5] = [U24("killer_id"), Hp16("hp_delta")]
            });
        map["r9c4"] = Empty();
        map["r9c5"] = Mux([U8("gmsg_id"), U32("color")]);
        map["r9c7"] = Mux(
            [U8("title_level"), U8("degree_level")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [1] = [U8("karma_type")]
            });
        map["r9c8"] = Mux([U24("target_id"), U16("shot")]);
        map["r9c11"] = Mux([U32("specab")]);
        map["r9c12"] = Mux([U8("gxp_flag")]);
        map["r9c13"] = Mux([U32("anim_lock")]);
        map["r9c14"] = Mux(
            [U8("suffix_a"), U8("suffix_b")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [1] = [U8("suffix_id")]
            });
        map["r9c15"] = new MbcCommandLayout { Custom = UnpackIndexedStat };

        // Owner region 0 (login / select)
        map["Owner.c1"] = Empty();
        map["Owner.c3"] = Empty();
        map["Owner.c4"] = Empty();
        map["Owner.c5"] = Empty();
        map["Owner.c6"] = Empty();
        map["Owner.c7"] = new MbcCommandLayout { Custom = UnpackEnterWorld };
        map["Owner.c10"] = Mux([U8("client_variant"), Text("login")]);
        map["Owner.c12"] = Mux([U8("slot")]);

        // _player Manager region 12
        map["Manager.c1"] = Mux([U32("format_arg"), U32("color")]);
        map["Manager.c2"] = new MbcCommandLayout { Custom = UnpackChat };
        map["_player.r12c2"] = map["Manager.c2"];
        map["Manager.c4"] = Mux([U24("weapon_process")]);
        map["Manager.c5"] = Mux([U8("anim_id")]);
        map["Manager.c6"] = Mux(
            [U8("slot")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [12] = [U8("slot"), U24("item_id"), U32("count"), U32("trade_extra")],
                [9] = [U8("slot"), U32("count"), U32("trade_extra")]
            });
        map["Manager.c7"] = Mux([U8("slot"), U32("offer_count"), U24("counterparty_id")]);
        map["Manager.c8"] = Mux([U24("item_id"), U24("merchant_id"), U32("price"), U32("count")]);
        map["Manager.c9"] = new MbcCommandLayout { Custom = UnpackSystemMessage };
        map["Manager.c10"] = Mux(
            [U8("enabled")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [1] = [U8("enabled")],
                [11] = [F32("x"), F32("z"), U24("process_id")]
            });
        map["Manager.c11"] = Mux([Hp16("clan_x"), Hp16("clan_z")]);
        map["Manager.c12"] = new MbcCommandLayout { Custom = UnpackTable };
        map["Manager.c13"] = Mux([
            U24("item_process"), U24("qty"), U24("party_process"),
            U32("amount"), U24("alt_item"), U16("type_flag")
        ]);
        map["Manager.c14"] = Mux(
            [],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [1] = [U8("key_alarm")],
                [4] = [U32("link_id")],
                [5] = [U32("value")]
            });
        map["Manager.c15"] = Mux(
            [F32("x"), F32("y"), F32("z")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [16] = [F32("x"), F32("y"), F32("z"), F32("yaw")]
            });
        map["Manager.c16"] = Mux(
            [U8("flag")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [2] = [U16("continent")]
            });
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
        map["Manager.c22"] = new MbcCommandLayout { Custom = UnpackSetClan };
        map["Manager.c23"] = new MbcCommandLayout { Custom = UnpackSearchClan };
        map["Manager.c25"] = new MbcCommandLayout { Custom = UnpackGroupUpdate };
        map["Manager.c26"] = Mux([U24("target_id"), U16("shot")]);
        map["Manager.c27"] = Mux([U8("crc_mode")]);
        map["Manager.c29"] = Empty();
        map["Manager.c30"] = Empty();
        map["Manager.c31"] = Mux([U24("item_id")]);
        map["Manager.c32"] = new MbcCommandLayout { Custom = UnpackGetBonus };
        map["Manager.c33"] = Mux([U8("code")]);
        map["Manager.c34"] = Mux([U8("flag")]);
        map["Manager.c36"] = Mux([U24("target_id"), Text("name")]);
        map["Manager.c37"] = Mux([U32("action_index")]);
        map["Manager.c40"] = Empty();
        map["Manager.c42"] = Mux([U32("result")]);
        map["Manager.c44"] = Mux([Text("console")]);
        map["Manager.c45"] = Empty();
        map["Manager.c46"] = Mux([U32("inviter_token"), Text("inviter_name")]);
        map["Manager.c47"] = Mux([F32("mark_x"), F32("mark_z")]);
        map["Manager.c48"] = Mux([F32("mark_x"), F32("mark_z")]);
        map["Manager.c49"] = Mux([U32("item_id")]);
        map["Manager.c50"] = Mux([U32("inviter_token")]);
        map["Manager.c51"] = Mux([U32("inviter_token")]);
        map["Manager.c54"] = Mux([U32("shard"), U32("passport_shard")]);
        map["Manager.c57"] = new MbcCommandLayout { Custom = UnpackTmntInfo };
        map["Manager.c58"] = new MbcCommandLayout { Custom = UnpackTmntAction };
        map["Manager.c59"] = Mux([U8("is_gvg"), U8("tmnt_type")]);
        map["Manager.c60"] = Mux([U8("done_code")]);
        map["Manager.c61"] = Mux([U8("state"), U8("type")]);
        map["Manager.c62"] = Empty();
        map["Manager.c63"] = Empty();
        map["Manager.c64"] = Mux([U32("item_or_money_flag"), U32("count")]);

        map["RcvInfo.c0"] = Schema("hit_type", "hp_delta", "killer_kind", "flags");
        map["RcvInfo.c1"] = map["RcvInfo.c0"];
        map["RcvInfo.c2"] = map["RcvInfo.c0"];
        map["RcvInfo.c3"] = map["RcvInfo.c0"];
        map["RcvInfo.c4"] = map["RcvInfo.c0"];
        map["_player.r8"] = Schema("hit_type", "hp_delta", "killer_kind", "flags");

        map["_player.r1"] = Schema("rel_x", "rel_y", "rel_z", "angle");
        map["_player.r2"] = Schema("x", "y", "z", "angle", "flags");
        map["_player.r3"] = Schema("anim_id");
        map["_player.r5"] = Schema("size", "payload");
        map["_player.r6"] = Schema(
            "link0", "link1", "link2",
            "wear0", "wear1", "wear2", "wear3", "wear4",
            "wear5", "wear6", "wear7", "wear8");
        map["_player.r7"] = Schema(
            "name", "title", "name_style", "model", "gender",
            "x", "y", "z", "yaw", "bag_total",
            "slot_item_ids", "slot_counts",
            "current_hp", "max_hp", "karma_type", "title_degree",
            "continent", "block_equip", "stat_i22", "money");
        map["_player.r10"] = Schema("cmd", "value");
        // CycleSend: array4 count + 4×u32 IEEE floats from g_rec_0004 (x,y,z,yaw).
        map["_player.r11"] = Schema("x", "y", "z", "angle");
        map["_player.r61"] = Schema(
            "x", "y", "z", "yaw", "model",
            "name", "title", "name_style",
            "current_hp", "max_hp", "gender", "karma_type",
            "title_level", "degree_level", "block_equip", "is_invisible",
            "title_rebirth", "degree_rebirth", "overhead_mark");

        map["NManager.c2"] = Mux([U8("flag")]);
        map["NManager.c3"] = Empty();
        map["NManager.c4"] = Empty();
        map["NManager.c5"] = Mux(
            [U24("item_a"), U24("item_b")],
            byLength: new Dictionary<int, IReadOnlyList<MbcLayoutField>>
            {
                [7] = [U24("item_a"), U24("item_b"), U8("pad")]
            });
        map["NAManager.c1"] = Mux([U8("flag")]);
        map["NAManager.c2"] = Mux([U8("overhead_mark")]);
        map["RegMReceiver.c1"] = Mux([Text("npc_title")]);
        map["RegMReceiver.c2"] = Mux([U32("class_gmsg_id")]);
        map["RegionManager.c1"] = Mux([U32("aim_x"), U32("aim_y"), U32("aim_z")]);
        map["Capture_Progress_on_client.c2"] = Mux([Text("caption")]);
        map["RegTmntReceiver.c3"] = Mux([Text("name")]);
        map["RegTmntReceiver.c8"] = Mux([U8("status")]);
        map["RegTmntReceiver.c11"] = Mux([U8("control")]);
        map["receiveArtisanMonitor.c0"] = Mux([U24("artisan_id")]);
        map["receiveArtisanMonitor.c3"] = Mux([U32("customer_id")]);
        map["receiveArtisanMonitor.c4"] = Mux([U24("workshop_id")]);
        map["receiveArtisanMonitor.c7"] = Mux([U32("ignored")]);
        map["receiveArtisanMonitor.c9"] = Mux([U32("ignored")]);
        map["receiveOwnerArtisanMonitor.c0"] = Mux([U32("artisan_id")]);
        map["receiveOwnerArtisanMonitor.c1"] = Mux([U32("price")]);
        map["receiveOwnerArtisanMonitor.c2"] = Mux([U32("customer_id")]);
        map["receiveOwnerArtisanMonitor.c4"] = Mux([U24("workshop_id")]);
        map["receiveOwnerArtisanMonitor.c5"] = Mux([U32("target")]);
        map["receiveOwnerArtisanMonitor.c6"] = Mux([U32("target")]);
        map["receiveOwnerArtisanMonitor.c7"] = Mux([U32("ignored")]);
        map["receiveOwnerArtisanMonitor.c8"] = Mux([U32("target")]);
        map["receiveOwnerArtisanMonitor.c9"] = Mux([U32("ignored")]);
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
    private static MbcLayoutField F32(string name) =>
        new() { Name = name, Bytes = 4, Kind = MbcValueKind.Float32 };
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

    private static List<MbcDecodedField> UnpackEnterWorld(byte[] data, int bitOffset, int countWidth)
    {
        var fields = new List<MbcDecodedField>();
        if (data.Length == 0)
        {
            return fields;
        }

        var start = bitOffset + countWidth;
        var nul = 0;
        while (nul < data.Length && data[nul] != 0)
        {
            nul++;
        }

        var nameLen = nul < data.Length ? nul + 1 : data.Length;
        var name = MbcPayloadDecoder.DecodeText(data.AsSpan(0, Math.Min(nul, data.Length)));
        fields.Add(new MbcDecodedField
        {
            Kind = "text",
            Name = "character_name",
            StringValue = name,
            BitOffset = start,
            BitLength = nameLen * 8,
            Display = $"character_name={name}"
        });

        if (nul + 1 + 4 <= data.Length)
        {
            var model = ReadLe(data, nul + 1, 4);
            fields.Add(Field("model", model, start + (nul + 1) * 8, 32, MbcValueKind.UInt));
        }

        return fields;
    }

    private static List<MbcDecodedField> UnpackSystemMessage(byte[] data, int bitOffset, int countWidth)
    {
        var fields = new List<MbcDecodedField>();
        if (data.Length < 1)
        {
            return fields;
        }

        var start = bitOffset + countWidth;
        var mode = data[0];
        fields.Add(Field("mode", mode, start, 8, MbcValueKind.UInt));

        if (mode == 0 && data.Length >= 7)
        {
            fields.Add(Field("gmsg_id", ReadLe(data, 1, 2), start + 8, 16, MbcValueKind.UInt));
            fields.Add(Field("color", ReadLe(data, 3, 4), start + 24, 32, MbcValueKind.UInt));
            return fields;
        }

        if ((mode is 2 or 3) && data.Length >= 3)
        {
            fields.Add(Field("arg", ReadLe(data, 1, 2), start + 8, 16, MbcValueKind.UInt));
            if (data.Length > 3)
            {
                var rest = MbcPayloadDecoder.DecodeText(data.AsSpan(3));
                fields.Add(new MbcDecodedField
                {
                    Kind = "text",
                    Name = "text",
                    StringValue = rest,
                    BitOffset = start + 24,
                    BitLength = (data.Length - 3) * 8,
                    Display = $"text={rest}"
                });
            }

            return fields;
        }

        if (data.Length > 1)
        {
            var text = MbcPayloadDecoder.DecodeText(data.AsSpan(1));
            fields.Add(new MbcDecodedField
            {
                Kind = "text",
                Name = "text",
                StringValue = text,
                BitOffset = start + 8,
                BitLength = (data.Length - 1) * 8,
                Display = $"text={text}"
            });
        }

        return fields;
    }

    private static List<MbcDecodedField> UnpackTable(byte[] data, int bitOffset, int countWidth)
    {
        var fields = new List<MbcDecodedField>();
        if (data.Length < 2)
        {
            return fields;
        }

        var start = bitOffset + countWidth;
        fields.Add(Field("opcode", ReadLe(data, 0, 2), start, 16, MbcValueKind.UInt));
        if (data.Length > 2)
        {
            var text = MbcPayloadDecoder.DecodeText(data.AsSpan(2));
            fields.Add(new MbcDecodedField
            {
                Kind = "text",
                Name = "text",
                StringValue = text,
                BitOffset = start + 16,
                BitLength = (data.Length - 2) * 8,
                Display = $"text={text}"
            });
        }

        return fields;
    }

    private static List<MbcDecodedField> UnpackSetClan(byte[] data, int bitOffset, int countWidth)
    {
        var fields = new List<MbcDecodedField>();
        if (data.Length < 1)
        {
            return fields;
        }

        var start = bitOffset + countWidth;
        if (data.Length == 1)
        {
            fields.Add(Field("membership", data[0], start, 8, MbcValueKind.UInt));
            return fields;
        }

        var op = data[0];
        fields.Add(Field("op", op, start, 8, MbcValueKind.UInt));

        if (op == 4 && data.Length >= 4)
        {
            fields.Add(Field("prayer_id", ReadLe(data, 1, 3), start + 8, 24, MbcValueKind.ObjectId));
            return fields;
        }

        if (op == 6 && data.Length > 1)
        {
            var name = MbcPayloadDecoder.DecodeText(data.AsSpan(1));
            fields.Add(new MbcDecodedField
            {
                Kind = "text",
                Name = "clan_name",
                StringValue = name,
                BitOffset = start + 8,
                BitLength = (data.Length - 1) * 8,
                Display = $"clan_name={name}"
            });
            return fields;
        }

        if (data.Length > 1)
        {
            var rest = MbcPayloadDecoder.DecodeText(data.AsSpan(1));
            fields.Add(new MbcDecodedField
            {
                Kind = "text",
                Name = "rest",
                StringValue = rest,
                BitOffset = start + 8,
                BitLength = (data.Length - 1) * 8,
                Display = $"rest={rest}"
            });
        }

        return fields;
    }

    private static List<MbcDecodedField> UnpackSearchClan(byte[] data, int bitOffset, int countWidth)
    {
        var fields = new List<MbcDecodedField>();
        if (data.Length < 1)
        {
            return fields;
        }

        var start = bitOffset + countWidth;
        fields.Add(Field("op", data[0], start, 8, MbcValueKind.UInt));
        if (data.Length > 1)
        {
            var rest = MbcPayloadDecoder.DecodeText(data.AsSpan(1));
            fields.Add(new MbcDecodedField
            {
                Kind = "text",
                Name = "rest",
                StringValue = rest,
                BitOffset = start + 8,
                BitLength = (data.Length - 1) * 8,
                Display = $"rest={rest}"
            });
        }

        return fields;
    }

    private static List<MbcDecodedField> UnpackGroupUpdate(byte[] data, int bitOffset, int countWidth)
    {
        var fields = new List<MbcDecodedField>();
        if (data.Length < 1)
        {
            return fields;
        }

        var start = bitOffset + countWidth;
        fields.Add(Field("op", data[0], start, 8, MbcValueKind.UInt));
        if (data.Length > 1)
        {
            var slice = data[1..];
            fields.Add(new MbcDecodedField
            {
                Kind = "bytes",
                Name = "rest",
                ArrayValue = slice.Select(b => (long)b).ToArray(),
                StringValue = MbcPayloadDecoder.DecodeText(slice),
                BitOffset = start + 8,
                BitLength = slice.Length * 8,
                Display = slice.Length <= 16
                    ? $"rest={Convert.ToHexString(slice)}"
                    : $"rest[{slice.Length}]={Convert.ToHexString(slice.AsSpan(0, 12))}..."
            });
        }

        return fields;
    }

    private static List<MbcDecodedField> UnpackGetBonus(byte[] data, int bitOffset, int countWidth)
    {
        // mgrGetBonus(buf, len): switch on payload_len_bytes (arg1).
        // len1: u8 prize_slot_count; 0 => empty prize window.
        // len2: payload ignored; program_restart(smsInfoIcon), then smsShowPrizeWindow.
        // len>2: u8 prize_slot_count + pairs (u32 item_process, u32 amount), then prize window.
        var fields = new List<MbcDecodedField>();
        var start = bitOffset + countWidth;
        if (data.Length == 0)
        {
            return fields;
        }

        if (data.Length == 1)
        {
            fields.Add(Field("prize_slot_count", data[0], start, 8, MbcValueKind.UInt));
            fields.Add(new MbcDecodedField
            {
                Kind = "text",
                Name = "action",
                StringValue = data[0] == 0 ? "smsShowPrizeWindow(empty)" : "smsShowPrizeWindow",
                BitOffset = start,
                BitLength = 0,
                Display = data[0] == 0
                    ? "action=smsShowPrizeWindow(empty)"
                    : "action=smsShowPrizeWindow"
            });
            return fields;
        }

        if (data.Length == 2)
        {
            fields.Add(new MbcDecodedField
            {
                Kind = "text",
                Name = "action",
                StringValue = "smsInfoIcon",
                BitOffset = start,
                BitLength = 0,
                Display = "action=smsInfoIcon (payload ignored)"
            });
            fields.Add(Field("ignored_0", data[0], start, 8, MbcValueKind.UInt));
            fields.Add(Field("ignored_1", data[1], start + 8, 8, MbcValueKind.UInt));
            return fields;
        }

        var slotCount = data[0];
        fields.Add(Field("prize_slot_count", slotCount, start, 8, MbcValueKind.UInt));
        var pairs = Math.Min(slotCount, (data.Length - 1) / 8);
        for (var i = 0; i < pairs; i++)
        {
            var off = 1 + i * 8;
            fields.Add(Field($"item_process[{i}]", ReadLe(data, off, 4), start + off * 8, 32,
                MbcValueKind.ObjectId));
            fields.Add(Field($"amount[{i}]", ReadLe(data, off + 4, 4), start + (off + 4) * 8, 32,
                MbcValueKind.UInt));
        }

        var consumed = 1 + pairs * 8;
        if (consumed < data.Length)
        {
            var rest = data[consumed..];
            fields.Add(new MbcDecodedField
            {
                Kind = "bytes",
                Name = "rest",
                ArrayValue = rest.Select(b => (long)b).ToArray(),
                BitOffset = start + consumed * 8,
                BitLength = rest.Length * 8,
                Display = $"rest={Convert.ToHexString(rest)}"
            });
        }

        fields.Add(new MbcDecodedField
        {
            Kind = "text",
            Name = "action",
            StringValue = "smsShowPrizeWindow",
            BitOffset = start,
            BitLength = 0,
            Display = "action=smsShowPrizeWindow"
        });
        return fields;
    }

    private static List<MbcDecodedField> UnpackTmntInfo(byte[] data, int bitOffset, int countWidth)
    {
        var fields = new List<MbcDecodedField>();
        if (data.Length < 2)
        {
            if (data.Length == 1)
            {
                fields.Add(Field("has_stats", data[0], bitOffset + countWidth, 8, MbcValueKind.UInt));
            }

            return fields;
        }

        var start = bitOffset + countWidth;
        fields.Add(Field("has_stats", data[0], start, 8, MbcValueKind.UInt));
        fields.Add(Field("is_results", data[1], start + 8, 8, MbcValueKind.UInt));
        if (data.Length > 2)
        {
            var name = MbcPayloadDecoder.DecodeText(data.AsSpan(2));
            fields.Add(new MbcDecodedField
            {
                Kind = "text",
                Name = "name",
                StringValue = name,
                BitOffset = start + 16,
                BitLength = (data.Length - 2) * 8,
                Display = $"name={name}"
            });
        }

        return fields;
    }

    private static List<MbcDecodedField> UnpackTmntAction(byte[] data, int bitOffset, int countWidth)
    {
        var fields = new List<MbcDecodedField>();
        if (data.Length == 0)
        {
            return fields;
        }

        var start = bitOffset + countWidth;
        var nul = 0;
        while (nul < data.Length && data[nul] != 0)
        {
            nul++;
        }

        var nameBytes = nul < data.Length ? nul + 1 : data.Length;
        var name = MbcPayloadDecoder.DecodeText(data.AsSpan(0, Math.Min(nul, data.Length)));
        fields.Add(new MbcDecodedField
        {
            Kind = "text",
            Name = "name",
            StringValue = name,
            BitOffset = start,
            BitLength = nameBytes * 8,
            Display = $"name={name}"
        });

        if (nul + 1 < data.Length)
        {
            fields.Add(Field("action", data[nul + 1], start + (nul + 1) * 8, 8, MbcValueKind.UInt));
        }

        return fields;
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
        long? raw = null;
        long intValue = value;
        double? doubleValue = null;
        var fieldKind = "int";
        if (kind == MbcValueKind.SignedMinus30000)
        {
            raw = value;
            intValue = value - 30000;
            display = intValue.ToString();
            fieldKind = "signed_m30000";
        }
        else if (kind == MbcValueKind.ObjectId)
        {
            raw = value;
            fieldKind = "object_id";
        }
        else if (kind == MbcValueKind.Float32)
        {
            raw = value;
            var bits = unchecked((int)(uint)value);
            doubleValue = BitConverter.Int32BitsToSingle(bits);
            display = $"{doubleValue:0.###}";
            fieldKind = "float";
        }

        return new MbcDecodedField
        {
            Kind = fieldKind,
            Name = name,
            IntValue = intValue,
            DoubleValue = doubleValue,
            RawWireValue = raw,
            BitOffset = bitOffset,
            BitLength = bitLength,
            Display = $"{name}={display}"
        };
    }
}
