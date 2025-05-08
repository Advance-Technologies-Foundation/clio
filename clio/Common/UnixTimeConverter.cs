using System;

namespace Clio.Common;

public static class UnixTimeConverter
{
    private static readonly DateTime EpochUnixDateTime = new (1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

    public static DateTime CovertFromUnixDateTimeToUtc(long unixDateTime) =>
        EpochUnixDateTime.AddMilliseconds(unixDateTime);

    public static long CovertToUnixDateTime(DateTime dateTime) =>
        (long)dateTime.ToUniversalTime().Subtract(EpochUnixDateTime).TotalMilliseconds;
}
