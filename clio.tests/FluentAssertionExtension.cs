using FluentAssertions;

namespace Clio.Tests;

public static class AssertionExtensions
{

    #region Class: Public

    public class DirectoryAssertion
    {

        #region Fields: Public

        public DirectoryToTest Directory;

        #endregion

        #region Methods: Public

        public void Exist(string because = null, params object[] reasonArgs) =>
            System.IO.Directory.Exists(Directory.Path).Should()
                  .BeTrue(because ?? $"Expect existing directory with path {Directory.Path}", reasonArgs);

        public void NotExist(string because = null, params object[] reasonArgs) =>
            System.IO.Directory.Exists(Directory.Path).Should()
                  .BeFalse(because ?? $"Expect not existing directory with path {Directory.Path}", reasonArgs);

        #endregion

    }

    public class DirectoryToTest
    {

        #region Fields: Public

        public string Path;

        #endregion

    }

    public class FileAssertion
    {

        #region Fields: Public

        public FileToTest File;

        #endregion

        #region Methods: Public

        public void Exist(string because = null, params object[] reasonArgs) =>
            System.IO.File.Exists(File.Path).Should()
                  .BeTrue(because ?? $"Expect existing file with path {File.Path}", reasonArgs);

        public void NotExist(string because = null, params object[] reasonArgs) =>
            System.IO.File.Exists(File.Path).Should()
                  .BeFalse(because ?? $"Expect existing file with path {File.Path}", reasonArgs);

        #endregion

    }

    public class FileToTest
    {

        #region Fields: Public

        public string Path;

        #endregion

    }

    #endregion

    #region Methods: Public

    public static DirectoryToTest Directory(string dName) =>
        new()
        {
            Path = dName
        };

    public static FileToTest File(string fName) =>
        new()
        {
            Path = fName
        };

    public static FileAssertion Should(this FileToTest file) =>
        new()
        {
            File = file
        };

    public static DirectoryAssertion Should(this DirectoryToTest directory) =>
        new()
        {
            Directory = directory
        };

    #endregion

}
