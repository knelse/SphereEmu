namespace SphServer.Helpers;

/// Quarter 0-1, minute 2-7, hour 8-12, day 13-17, month 18-21, wire year 22-31; HUD year is wire +
/// 7800
public static class TimeHelper
{
    // (unix + offset) * 48 / 60 → quarter-minute ticks; 5760 ticks/day, 365-day years.
    private const long UnixOffset = 0x4900FAE80;
    private const int TicksPerDay = 5760;
    private const int TicksPerYear = 365 * TicksPerDay;

    /// Year is the wire year; HUD year is wire + 7800
    public static DateTime GetCurrentSphereDateTime ()
    {
        var c = CurrentComponents ();
        return new DateTime (Math.Max (1, c.Year), c.Month, c.Day, c.Hour, c.Minute, 0);
    }

    /// Little-endian pack word as four bytes
    public static byte[] EncodeCurrentSphereDateTime ()
    {
        var c = CurrentComponents ();
        var packed = (uint) ((c.Year << 22)
                            | (c.Month << 18)
                            | ((c.Day & 31) << 13)
                            | (c.Hour << 8)
                            | (c.Minute << 2)
                            | (c.Quarter & 3));
        return
        [
            (byte) packed,
            (byte) (packed >> 8),
            (byte) (packed >> 16),
            (byte) (packed >> 24)
        ];
    }

    private static SphereCalendarComponents CurrentComponents ()
    {
        return FromUnixTime (DateTimeOffset.UtcNow.ToUnixTimeSeconds ());
    }

    /// Year field is the wire year
    public static SphereCalendarComponents FromUnixTime (long unixSeconds)
    {
        var ticks = (unixSeconds + UnixOffset) * 48 / 60;
        var yearTicks = (int) (ticks / TicksPerYear);
        var dayOfYear = (int) (ticks % TicksPerYear / TicksPerDay);
        var timeOfDay = (int) (ticks % TicksPerDay);

        // Wire year10 = (yearTicks + 392) truncated to 10 bits; HUD = that + 7800.
        var year = (yearTicks + 392) & 0x3FF;

        var month = 1;
        while (month < 12 && dayOfYear >= DaysBeforeMonth (month + 1))
        {
            month++;
        }

        var day = dayOfYear - DaysBeforeMonth (month) + 1;
        var hour = timeOfDay / 240;
        var minute = timeOfDay % 240 / 4;
        var quarter = timeOfDay % 4;

        return new SphereCalendarComponents (year, month, day, hour, minute, quarter);
    }

    public readonly record struct SphereCalendarComponents (
        int Year, int Month, int Day, int Hour, int Minute, int Quarter);

    // Client sfera_calendar_days_in_month: no leap days.
    private static int DaysInMonth (int month)
    {
        if (month is < 1 or > 12)
        {
            return 0;
        }

        return 30 + ((month + (month > 7 ? 1 : 0)) & 1) - (month == 2 ? 2 : 0);
    }

    private static int DaysBeforeMonth (int month)
    {
        if (month is < 1 or > 13)
        {
            return 0;
        }

        var days = 0;
        for (var current = 1; current < month; current++)
        {
            days += DaysInMonth (current);
        }

        return days;
    }
}
