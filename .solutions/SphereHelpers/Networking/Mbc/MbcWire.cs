namespace SphServer.Helpers.Networking;

/// <summary>
///     Msg300 body markers: 7-bit wire. 0 = TERM, 0x3F = NewProcessOrModule, else region = wire - 1.
/// </summary>
public static class MbcWire
{
    public const int Terminator = 0;
    public const int Switch = 0x3F;

    public static string RegionType(int wire, int? region = null, string? recoveredName = null, int? command = null)
    {
        if (wire == Terminator)
        {
            return "TERM";
        }

        if (wire == Switch)
        {
            return "NewProcessOrModule";
        }

        var resolvedRegion = region ?? wire - 1;
        if (resolvedRegion == 4)
        {
            return "NextCommand";
        }

        if (resolvedRegion == 61)
        {
            return "SpawnData";
        }

        if (resolvedRegion == 11)
        {
            return "PositionStream";
        }

        if (command is null &&
            !string.IsNullOrEmpty(recoveredName) &&
            !recoveredName.StartsWith("cmd", StringComparison.OrdinalIgnoreCase) &&
            !recoveredName.StartsWith("r", StringComparison.Ordinal) &&
            recoveredName is not ("ContMan" or "TradeMan" or "CheckPing" or "Manager" or "SpawnSnapshot"))
        {
            return recoveredName;
        }

        return resolvedRegion switch
        {
            1 => "TransformUpdate",
            10 => "SetStat",
            _ => $"r{resolvedRegion}"
        };
    }
}
