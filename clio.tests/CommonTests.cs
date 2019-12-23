using Clio.Common;
using NUnit.Framework;

namespace Clio.Tests
{
	internal class CommonTests
	{
		[Test, Category("Unit")]
		public void BpmPkg_Create_CheckCorrectTplFilePathGettingFromPath() {
			string input = "Test1,Test2, Test3, Test4 , Test5 ";
			string[] expected = { "Test1", "Test2", "Test3", "Test4", "Test5" };
			var actual = StringParser.ParseArray(input);
			Assert.AreEqual(expected, actual);
		}
	}
}