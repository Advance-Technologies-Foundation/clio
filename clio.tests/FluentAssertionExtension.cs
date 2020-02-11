using FluentAssertions;

namespace Clio.Tests
{
	public static class AssertionExtensions
	{
		public static FileAssertion Should(this FileToTest file) => new FileAssertion { File = file };
		public class FileAssertion
		{
			public FileToTest File;
			public void NotExist(string because = null, params object[] reasonArgs)
				=> System.IO.File.Exists(File.Path).Should()
					.BeFalse(because ?? $"Expect existing file with path {File.Path}", reasonArgs);
			public void Exist(string because = null, params object[] reasonArgs)
				=> System.IO.File.Exists(File.Path).Should()
					.BeTrue(because ?? $"Expect existing file with path {File.Path}", reasonArgs);
		}
		public static FileToTest File(string fName) => new FileToTest { Path = fName };
		public class FileToTest { public string Path; }


		public static DirectoryAssertion Should(this DirectoryToTest directory) =>
			new DirectoryAssertion { Directory = directory };
		public class DirectoryAssertion
		{
			public DirectoryToTest Directory;
			public void NotExist(string because = null, params object[] reasonArgs)
				=> System.IO.Directory.Exists(Directory.Path).Should()
					.BeFalse(because ?? $"Expect not existing directory with path {Directory.Path}", reasonArgs);
			public void Exist(string because = null, params object[] reasonArgs)
				=> System.IO.Directory.Exists(Directory.Path).Should()
					.BeTrue(because ?? $"Expect existing directory with path {Directory.Path}", reasonArgs);
		}
		public static DirectoryToTest Directory(string dName) => new DirectoryToTest { Path = dName };
		public class DirectoryToTest { public string Path; }
	}
}
