using System;

namespace Clio.Common
{

	#region Class: UnixTimeConverter

	public static class UnixTimeConverter
	{
		#region Fields: Private

		private static readonly DateTime _epochUnixDateTime = 
			new DateTime(1970,1,1,0,0,0,0,System.DateTimeKind.Utc);

		#endregion

		#region Methods: Public

		public static DateTime CovertFromUnixDateTimeToUtc(long unixDateTime) {
			return _epochUnixDateTime.AddMilliseconds(unixDateTime);
		}

		public static long CovertToUnixDateTime(DateTime dateTime) {
			return (long)(dateTime.ToUniversalTime().Subtract(_epochUnixDateTime)).TotalMilliseconds;
			//return _epochUnixDateTime.AddMilliseconds(unixDateTime).ToLocalTime();
		}

		#endregion

	}

	#endregion

}