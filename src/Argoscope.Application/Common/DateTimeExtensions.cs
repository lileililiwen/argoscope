namespace Argoscope.Application.Common;

internal static class DateTimeExtensions
{
    public static DateOnly ToDateOnly(this DateTime value) =>
        DateOnly.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
