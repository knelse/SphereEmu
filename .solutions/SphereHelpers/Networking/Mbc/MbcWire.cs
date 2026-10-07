namespace SphServer.Helpers.Networking;

/// 7-bit msg300 markers: 0 is TERM, 0x3F is NewProcessOrModule, otherwise region is wire - 1
public static class MbcWire
{
    public const int Terminator = 0;
    public const int Switch = 0x3F;

    public static string RegionType (int wire, int? region = null, string? recoveredName = null, int? command = null)
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
            !string.IsNullOrEmpty (recoveredName) &&
            !recoveredName.StartsWith ("cmd", StringComparison.OrdinalIgnoreCase) &&
            !recoveredName.StartsWith ("r", StringComparison.Ordinal) &&
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
