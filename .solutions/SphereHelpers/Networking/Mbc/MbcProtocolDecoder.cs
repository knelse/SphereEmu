namespace SphServer.Helpers.Networking;

internal sealed class MbcBitReader
{
    private readonly byte[] data;
    public int Pos { get; private set; }
    public int Limit { get; }

    public MbcBitReader(byte[] data)
    {
        this.data = data;
        Limit = data.Length * 8;
    }

    public int Left => Limit - Pos;

    public int Read(int width)
    {
        if (width < 0 || Pos + width > Limit)
        {
            throw new MbcDecodeException($"need {width} bits at {Pos}, left {Left}");
        }

        var value = 0;
        for (var i = 0; i < width; i++)
        {
            var bit = Pos + i;
            value |= ((data[bit >> 3] >> (bit & 7)) & 1) << i;
        }

        Pos += width;
        return value;
    }

    public int Signed(int width)
    {
        var value = Read(width);
        var sign = 1 << (width - 1);
        return (value & sign) != 0 ? value - (1 << width) : value;
    }
}

internal sealed class MbcDecodeException : Exception
{
    public MbcDecodeException(string message) : base(message)
    {
    }
}

public sealed class MbcProtocolDecoder
{
    private static readonly ushort[] KnownMessages = [100, 200, 300, 400, 500, 600, 700];
    private static readonly int[] VarintWidths = [3, 7, 14, 31];

    private readonly MbcCatalog catalog;
    private readonly MbcRecoveredCatalog recovered;
    private readonly Dictionary<int, int> processModules = new();

    public MbcProtocolDecoder(MbcCatalog catalog, MbcRecoveredCatalog? recovered = null)
    {
        this.catalog = catalog;
        this.recovered = recovered ?? new MbcRecoveredCatalog();
    }

    public static MbcProtocolDecoder CreateDefault() =>
        new(MbcCatalog.LoadEmbedded(), MbcRecoveredCatalog.LoadEmbedded());

    public void ResetProcessBindings() => processModules.Clear();

    public string ModuleName(int? tag) => catalog.ModuleName(tag);

    public static List<MbcTcpFrame> SplitTcpFrames(byte[] content, MbcDirection direction)
    {
        var headerSize = direction == MbcDirection.Client ? 8 : 4;
        var offset = 0;
        var frames = new List<MbcTcpFrame>();
        while (offset + headerSize <= content.Length)
        {
            var size = content[offset] | (content[offset + 1] << 8);
            var messageOffset = offset + (direction == MbcDirection.Client ? 6 : 2);
            var message = (ushort)(content[messageOffset] | (content[messageOffset + 1] << 8));
            if (size < headerSize || size > content.Length - offset || Array.IndexOf(KnownMessages, message) < 0)
            {
                break;
            }

            var raw = content.AsSpan(offset, size);
            var payload = direction == MbcDirection.Client ? raw[8..].ToArray() : raw[4..].ToArray();
            frames.Add(new MbcTcpFrame(offset, size, message, payload));
            offset += size;
        }

        return frames;
    }

