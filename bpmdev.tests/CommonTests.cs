using System;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using static bpmcli.tests.AssertionExtensions;
using File = System.IO.File;


namespace bpmcli.tests
{
	class CommonTests
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
