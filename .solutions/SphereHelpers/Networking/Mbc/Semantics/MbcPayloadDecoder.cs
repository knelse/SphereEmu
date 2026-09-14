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

    public static void Apply(MbcDecodedEvent decoded, MbcRecoveredCatalog? recovered)
    {
        var rec = recovered?.Find(decoded.ModuleTag, decoded.Region, decoded.Command);
        if (rec is not null && !string.IsNullOrEmpty(rec.Name))
        {
            decoded.RecoveredName = rec.Name;
            if (rec.Source is "override" or "shared-item" or "decompile" or "alias")
            {
                decoded.Confidence = "high";
            }
        }

        if (string.IsNullOrEmpty(decoded.RecoveredName))
        {
            decoded.RecoveredName = LeafName(decoded.EventName);
        }

        var layout = MbcPayloadLayouts.Find(decoded.Module, decoded.Handler, decoded.Region, decoded.Command);
        if (decoded.Command is not null && TryGetBytePayload(decoded, out var arrayField, out var payload, out var countWidth))
        {
            var unpacked = UnpackPayload(payload, arrayField.BitOffset, countWidth, layout, rec);
            if (unpacked.Count > 0)
            {
                ReplacePayloadFields(decoded, arrayField, unpacked);
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
        PromoteArrayStrings(decoded);
        decoded.Summary = BuildSummary(decoded);
        decoded.EventName = FormatEventName(decoded);
    }

    public static string FormatEventName(MbcDecodedEvent decoded)
    {
        var name = string.IsNullOrEmpty(decoded.RecoveredName)
            ? decoded.EventName
            : $"{decoded.Module}.{decoded.RecoveredName}";
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
            var value = field.Kind == MbcValueKind.SignedMinus30000 ? raw - 30000 : raw;
            var shown = field.Kind == MbcValueKind.ObjectId ? $"0x{raw:X}" : value.ToString();
            result.Add(new MbcDecodedField
            {
                Kind = field.Kind == MbcValueKind.ObjectId ? "object_id" : "int",
                Name = field.Name,
                IntValue = value,
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
        List<MbcDecodedField> unpacked)
    {
        var index = decoded.Fields.IndexOf(arrayField);
        if (index < 0)
        {
            decoded.Fields.AddRange(unpacked);
            return;
        }

        decoded.Fields.RemoveAt(index);
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
            return $"{name}={num}";
        }

        return string.IsNullOrEmpty(field.Display) ? name : $"{name}={field.Display}";
    }

    private static string BuildSummary(MbcDecodedEvent decoded)
    {
        var bits = new List<string>();
        foreach (var field in decoded.Fields)
        {
            if (field.Name is "" or "command" or "rest")
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
}
