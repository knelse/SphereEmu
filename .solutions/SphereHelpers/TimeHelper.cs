namespace SphServer.Helpers;

/// <summary>
///     Sphere calendar matching client <c>SferaGameCalendar::fromUnixTime</c>.
///     Packed fields: minute/quarter, day-lsb/hour/minute-hi, month/day-hi, year-lo, year-hi2.
///     Display year = wire year + 7800. Credentials writes the first four bytes as Owner
///     array8 payload (bitstream), not as five aligned bytes after <c>20 10</c>.
/// </summary>
public static class TimeHelper
{
    // (unix + offset) * 48 / 60 → quarter-minute ticks; 5760 ticks/day, 365-day years.
    private const long UnixOffset = 0x4900FAE80;
    private const int TicksPerDay = 5760;
    private const int TicksPerYear = 365 * TicksPerDay;

    /// <summary>
    ///     Components with year = wire year (add 7800 for HUD). Same source as encode.
    /// </summary>
    public static DateTime GetCurrentSphereDateTime()
    {
        var c = CurrentComponents();
        return new DateTime(Math.Max(1, c.Year), c.Month, c.Day, c.Hour, c.Minute, 0);
    }

    /// <summary>
    ///     Five packed calendar bytes. Year-hi is the low 2 bits of byte 4 (not 0x34+).
    /// </summary>
    public static byte[] EncodeCurrentSphereDateTime()
    {
        var c = CurrentComponents();
        var minuteLo = (byte)((c.Minute & 0b1111) << 4);
        var quarter = (byte)(c.Quarter & 0b1111);
        var first = (byte)(minuteLo | quarter);

        var dayLsb = (byte)((c.Day & 1) << 7);
        var hours = (byte)(c.Hour << 2);
        var minuteHi = (byte)((c.Minute & 0b110000) >> 4);
        var second = (byte)(dayLsb | hours | minuteHi);

        var dayHi = (byte)((c.Day & 0b11110) >> 1);
        var month = (byte)(c.Month << 4);
        var third = (byte)(month | dayHi);

        var yearLo = (byte)(c.Year & 0xFF);
        var yearHi = (byte)((c.Year >> 8) & 0b11);

        return [first, second, third, yearLo, yearHi];
    }

    private static SphereCalendarComponents CurrentComponents()
    {
        return FromUnixTime(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>Client <c>SferaGameCalendar::fromUnixTime</c> components (wire year, not HUD year).</summary>
    public static SphereCalendarComponents FromUnixTime(long unixSeconds)
    {
        var ticks = (unixSeconds + UnixOffset) * 48 / 60;
        var yearTicks = (int)(ticks / TicksPerYear);
        var dayOfYear = (int)(ticks % TicksPerYear / TicksPerDay);
        var timeOfDay = (int)(ticks % TicksPerDay);

        // Wire year10 = (yearTicks + 392) truncated to 10 bits; HUD = that + 7800.
        var year = (yearTicks + 392) & 0x3FF;

        var month = 1;
        while (month < 12 && dayOfYear >= DaysBeforeMonth(month + 1))
        {
            month++;
        }

        var day = dayOfYear - DaysBeforeMonth(month) + 1;
        var hour = timeOfDay / 240;
        var minute = timeOfDay % 240 / 4;
        var quarter = timeOfDay % 4;

        return new SphereCalendarComponents(year, month, day, hour, minute, quarter);
    }

    public readonly record struct SphereCalendarComponents(
        int Year, int Month, int Day, int Hour, int Minute, int Quarter);

    // Client sfera_calendar_days_in_month: no leap days.
    private static int DaysInMonth(int month)
    {
        if (month is < 1 or > 12)
        {
            return 0;
        }

        return 30 + ((month + (month > 7 ? 1 : 0)) & 1) - (month == 2 ? 2 : 0);
    }

    private static int DaysBeforeMonth(int month)
    {
        if (month is < 1 or > 13)
        {
            return 0;
        }

        var days = 0;
        for (var current = 1; current < month; current++)
        {
            days += DaysInMonth(current);
        }

        return days;
    }
}
