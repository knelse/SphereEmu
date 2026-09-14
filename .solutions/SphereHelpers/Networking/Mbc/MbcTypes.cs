namespace SphServer.Helpers.Networking;

public enum MbcDirection
{
    Client,
    Server
}

public sealed class MbcModuleSpec
{
    public required string Name { get; init; }
    public required IReadOnlyDictionary<int, MbcRegionSpec> Regions { get; init; }
}

public sealed class MbcRegionSpec
{
    public int Flags { get; init; }
    public string Handler { get; init; } = "";
    public string Schema { get; init; } = "";
    public int[] Desc { get; init; } = [];
}

public sealed class MbcEventInfo
{
    public required string EventId { get; init; }
    public required string Name { get; init; }
    public string Confidence { get; init; } = "";
    public string Evidence { get; init; } = "";
    public string Handler { get; init; } = "";
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

public sealed class MbcDecodedField
{
    public int Descriptor { get; init; }
    public required string Kind { get; init; }
    public long? IntValue { get; init; }
    public double? DoubleValue { get; init; }
    public long[]? ArrayValue { get; init; }
    public int BitOffset { get; init; }
    public int BitLength { get; init; }
    public required string Display { get; init; }
}

public sealed class MbcDecodedEvent
{
    public int ProcessId { get; set; }
    public int ModuleTag { get; set; }
    public string Module { get; set; } = "";
    public int WireRegion { get; set; }
    public int Region { get; set; }
    public string Handler { get; set; } = "";
    public int Flags { get; set; }
    public string Schema { get; set; } = "";
    public int? Command { get; set; }
    public string EventId { get; set; } = "";
    public string EventName { get; set; } = "";
    public string Confidence { get; set; } = "";
    public string Evidence { get; set; } = "";
    public IReadOnlyList<string> Aliases { get; set; } = [];
    public string? WireSchemaOverride { get; set; }
    public int StartBit { get; set; }
    public int EndBit { get; set; }
    public List<MbcDecodedField> Fields { get; } = [];
}

public sealed class MbcLifecycleEvent
{
    public required string Type { get; init; }
    public int ProcessId { get; init; }
    public int? ResolvedModuleTag { get; init; }
    public string ResolvedModule { get; init; } = "";
}

public sealed class MbcDecodeResult
{
    public string Status { get; set; } = "fail";
    public MbcDirection Direction { get; set; }
    public byte Control { get; set; }
    public bool HasPosition { get; set; }
    public int[]? BasePosition { get; set; }
    public int Tick { get; set; }
    public int ProcessId { get; set; }
    public int ModuleTag { get; set; }
    public int? EffectiveModuleTag { get; set; }
    public string Module { get; set; } = "";
    public int WireBitLength { get; set; }
    public int ParseBit { get; set; }
    public int OverreadBits { get; set; }
    public string Error { get; set; } = "";
    public int? StopWire { get; set; }
    public int? StopRegion { get; set; }
    public int? StopBit { get; set; }
    public int TerminatorBit { get; set; } = -1;
    public int ContextSwitches { get; set; }
    public List<MbcDecodedEvent> Events { get; } = [];
    public List<MbcLifecycleEvent> Lifecycle { get; } = [];

    public bool HasUsefulDecode =>
        Events.Count > 0
        || Lifecycle.Count > 0
        || Status is "full"
            or "client_stop_invalid_wire"
            or "client_stop_undeclared_region"
            or "ignored_invalid_process"
            or "ignored_unknown_module"
            or "ignored_ekill_target";
}

public readonly record struct MbcTcpFrame(int Offset, int Size, ushort Message, byte[] Payload);