    public MbcDecodeResult DecodeGame(byte[] payload, MbcDirection direction)
    {
        var result = new MbcDecodeResult
        {
            Status = "fail",
            Direction = direction
        };
        MbcBitReader? bits = null;
        var originalBits = 0;
        try
        {
            if (payload.Length == 0)
            {
                throw new MbcDecodeException("empty msg300 payload");
            }

            result.Control = payload[0];
            var body = payload.Length == 1 ? [] : payload[1..];
            originalBits = body.Length * 8;
            var padded = new byte[body.Length + 1];
            body.CopyTo(padded, 0);
            bits = new MbcBitReader(padded);
            var hasPosition = bits.Read(1) == 1;
            int[]? basePos = null;
            if (hasPosition)
            {
                basePos = [bits.Read(16) - 0x8000, bits.Read(13) - 0x4B0, bits.Read(16) - 0x8000];
            }

            var tick = bits.Read(15);
            var processId = bits.Read(18);
            var wireModuleTag = bits.Read(12);
            var moduleTag = EnterContext(processId, wireModuleTag, result, out var contextState);
            result.HasPosition = hasPosition;
            result.BasePosition = basePos;
            result.Tick = tick;
            result.ProcessId = processId;
            result.ModuleTag = wireModuleTag;
            result.EffectiveModuleTag = moduleTag;
            result.Module = catalog.ModuleName(moduleTag);
            result.WireBitLength = originalBits;
            if (moduleTag is null)
            {
                result.Status = contextState;
                return Finish(result, bits, originalBits);
            }

            while (true)
            {
                var wireStart = bits.Pos;
                var wire = bits.Read(7);
                if (wire == 0)
                {
                    result.Status = "full";
                    result.TerminatorBit = wireStart;
                    break;
                }

                if (wire == 0x3F)
                {
                    processId = bits.Read(18);
                    wireModuleTag = bits.Read(12);
                    moduleTag = EnterContext(processId, wireModuleTag, result, out contextState);
                    result.ContextSwitches++;
                    if (moduleTag is null)
                    {
                        result.Status = contextState;
                        break;
                    }

                    continue;
                }

                if (wire > 0x3F)
                {
                    result.Status = "client_stop_invalid_wire";
                    result.StopWire = wire;
                    result.StopBit = wireStart;
                    break;
                }

                var region = wire - 1;
                if (region is 0 or 61 && wireModuleTag != 0)
                {
                    var recreated = RecreateForRegion(processId, wireModuleTag);
                    if (recreated is null)
                    {
                        result.Status = "ignored_unknown_module";
                        break;
                    }

                    moduleTag = recreated;
                }

                if (!catalog.Modules.TryGetValue(moduleTag.Value, out var module))
                {
                    result.Status = "ignored_unknown_module";
                    break;
                }

                if (!module.Regions.TryGetValue(region, out var spec))
                {
                    result.Status = "client_stop_undeclared_region";
                    result.StopWire = wire;
                    result.StopRegion = region;
                    result.StopBit = wireStart;
                    break;
                }

                var start = bits.Pos;
                var desc = spec.Desc.ToArray();
                string? wireOverride = null;
                if (direction == MbcDirection.Client && spec.Handler == "ContMan" && desc.Length > 0 && desc[0] == 4)
                {
                    desc[0] = 8;
                    wireOverride = "ContMan command is u8 on client wire (MBC descriptor declares u4)";
                }

                var fields = DecodeFields(bits, desc, basePos);
                var decoded = new MbcDecodedEvent
                {
                    ProcessId = processId,
                    ModuleTag = moduleTag.Value,
                    Module = module.Name,
                    WireRegion = wire,
                    Region = region,
                    Handler = spec.Handler,
                    Flags = spec.Flags,
                    Schema = spec.Schema,
                    StartBit = start - 7,
                    EndBit = bits.Pos,
                    WireSchemaOverride = wireOverride
                };
                decoded.Fields.AddRange(fields);

                int? command = null;
                if (fields.Count > 0 && fields[0].IntValue is { } candidate)
                {
                    var side = direction == MbcDirection.Client ? "C2S" : "S2C";
                    if (catalog.CommandRegions.Contains((side, moduleTag.Value, region)))
                    {
                        command = (int)candidate;
                        decoded.Command = command;
                    }
                }

                var semantic = catalog.SemanticEvent(direction, moduleTag.Value, region, command);
                if (semantic is not null)
                {
                    decoded.EventId = semantic.EventId;
                    decoded.EventName = semantic.Name;
                    decoded.Aliases = semantic.Aliases;
                    decoded.Confidence = semantic.Confidence;
                    decoded.Evidence = semantic.Evidence;
                }
                else
                {
                    var suffix = command is not null ? $".cmd{command}" : "";
                    var handler = string.IsNullOrEmpty(decoded.Handler) ? $"region{region}" : decoded.Handler;
                    decoded.EventName = $"{decoded.Module}.{handler}{suffix}";
                    decoded.Confidence = "fallback";
                    decoded.EventId = $"{(direction == MbcDirection.Client ? "C2S" : "S2C")}:{moduleTag.Value}:{region}:{(command is null ? "*" : command.ToString())}";
                }

                MbcPayloadDecoder.Apply(decoded, recovered);
                result.Events.Add(decoded);
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.Status = result.Events.Count > 0 ? "partial_truncated" : "truncated";
        }

        return Finish(result, bits, originalBits);
    }

    private int? EnterContext(int processId, int wireModuleTag, MbcDecodeResult result, out string contextState)
    {
        if (wireModuleTag == 0)
        {
            int? previousOrNull = processModules.TryGetValue(processId, out var previous) ? previous : null;
            result.Lifecycle.Add(new MbcLifecycleEvent
            {
                Type = "EKill",
                ProcessId = processId,
                ResolvedModuleTag = previousOrNull,
                ResolvedModule = catalog.ModuleName(previousOrNull)
            });
            if (processId > 0xFFFF || previousOrNull is null)
            {
                contextState = "ignored_ekill_target";
                return null;
            }

            contextState = "existing";
            return previousOrNull;
        }

        if (processId > 0xFFFF)
        {
            contextState = "ignored_invalid_process";
            return null;
        }

        if (processModules.TryGetValue(processId, out var bound))
        {
            contextState = "existing";
            return bound;
        }

        if (!catalog.Modules.ContainsKey(wireModuleTag))
        {
            contextState = "ignored_unknown_module";
            return null;
        }

        processModules[processId] = wireModuleTag;
        contextState = "created";
        return wireModuleTag;
    }

    private int? RecreateForRegion(int processId, int wireModuleTag)
    {
        if (wireModuleTag == 0 || processId > 0xFFFF || !catalog.Modules.ContainsKey(wireModuleTag))
        {
            return null;
        }

        processModules[processId] = wireModuleTag;
        return wireModuleTag;
    }

    private static List<MbcDecodedField> DecodeFields(MbcBitReader bits, int[] desc, int[]? basePosition)
    {
        var fields = new List<MbcDecodedField>();
        var i = 0;
        while (i < desc.Length)
        {
            var raw = desc[i];
            var signedDesc = raw >= 128 ? raw - 256 : raw;
            var start = bits.Pos;
            if (raw is 0x65 or 0x66)
            {
                var countWidth = raw == 0x65 ? 4 : 8;
                var count = bits.Read(countWidth);
                if (i + 1 >= desc.Length)
                {
                    throw new MbcDecodeException("array descriptor has no element descriptor");
                }

                var elemRaw = desc[i + 1];
                var elem = elemRaw >= 128 ? elemRaw - 256 : elemRaw;
                if (Math.Abs(elem) is < 1 or > 32)
                {
                    throw new MbcDecodeException($"unsupported array element descriptor 0x{elemRaw:X2}");
                }

                var values = new long[count];
                for (var n = 0; n < count; n++)
                {
                    values[n] = elem < 0 ? bits.Signed(Math.Abs(elem)) : bits.Read(elem);
                }

                fields.Add(new MbcDecodedField
                {
                    Descriptor = raw,
                    Kind = "array",
                    ArrayValue = values,
                    BitOffset = start,
                    BitLength = bits.Pos - start,
                    Display = FormatArray(values, elem)
                });
                i += 2;
                continue;
            }

            if (Math.Abs(signedDesc) is >= 1 and <= 32)
            {
                var value = signedDesc < 0 ? bits.Signed(Math.Abs(signedDesc)) : bits.Read(signedDesc);
                fields.Add(new MbcDecodedField
                {
                    Descriptor = signedDesc,
                    Kind = "int",
                    IntValue = value,
                    BitOffset = start,
                    BitLength = bits.Pos - start,
                    Display = value.ToString()
                });
            }
            else if (raw == 0x67)
            {
                var value = DecodeVarint(bits);
                fields.Add(new MbcDecodedField
                {
                    Descriptor = 0x67,
                    Kind = "varint",
                    IntValue = value,
                    BitOffset = start,
                    BitLength = bits.Pos - start,
                    Display = value.ToString()
                });
            }
            else if (raw == 0x68)
            {
                fields.Add(new MbcDecodedField
                {
                    Descriptor = 0x68,
                    Kind = "pseudo",
                    BitOffset = start,
                    BitLength = 0,
                    Display = ""
                });
            }
            else if (raw is 0x69 or 0x6A or 0x6B)
            {
                var axis = raw - 0x69;
                var coordRaw = bits.Read(12);
                var (delta, value) = RelCoord(coordRaw, basePosition is null ? null : basePosition[axis]);
                var kind = axis == 0 ? "relX" : axis == 1 ? "relY" : "relZ";
                fields.Add(new MbcDecodedField
                {
                    Descriptor = raw,
                    Kind = kind,
                    IntValue = coordRaw,
                    DoubleValue = value,
                    BitOffset = start,
                    BitLength = bits.Pos - start,
                    Display = value is null ? $"raw={coordRaw} delta={delta:0.###}" : $"{value:0.###}"
                });
            }
            else if (raw == 0x6C)
            {
                var angleRaw = bits.Read(8);
                var degrees = angleRaw * 1.40625;
                fields.Add(new MbcDecodedField
                {
                    Descriptor = 0x6C,
                    Kind = "angle",
                    IntValue = angleRaw,
                    DoubleValue = degrees,
                    BitOffset = start,
                    BitLength = bits.Pos - start,
                    Display = $"{degrees:0.##} deg"
                });
            }
            else
            {
                throw new MbcDecodeException($"unsupported descriptor 0x{raw:X2}");
            }

            i++;
        }

        return fields;
    }

    private static int DecodeVarint(MbcBitReader bits)
    {
        var negative = bits.Read(1) == 1;
        var tier = bits.Read(2);
        var magnitude = bits.Read(VarintWidths[tier]);
        return negative ? -magnitude : magnitude;
    }

    private static (double Delta, double? Value) RelCoord(int raw, int? baseValue)
    {
        var magnitude = raw & 0x7FF;
        var denominator = (1.0 / 40.0 - 1.0 / 160.0) * (magnitude / 2047.0) + 1.0 / 160.0;
        var delta = 1.0 / denominator - 40.0;
        if ((raw & 0x800) != 0)
        {
            delta = -delta;
        }

        return (delta, baseValue is null ? null : baseValue.Value + delta);
    }

    private static string FormatArray(long[] values, int element)
    {
        if (Math.Abs(element) == 8)
        {
            var data = values.Select(v => (byte)(v & 0xFF)).ToArray();
            var text = new string(data.Select(v => v is >= 32 and < 127 ? (char)v : '.').ToArray());
            return $"hex={Convert.ToHexString(data)} text={text}";
        }

        return string.Join(",", values);
    }

    private static MbcDecodeResult Finish(MbcDecodeResult result, MbcBitReader? bits, int originalBits)
    {
        if (bits is not null)
        {
            result.ParseBit = bits.Pos;
            result.OverreadBits = Math.Max(0, bits.Pos - originalBits);
        }

        return result;
    }
}
