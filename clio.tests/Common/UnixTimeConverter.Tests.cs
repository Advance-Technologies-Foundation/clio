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
			const long unixDateTime = 1556958281234;
			DateTime expectedDateTime = new DateTime(2019, 5, 4, 11,24, 41, 234);
			DateTime actualDateTime = UnixTimeConverter.CovertFromUnixDateTime(unixDateTime);
			actualDateTime.Should().Be(expectedDateTime);
		}

		[Test, Category("Unit")]
		public void CovertToUnixDateTime_ReturnsUnixDateTime() {
			const long expectedUnixDateTime = 1556958281234;
			DateTime dateTime = new DateTime(2019, 5, 4, 11,24, 41, 234);
			long actualUnixDateTime = UnixTimeConverter.CovertToUnixDateTime(dateTime);
			actualUnixDateTime.Should().Be(expectedUnixDateTime);
		}

		#endregion

	}

	#endregion

}