using System.Text;

namespace SphServer.Helpers.Networking;

/// <summary>
///     Applies recovered command names and unpacks inner payload bytes / schema fields.
///     Layouts: MbcPayloadLayouts (hand-tuned). Names: MbcRecoveredCatalog (generated).
/// </summary>
public static class MbcPayloadDecoder
{
    private static readonly Encoding TextEnc;

    static MbcPayloadDecoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            TextEnc = Encoding.GetEncoding(1251);
        }
        catch (Exception)
        {
            TextEnc = Encoding.Latin1;
        }
    }

    public static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.Length;
        while (end > 0 && bytes[end - 1] == 0)
        {
            end--;
        }

        if (end <= 0)
        {
            return "";
        }

        var text = TextEnc.GetString(bytes[..end]);
        return text.Replace('\0', ' ').Trim();
    }

    private static readonly HashSet<string> MbcRoutineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ContMan", "TradeMan", "Manager", "NManager", "NAManager", "Owner",
        "CheckPing", "Image", "RcvInfo", "RegionManager", "User", "EInit",
        "CycleSend", "Resstop", "RegMReceiver", "RegTmntReceiver",
        "receiveArtisanMonitor", "receiveOwnerArtisanMonitor", "receiveRegExpTime"
    };

    public static void Apply(MbcDecodedEvent decoded, MbcRecoveredCatalog? recovered)
    {
        var rec = recovered?.Find(decoded.ModuleTag, decoded.Region, decoded.Command, decoded.Handler);
        if (rec is not null && !string.IsNullOrEmpty(rec.Name) && !IsMbcRoutineName(rec.Name))
        {
            decoded.RecoveredName = rec.Name;
            if (rec.Source is "override" or "shared-item" or "decompile" or "alias")
            {
                decoded.Confidence = "high";
            }
        }

        if (string.IsNullOrEmpty(decoded.RecoveredName))
        {
            var leaf = LeafName(decoded.EventName);
            if (!IsMbcRoutineName(leaf) && !IsGenericRegionLeaf(leaf))
            {
                decoded.RecoveredName = leaf;
            }
        }

        if (IsMbcRoutineName(decoded.RecoveredName) || IsGenericRegionLeaf(decoded.RecoveredName))
        {
            decoded.RecoveredName = "";
        }

        if (decoded.Region == 61 &&
            (decoded.RecoveredName is "SpawnSnapshot" or "" || IsGenericRegionLeaf(decoded.RecoveredName)))
        {
            decoded.RecoveredName = "SpawnData";
        }

        var layout = MbcPayloadLayouts.Find(decoded.Module, decoded.Handler, decoded.Region, decoded.Command);
        if (decoded.Command is not null && TryGetBytePayload(decoded, out var arrayField, out var payload, out var countWidth))
        {
            var unpacked = UnpackPayload(payload, arrayField.BitOffset, countWidth, layout, rec);
            if (unpacked.Count > 0)
            {
                ReplacePayloadFields(decoded, arrayField, unpacked, countWidth);
            }
        }
        else if (layout?.SchemaFieldNames is { Count: > 0 } names)
        {
            ApplySchemaNames(decoded, names);
        }
        else if (rec?.Unpack is { Count: > 0 } hints && hints.All(h => h.Bytes == 0))
        {
            ApplySchemaNames(decoded, hints.Select(h => h.Name).ToList());
        }

        ExpandTitleDegree(decoded);
        ApplyPlayerStatFieldNames(decoded);
        FixupItemContManCmd9Name(decoded);
        ExpandPositionStream(decoded);
        ReinterpretSchemaFloats(decoded);
        FormatItemSpawnSnapshotFields(decoded);
        PromoteArrayStrings(decoded);
        if (string.IsNullOrEmpty(decoded.RecoveredName))
        {
            decoded.RecoveredName = FallbackInferredName(decoded);
        }

        decoded.Summary = BuildSummary(decoded);
        decoded.EventName = FormatEventName(decoded);
    }

    /// <summary>
    ///     guild/st_map ContMan cmd9: len6 = name query; len5 = blk_type+blk_id (getBLkType/getBLkID).
    /// </summary>
    private static void FixupItemContManCmd9Name(MbcDecodedEvent decoded)
    {
        if (decoded.Command != 9 || decoded.Region != 4 ||
            decoded.Module is not ("guild" or "st_map"))
        {
            return;
        }

        var len = decoded.Fields.FirstOrDefault(f => f.Name == "payload_len_bytes")?.IntValue;
        if (len == 6)
        {
            decoded.RecoveredName = "QueryObjectName";
            return;
        }

        if (len == 5)
        {
            decoded.RecoveredName = "SyncBinding";
            FormatBindingFields(decoded);
            return;
        }

        decoded.RecoveredName = "SyncBinding";
        FormatBindingFields(decoded);
    }

    private static void FormatBindingFields(MbcDecodedEvent decoded)
    {
        foreach (var field in decoded.Fields)
        {
            if (field.Name == "blk_id" && field.IntValue is { } id)
            {
                field.Kind = "object_id";
                field.Display = id is 0 or unchecked((long)uint.MaxValue)
                    ? "blk_id=unbound"
                    : $"blk_id=0x{id:X}";
            }
            else if (field.Name == "blk_type" && field.IntValue is { } type)
            {
                field.Display = $"blk_type={type}";
            }
        }
    }

    private static void FormatItemSpawnSnapshotFields(MbcDecodedEvent decoded)
    {
        if (decoded.Module is not ("guild" or "st_map"))
        {
            return;
        }

        foreach (var field in decoded.Fields)
        {
            if (field.Name == "suffix_id" && field.IntValue is { } suffix)
            {
                field.Display = suffix < 0 ? "suffix_id=none" : $"suffix_id={suffix}";
            }
            else if (field.Name == "contain_state" && field.IntValue is { } state)
            {
                field.Display = state switch
                {
                    1 => "contain_state=1 (free)",
                    2 => "contain_state=2 (in_container)",
                    _ => $"contain_state={state}"
                };
            }
            else if (field.Name == "game_id" && field.IntValue is { } gameId)
            {
                var locName = TryGameObjectName((int)gameId);
                field.Display = string.IsNullOrEmpty(locName)
                    ? $"game_id={gameId}"
                    : $"game_id={gameId} {locName}";
            }
            else if ((field.Name is "container_id" or "local") && field.IntValue is { } cid)
            {
                field.Name = "container_id";
                field.Kind = "object_id";
                field.Display = $"container_id={cid:X4}";
            }
        }
    }

    private static bool IsMbcRoutineName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var leaf = name.Contains('.') ? name.Split('.').Last()! : name;
        if (leaf.StartsWith("cmd", StringComparison.OrdinalIgnoreCase) &&
            leaf.Length > 3 && leaf[3..].All(char.IsDigit))
        {
            return false;
        }

        return MbcRoutineNames.Contains(leaf);
    }

    private static bool IsGenericRegionLeaf(string? name) =>
        !string.IsNullOrEmpty(name) &&
        name.StartsWith("region", StringComparison.OrdinalIgnoreCase) &&
        name.Length > 6 && name[6..].All(char.IsDigit);

    /// <summary>
    ///     Last resort when jsonl/recovered has no inferred n: module.cmdN or module.rN, never the handler routine.
    /// </summary>
    private static string FallbackInferredName(MbcDecodedEvent decoded)
    {
        if (decoded.Command is { } cmd)
        {
            return $"cmd{cmd}";
        }

        return $"r{decoded.Region}";
    }

    /// <summary>
    ///     _player region 10: u6 index + varint value written to g_rec_0C44[index].
    ///     Received by the CheckPing coroutine; display name is the wire meaning (SetStat).
    /// </summary>
    private static void ApplyPlayerStatFieldNames(MbcDecodedEvent decoded)
    {
        if (decoded.ModuleTag != 2 || decoded.Region != 10 || decoded.Fields.Count < 2)
        {
            return;
        }

        if (decoded.Fields[0].IntValue is not { } index)
        {
            return;
        }

        var statName = MbcStatFields.Name((int)index);
        decoded.Fields[0].Name = "command";
        decoded.Fields[0].Display = $"command={index} ({statName})";
        decoded.Fields[1].Name = statName;
        decoded.Fields[1].Display = FormatNamed(decoded.Fields[1], statName);
        decoded.RecoveredName = "SetStat";
    }

    public static string FormatEventName(MbcDecodedEvent decoded)
    {
        var leaf = string.IsNullOrEmpty(decoded.RecoveredName)
            ? LeafName(decoded.EventName)
            : decoded.RecoveredName;
        if (IsMbcRoutineName(leaf) || IsGenericRegionLeaf(leaf))
        {
            leaf = FallbackInferredName(decoded);
        }

        var name = $"{decoded.Module}.{leaf}";
        return string.IsNullOrEmpty(decoded.Summary) ? name : $"{name} {decoded.Summary}";
    }

    private static string LeafName(string eventName)
    {
        var leaf = eventName.Split('.').LastOrDefault() ?? eventName;
        return leaf.StartsWith("cmd", StringComparison.Ordinal) ? leaf : leaf;
    }

    private static bool TryGetBytePayload(MbcDecodedEvent decoded, out MbcDecodedField arrayField, out byte[] data,
        out int countWidth)
    {
        arrayField = null!;
        data = [];
        countWidth = 8;
        foreach (var field in decoded.Fields)
        {
            if (field.ArrayValue is not { Length: > 0 } values)
            {
                continue;
            }

            if (values.Any(v => v is < 0 or > 255))
            {
                continue;
            }

            arrayField = field;
            data = values.Select(v => (byte)(v & 0xFF)).ToArray();
            var payloadBits = data.Length * 8;
            countWidth = Math.Max(0, field.BitLength - payloadBits);
            if (countWidth is not (4 or 8))
            {
                countWidth = field.BitLength >= payloadBits + 8 ? 8 : 4;
            }

            return true;
        }

        return false;
    }

    private static List<MbcDecodedField> UnpackPayload(byte[] data, int arrayBitOffset, int countWidth,
        MbcCommandLayout? layout, MbcRecoveredCommand? rec)
    {
        if (data.Length == 0)
        {
            return [];
        }

        if (layout?.Custom is not null)
        {
            return layout.Custom(data, arrayBitOffset, countWidth);
        }

        IReadOnlyList<MbcLayoutField>? fields = null;
        if (layout?.ByLength is not null && layout.ByLength.TryGetValue(data.Length, out var byLen))
        {
            fields = byLen;
        }

        fields ??= layout?.Fields;
        if (fields is null && rec?.Unpack is { Count: > 0 } hints && hints.Any(h => h.Bytes > 0))
        {
            fields = hints.Where(h => h.Bytes > 0)
                .Select(h => new MbcLayoutField
                {
                    Name = h.Name,
                    Bytes = h.Bytes,
                    Kind = h.Bytes == 3 ? MbcValueKind.ObjectId : MbcValueKind.UInt
                })
                .ToList();
        }

        if (fields is null || fields.Count == 0)
        {
            return FallbackBytes(data, arrayBitOffset, countWidth);
        }

        return ReadFields(data, arrayBitOffset, countWidth, fields);
    }

    private static List<MbcDecodedField> ReadFields(byte[] data, int arrayBitOffset, int countWidth,
        IReadOnlyList<MbcLayoutField> fields)
    {
        var result = new List<MbcDecodedField>();
        var offset = 0;
        var start = arrayBitOffset + countWidth;
        foreach (var field in fields)
        {
            if (offset >= data.Length)
            {
                break;
            }

            if (field.Kind == MbcValueKind.Text || field.Bytes == 0)
            {
                var slice = data.AsSpan(offset);
                var text = DecodeText(slice);
                result.Add(new MbcDecodedField
                {
                    Kind = "text",
                    Name = field.Name,
                    StringValue = text,
                    BitOffset = start + offset * 8,
                    BitLength = slice.Length * 8,
                    Display = $"{field.Name}={text}"
                });
                offset = data.Length;
                continue;
            }

            var width = Math.Min(field.Bytes, data.Length - offset);
            var raw = MbcPayloadLayouts.ReadLe(data, offset, width);
            if (field.Kind == MbcValueKind.Float32)
            {
                result.Add(MbcPayloadLayouts.Field(field.Name, raw, start + offset * 8, width * 8,
                    MbcValueKind.Float32));
                offset += width;
                continue;
            }

            var signedHp = field.Kind == MbcValueKind.SignedMinus30000;
            var value = signedHp ? raw - 30000 : raw;
            var shown = field.Kind == MbcValueKind.ObjectId ? $"0x{raw:X}" : value.ToString();
            result.Add(new MbcDecodedField
            {
                Kind = field.Kind == MbcValueKind.ObjectId
                    ? "object_id"
                    : signedHp
                        ? "signed_m30000"
                        : "int",
                Name = field.Name,
                IntValue = value,
                RawWireValue = signedHp || field.Kind == MbcValueKind.ObjectId ? raw : null,
                BitOffset = start + offset * 8,
                BitLength = width * 8,
                Display = $"{field.Name}={shown}"
            });
            offset += width;
        }

        if (offset < data.Length)
        {
            var rest = data[offset..];
            result.Add(new MbcDecodedField
            {
                Kind = "bytes",
                Name = "rest",
                ArrayValue = rest.Select(b => (long)b).ToArray(),
                StringValue = DecodeText(rest),
                BitOffset = start + offset * 8,
                BitLength = rest.Length * 8,
                Display = rest.Length <= 12
                    ? $"rest={Convert.ToHexString(rest)}"
                    : $"rest[{rest.Length}]={Convert.ToHexString(rest[..12])}..."
            });
        }

        return result;
    }

    private static List<MbcDecodedField> FallbackBytes(byte[] data, int arrayBitOffset, int countWidth)
    {
        var text = DecodeText(data);
        var printable = text.Length >= 3 && text.Count(char.IsLetterOrDigit) >= text.Length / 2;
        var display = printable ? text : Convert.ToHexString(data);
        return
        [
            new MbcDecodedField
            {
                Kind = printable ? "text" : "bytes",
                Name = printable ? "text" : "payload",
                ArrayValue = data.Select(b => (long)b).ToArray(),
                StringValue = printable ? text : null,
                IntValue = data.Length <= 4 ? MbcPayloadLayouts.ReadLe(data, 0, data.Length) : null,
                BitOffset = arrayBitOffset + countWidth,
                BitLength = data.Length * 8,
                Display = printable ? $"text={text}" : $"payload={display}"
            }
        ];
    }

    private static void ReplacePayloadFields(MbcDecodedEvent decoded, MbcDecodedField arrayField,
        List<MbcDecodedField> unpacked, int countWidth)
    {
        var index = decoded.Fields.IndexOf(arrayField);
        if (index < 0)
        {
            decoded.Fields.AddRange(unpacked);
            return;
        }

        decoded.Fields.RemoveAt(index);
        // ContMan/TradeMan: uN command, then arrayN count bits, then payload bytes.
        // Unpacked fields start after the count; keep the count as payload_len_bytes so it is not a blank.
        if (countWidth > 0 && arrayField.ArrayValue is { } values)
        {
            decoded.Fields.Insert(index, new MbcDecodedField
            {
                Kind = "int",
                Name = "payload_len_bytes",
                IntValue = values.Length,
                BitOffset = arrayField.BitOffset,
                BitLength = countWidth,
                Display = $"payload_len_bytes={values.Length}"
            });
            index++;
        }

        decoded.Fields.InsertRange(index, unpacked);
        if (decoded.Fields.Count > 0 && decoded.Fields[0].Kind == "int" && string.IsNullOrEmpty(decoded.Fields[0].Name))
        {
            decoded.Fields[0].Name = "command";
            var cmd = decoded.Fields[0].IntValue ?? decoded.Command ?? 0;
            decoded.Fields[0].Display = $"command={cmd}";
        }
    }

    private static void ApplySchemaNames(MbcDecodedEvent decoded, IReadOnlyList<string> names)
    {
        var count = Math.Min(decoded.Fields.Count, names.Count);
        for (var i = 0; i < count; i++)
        {
            var field = decoded.Fields[i];
            var name = names[i];
            if (string.IsNullOrEmpty(name) || name is "local" or "u32")
            {
                continue;
            }

            field.Name = name;
            field.Display = FormatNamed(field, name);
        }
    }

    private static void PromoteArrayStrings(MbcDecodedEvent decoded)
    {
        foreach (var field in decoded.Fields)
        {
            if (field.ArrayValue is not { Length: > 0 } values)
            {
                continue;
            }

            if (values.Any(v => v is < 0 or > 255))
            {
                if (string.IsNullOrEmpty(field.Name))
                {
                    continue;
                }

                field.Display = $"{field.Name}[{values.Length}]={string.Join(",", values.Take(8))}"
                                + (values.Length > 8 ? "..." : "");
                continue;
            }

            var bytes = values.Select(v => (byte)v).ToArray();
            var text = DecodeText(bytes);
            var printable = text.Length >= 1 && text.Any(char.IsLetterOrDigit);
            if (printable)
            {
                field.StringValue = text;
                var name = string.IsNullOrEmpty(field.Name) ? "text" : field.Name;
                field.Name = name;
                field.Display = $"{name}={text}";
            }
            else if (!string.IsNullOrEmpty(field.Name))
            {
                field.Display = FormatNamed(field, field.Name);
            }
        }
    }

    private static void ExpandTitleDegree(MbcDecodedEvent decoded)
    {
        var packed = decoded.Fields.FirstOrDefault(f => f.Name is "title_degree" or "stat.i37");
        if (packed?.IntValue is not { } value)
        {
            return;
        }

        decoded.Fields.Add(new MbcDecodedField
        {
            Kind = "int",
            Name = "title_level",
            IntValue = value % 100,
            BitOffset = packed.BitOffset,
            BitLength = packed.BitLength,
            Display = $"title_level={value % 100}"
        });
        decoded.Fields.Add(new MbcDecodedField
        {
            Kind = "int",
            Name = "degree_level",
            IntValue = value / 100,
            BitOffset = packed.BitOffset,
            BitLength = packed.BitLength,
            Display = $"degree_level={value / 100}"
        });
    }

    /// <summary>
    ///     _player region 11 PositionStream: array4&lt;u32&gt; = count nibble + N IEEE float words.
    ///     CycleSend always sends 4: g_rec_0004 pos_x/pos_y/pos_z/yaw (absolute, not encodeCoordinate).
    /// </summary>
    private static void ExpandPositionStream(MbcDecodedEvent decoded)
    {
        if (decoded.Region != 11)
        {
            return;
        }

        var arrayIndex = decoded.Fields.FindIndex(f => f.Kind == "array" && f.ArrayValue is { Length: > 0 });
        if (arrayIndex < 0)
        {
            return;
        }

        var array = decoded.Fields[arrayIndex];
        var words = array.ArrayValue!;
        // desc 0x65 = array4 count width.
        const int countWidth = 4;
        var names = new[] { "x", "y", "z", "angle" };
        var expanded = new List<MbcDecodedField>(1 + words.Length)
        {
            new()
            {
                Descriptor = 0x65,
                Kind = "int",
                Name = "word_count",
                IntValue = words.Length,
                BitOffset = array.BitOffset,
                BitLength = countWidth,
                Display = $"word_count={words.Length}"
            }
        };

        for (var i = 0; i < words.Length; i++)
        {
            var raw = unchecked((uint)words[i]);
            var value = BitConverter.Int32BitsToSingle(unchecked((int)raw));
            var name = i < names.Length ? names[i] : $"word{i}";
            expanded.Add(new MbcDecodedField
            {
                Descriptor = 32,
                Kind = "float",
                Name = name,
                IntValue = words[i],
                RawWireValue = words[i],
                DoubleValue = value,
                BitOffset = array.BitOffset + countWidth + i * 32,
                BitLength = 32,
                Display = $"{name}={value:0.###}"
            });
        }

        decoded.Fields.RemoveAt(arrayIndex);
        decoded.Fields.InsertRange(arrayIndex, expanded);
        if (string.IsNullOrEmpty(decoded.RecoveredName) ||
            decoded.RecoveredName.StartsWith("r", StringComparison.Ordinal) ||
            decoded.RecoveredName.StartsWith("cmd", StringComparison.OrdinalIgnoreCase))
        {
            decoded.RecoveredName = "PositionStream";
        }
    }

    /// <summary>
    ///     Region schemas that declare world coords as u32 are IEEE float bit patterns
    ///     (TransformState / WorldSnapshot / SpawnData / PositionStream), not integer world units.
    /// </summary>
    private static void ReinterpretSchemaFloats(MbcDecodedEvent decoded)
    {
        if (decoded.Region is not (2 or 7 or 11 or 61))
        {
            return;
        }

        foreach (var field in decoded.Fields)
        {
            if (field.Name is not ("x" or "y" or "z" or "yaw" or "angle"))
            {
                continue;
            }

            if (field.Kind == "float" || field.DoubleValue is not null)
            {
                continue;
            }

            if (field.IntValue is not { } bits || field.BitLength != 32)
            {
                continue;
            }

            var value = BitConverter.Int32BitsToSingle(unchecked((int)(uint)bits));
            field.Kind = "float";
            field.DoubleValue = value;
            field.RawWireValue = bits;
            field.Display = $"{field.Name}={value:0.###}";
        }
    }

    private static string FormatNamed(MbcDecodedField field, string name)
    {
        if (field.StringValue is { Length: > 0 } text)
        {
            return $"{name}={text}";
        }

        if (field.ArrayValue is { Length: > 0 } arr)
        {
            if (arr.Length > 8)
            {
                return $"{name}[{arr.Length}]";
            }

            return $"{name}={string.Join(",", arr)}";
        }

        if (field.DoubleValue is { } dbl)
        {
            return $"{name}={dbl:0.###}";
        }

        if (field.IntValue is { } num)
        {
            if (field.Kind == "object_id")
            {
                return $"{name}={num:X4}";
            }

            return $"{name}={num}";
        }

        return string.IsNullOrEmpty(field.Display) ? name : $"{name}={field.Display}";
    }

    private static string BuildSummary(MbcDecodedEvent decoded)
    {
        if (TryBuildEntityPositionSummary(decoded, out var positionSummary))
        {
            return positionSummary;
        }

        var bits = new List<string>();
        foreach (var field in decoded.Fields)
        {
            if (field.Name is "" or "command" or "rest" or "payload_len_bytes" or "word_count")
            {
                continue;
            }

            if (field.ArrayValue is { Length: > 8 } && field.StringValue is null)
            {
                bits.Add($"{field.Name}[{field.ArrayValue.Length}]");
                continue;
            }

            var text = field.Display;
            if (string.IsNullOrEmpty(text) || text == field.Kind)
            {
                continue;
            }

            if (!text.Contains('=') && !string.IsNullOrEmpty(field.Name))
            {
                text = FormatNamed(field, field.Name);
            }

            bits.Add(text);
            if (bits.Count >= 12)
            {
                break;
            }
        }

        var summary = string.Join(" ", bits);
        if (summary.Length > 240)
        {
            summary = summary[..237] + "...";
        }

        return summary;
    }

    /// <summary>
    ///     Banner form: process_id newX newY newZ newAngle.
    ///     Region 1 TransformUpdate (rel deltas as world) and region 11 PositionStream (absolute floats).
    /// </summary>
    private static bool TryBuildEntityPositionSummary(MbcDecodedEvent decoded, out string summary)
    {
        summary = "";
        if (decoded.Region is not (1 or 11))
        {
            return false;
        }

        double? x = null, y = null, z = null, angle = null;
        foreach (var field in decoded.Fields)
        {
            switch (field.Name)
            {
                case "x" when field.DoubleValue is { } vx:
                    x = vx;
                    break;
                case "y" when field.DoubleValue is { } vy:
                    y = vy;
                    break;
                case "z" when field.DoubleValue is { } vz:
                    z = vz;
                    break;
                case "angle" when field.DoubleValue is { } af:
                    angle = af;
                    break;
                case "angle" when field.IntValue is { } a && field.DoubleValue is null:
                    angle = a;
                    break;
            }
        }

        if (x is null || y is null || z is null || angle is null)
        {
            return false;
        }

        summary = decoded.Region == 11
            ? $"{decoded.ProcessId:X4} {x.Value:0.###} {y.Value:0.###} {z.Value:0.###} {angle.Value:0.###}"
            : $"{decoded.ProcessId:X4} {x.Value:0.###} {y.Value:0.###} {z.Value:0.###} {angle.Value}";
        return true;
    }

    private static string? TryGameObjectName(int gameId)
    {
        try
        {
            if (!SphObjectDb.GameObjectDataDb.TryGetValue(gameId, out var go) || go.Localisation.Count == 0)
            {
                return null;
            }

            if (go.Localisation.TryGetValue(Locale.Russian, out var ru) && !string.IsNullOrEmpty(ru))
            {
                return ru;
            }

            if (go.Localisation.TryGetValue(Locale.English, out var en) && !string.IsNullOrEmpty(en))
            {
                return en;
            }

            return go.Localisation.Values.FirstOrDefault(x => !string.IsNullOrEmpty(x));
        }
        catch
        {
            return null;
        }
    }
}
