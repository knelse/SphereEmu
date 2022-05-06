namespace emu.Helpers;

public static class TimeHelper
{
    public const int UnixTimeOrigin = 1649722100;

    public static DateTime GetCurrentSphereDateTime()
    {
        var now = new DateTimeOffset(DateTime.UtcNow);
        var fromUnixTimeOrigin = now.ToUnixTimeSeconds() - UnixTimeOrigin;
        var sphereTimeOffset = fromUnixTimeOrigin * 12;
        var sphereDateTime = new DateTime().AddSeconds(sphereTimeOffset);
        return sphereDateTime;
    }
}