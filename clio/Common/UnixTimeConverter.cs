using System;

namespace Clio.Common;

#region Class: UnixTimeConverter

public static class UnixTimeConverter
{

	#region Fields: Private

	private static readonly DateTime EpochUnixDateTime = new(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

	#endregion

	#region Methods: Public

	public static DateTime CovertFromUnixDateTimeToUtc(long unixDateTime){
		return EpochUnixDateTime.AddMilliseconds(unixDateTime);
	}

	public static long CovertToUnixDateTime(DateTime dateTime){
		return (long)dateTime.ToUniversalTime().Subtract(EpochUnixDateTime).TotalMilliseconds;
	}

	#endregion

}

#endregion