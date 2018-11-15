using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;

namespace bpmcli.tests
{
	public static class AssertionExtensions
	{
		public static FileAssertion Should(this FileToTest file) => new FileAssertion { File = file };
		public class FileAssertion
		{
			public FileToTest File;
			public void NotExist(string because = "", params object[] reasonArgs)
				=> System.IO.File.Exists(File.Path).Should().BeFalse(because, reasonArgs);
			public void Exist(string because = "", params object[] reasonArgs)
				=> System.IO.File.Exists(File.Path).Should().BeTrue(because, reasonArgs);
		}
		public static FileToTest File(string fName) => new FileToTest { Path = fName };
		public class FileToTest { public string Path; }
	}
}
