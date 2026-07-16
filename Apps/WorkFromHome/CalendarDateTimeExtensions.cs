using Ical.Net.DataTypes;

namespace HomeAutomationNetDaemon.Apps.WorkFromHome;

internal static class CalendarDateTimeExtensions
{
    internal static DateTimeOffset ToUtc(this DateTime localDateTime, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone));
    }

    internal static DateTimeOffset ToUtc(this CalDateTime calendarDateTime, TimeZoneInfo floatingTimeZone)
    {
        if (calendarDateTime.IsFloating || !calendarDateTime.HasTime)
        {
            return calendarDateTime.Value.ToUtc(floatingTimeZone);
        }

        return new DateTimeOffset(calendarDateTime.AsUtc);
    }

    internal static TimeOnly ToLocalTime(this CalDateTime calendarDateTime, TimeZoneInfo timeZone) =>
        TimeOnly.FromDateTime(calendarDateTime.ToTimeZone(timeZone.Id).Value);
}
