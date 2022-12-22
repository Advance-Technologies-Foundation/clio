using System;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common
{

	#region Class: UnixTimeConverterTestCase

	public class UnixTimeConverterTestCase
	{

		#region Methods: Public

		[Test, Category("Unit")]
		public void CovertFromUnixDateTime_ReturnsDateTime() {
			const long unixDateTime = 1557012281234;
			DateTime expectedDateTime = new DateTime(2019, 5, 4, 23,24, 41, 234, DateTimeKind.Utc);
			DateTime actualDateTime = UnixTimeConverter.CovertFromUnixDateTimeToUtc(unixDateTime);
			actualDateTime.Should().Be(expectedDateTime);
		}

		[Test, Category("Unit")]
		public void CovertToUnixDateTime_ReturnsUnixDateTime() {
			const long expectedUnixDateTime = 1557012281234;
			DateTime dateTime = new DateTime(2019, 5, 4, 23,24, 41, 234, DateTimeKind.Utc);
			long actualUnixDateTime = UnixTimeConverter.CovertToUnixDateTime(dateTime);
			actualUnixDateTime.Should().Be(expectedUnixDateTime);
		}

		#endregion

	}

	#endregion

}