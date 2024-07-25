using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Castle.Core.Logging;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common
{

	[TestFixture]
	internal class ConsoleProgressbarTest
	{

		[TestCase(0, 10, "[..........] 0%")]
		[TestCase(50, 10, "[*****.....] 50%")]
		[TestCase(100, 10, "[**********] 100%")]
		[TestCase(0, 5, "[.....] 0%")]
		[TestCase(40, 5, "[**...] 40%")]
		[TestCase(50, 5, "[**...] 50%")]
		[TestCase(100, 5, "[*****] 100%")]
		[TestCase(46, 5, "[**...] 46%")]

		public void GetBuatifyProgress_CorrectReturn( int value, int scale, string expectedResult) {
			var progressbar = new ConsoleProgressbar() {
				Scale = scale
			};
			string actionName = "testaction";
			string result = progressbar.GetBuatifyProgress(actionName, value);
			result.Should().Contain(expectedResult);
		}

		[TestCase(0, 100, 10, "[..........] 0%")]
		[TestCase(500, 1000, 10, "[*****.....] 50%")]
		[TestCase(1000, 1000, 10, "[**********] 100%")]
		[TestCase(0, 2000, 5, "[.....] 0%")]
		[TestCase(400, 500, 5, "[****.] 80%")]
		[TestCase(500, 1000, 5, "[**...] 50%")]
		[TestCase(220, 1000, 5, "[*....] 22%")]

		public void GetBuatifyProgress_CorrectReturnFromCurrentTotal(int value, int total, int scale,
				string expectedResult) {
			var progressbar = new ConsoleProgressbar() {
				Scale = scale
			};
			string actionName = "testaction";
			string result = progressbar.GetBuatifyProgress(actionName, value, total);
			result.Should().Contain(expectedResult);
		}
	}
}
