using FluentAssertions;
using NUnit.Framework;
using System;
using System.IO;
using static Clio.Tests.AssertionExtensions;
using File = System.IO.File;


namespace Clio.Tests;

// NonParallelizable because two of the tests below assign Environment.CurrentDirectory and
// Environment.SetEnvironmentVariable("PATH"), both of which are PROCESS-global. Under the assembly's
// [Parallelizable(ParallelScope.Fixtures)] every concurrently running fixture sees those mutations, and
// several of them (CreateUiProjectToolTests, DownloadConfigurationToolTests, WorkspaceSyncToolTests,
// AddPackageCommandTests) capture-and-restore the working directory themselves — so one can snapshot this
// fixture's temporary directory and restore it afterwards, leaving the process parked inside the directory
// the teardown then has to delete. Windows refuses to remove a directory that is a process's current one.
[NonParallelizable]
[Category("Unit")]
[Property("Module", "Core")]
public class CreatioPkgTests
{

	private const string PackageName = "TestPackage";
	private const string PackageUId = "7133B6CF-E7AB-488E-8E03-80BBF38FD12A";
	private const string Maintainer = "TestCompany";
	private const string ResultDir = "TestResult";
	private const string ExpectFilesDir = "samplefiles";

	// ABSOLUTE, resolved once against the test assembly's own directory. The relative form was a second
	// defect independent of the parallelism above: every use resolved against whatever the process's current
	// directory happened to be at that moment, so setup and teardown could mean different directories.
	// Observed, not hypothetical - after a `Category!=Integration` run the directory is left behind, because
	// this fixture's own tests are filtered out (they are Category=Integration) while its one-time setup and
	// teardown still run, and the teardown's existence check resolved somewhere else and quietly skipped.
	private static readonly string ResultDirPath =
		Path.Combine(TestContext.CurrentContext.TestDirectory, ResultDir);

	private static readonly string ExpectFilesDirPath =
		Path.Combine(TestContext.CurrentContext.TestDirectory, ExpectFilesDir);

	private static readonly DateTime TestCreatedOn = new DateTime(2018, 1, 1, 1, 12, 10, 200, DateTimeKind.Utc);

	private class CreatioPkgMock : CreatioPackage
	{

		public CreatioPkgMock(bool setDirectory = true) : base(CreatioPkgTests.PackageName, CreatioPkgTests.Maintainer)
		{
			ProjectId = Guid.Parse(PackageUId);
			CreatedOn = TestCreatedOn;
			if (setDirectory)
			{
				FullPath = ResultDirPath;
			}
		}

		public void CreateDescriptor()
		{
			CreatePkgDescriptor();
		}

		public void CreateProjFile()
		{
			CreateProj();
		}

		public void CreateNugetPackageConfig()
		{
			CreatePackageConfig();
		}

		public void CreateAssemblyProps()
		{
			CreateAssemblyInfo();
		}

	}

	[OneTimeSetUp]
	public void SetupOneTime()
	{
		if (!System.IO.Directory.Exists(ResultDirPath))
		{
			System.IO.Directory.CreateDirectory(ResultDirPath);
		}
	}
	
	[Test, Category("Integration")]
	[TestCase(CreatioPackage.DescriptorName, CreatioPackage.DescriptorName, "CreateDescriptor", TestName = "Check Correct Descriptor")]
	[TestCase(PackageName + "." + CreatioPackage.CsprojExtension, "Proj.csproj", "CreateProjFile",
		TestName = "Check Correct ProjectFile")]
	[TestCase(CreatioPackage.PackageConfigName, CreatioPackage.PackageConfigName, "CreateNugetPackageConfig",
		TestName = "Check Correct PackageConfig")]
	
#if WINDOWS	
	[TestCase(
		CreatioPackage.PropertiesDirName + "\\" + CreatioPackage.AssemblyInfoName, 
		CreatioPackage.AssemblyInfoName,
		"CreateAssemblyProps", TestName = "Check Correct AssemblyInfo file")]
# else
	[TestCase(
		CreatioPackage.PropertiesDirName + "/" + CreatioPackage.AssemblyInfoName,
		CreatioPackage.AssemblyInfoName,
		"CreateAssemblyProps", TestName = "Check Correct AssemblyInfo file")]
#endif
	public void CreatioPkg_Create_CheckCorrectFiles(string resultFileName, string sampleFileName, string methodName)
	{
		var pkg = new CreatioPkgMock();
		pkg.GetType().GetMethod(methodName).Invoke(pkg, null);
		var resultPath = Path.Combine(pkg.FullPath, resultFileName);
		var samplePath = Path.Combine(ExpectFilesDirPath, sampleFileName);
		File(resultPath).Should().Exist();
		File.ReadAllText(resultPath).Should().BeEquivalentTo(File.ReadAllText(samplePath));
	}

	[Test, Category("Integration")]
	public void CreatioPkg_Create_CheckPackageStructure()
	{
		var oldEnvironment = Environment.CurrentDirectory;
		CreatioPackage pkg;
		try
		{
			string workDirPath = ResultDirPath;
			if (!System.IO.Directory.Exists(workDirPath))
			{
				System.IO.Directory.CreateDirectory(workDirPath);
			}
			Environment.CurrentDirectory = workDirPath;
			pkg = CreatioPackage.CreatePackage(PackageName, Maintainer);
		}
		finally
		{
			Environment.CurrentDirectory = oldEnvironment;
		}
		pkg.Create();
		File(Path.Combine(pkg.FullPath, CreatioPackage.DescriptorName)).Should().Exist();
		File(Path.Combine(pkg.FullPath, PackageName + "." + CreatioPackage.CsprojExtension)).Should().Exist();
		File(Path.Combine(pkg.FullPath, CreatioPackage.PackageConfigName)).Should().Exist();
		File(Path.Combine(pkg.FullPath, CreatioPackage.PropertiesDirName,CreatioPackage.AssemblyInfoName))
			.Should().Exist();
		File(Path.Combine(pkg.FullPath, "Files","cs", "EmptyClass.cs")).Should().Exist();
		File(Path.Combine(pkg.FullPath, "Assemblies",CreatioPackage.PlaceholderFileName)).Should().Exist();
		File(Path.Combine(pkg.FullPath, "Data",CreatioPackage.PlaceholderFileName)).Should().Exist();
		File(Path.Combine(pkg.FullPath, "Resources",CreatioPackage.PlaceholderFileName)).Should().Exist();
		File(Path.Combine(pkg.FullPath, "Schemas",CreatioPackage.PlaceholderFileName)).Should().Exist();
		File(Path.Combine(pkg.FullPath, "SqlScripts",CreatioPackage.PlaceholderFileName)).Should().Exist();
		Directory(Path.Combine(pkg.FullPath, "Assemblies")).Should().Exist();
		Directory(Path.Combine(pkg.FullPath, "Data")).Should().Exist();
		Directory(Path.Combine(pkg.FullPath, "Resources")).Should().Exist();
		Directory(Path.Combine(pkg.FullPath, "Schemas")).Should().Exist();
		Directory(Path.Combine(pkg.FullPath, "SqlScripts")).Should().Exist();
		Directory(Path.Combine(pkg.FullPath, "Files")).Should().Exist();
		Directory(Path.Combine(pkg.FullPath, "Files","cs")).Should().Exist();
	}

	[Test, Category("Integration")]
	public void CreatioPkg_Create_CheckCorrectTplFilePathGettingFromPath()
	{
		var oldCD = Environment.CurrentDirectory;
		var oldPath = Environment.GetEnvironmentVariable("PATH");
		CreatioPackage pkg;
		try
		{
			string workDirPath = ResultDirPath;
			if (!System.IO.Directory.Exists(workDirPath))
			{
				System.IO.Directory.CreateDirectory(workDirPath);
			}
			Environment.CurrentDirectory = workDirPath;
			Environment.SetEnvironmentVariable("PATH", oldCD + ";C:\\Program Files\\dotnet");
			pkg = CreatioPackage.CreatePackage(PackageName, Maintainer);
			pkg.Create();
		}
		finally
		{
			Environment.CurrentDirectory = oldCD;
			Environment.SetEnvironmentVariable("PATH", oldPath);
		}
		var resultPath = Path.Combine(pkg.FullPath, CreatioPackage.PackageConfigName);
		var samplePath = Path.Combine(ExpectFilesDirPath, CreatioPackage.PackageConfigName);
		File(resultPath).Should().Exist();
		File.ReadAllText(resultPath).Should().BeEquivalentTo(File.ReadAllText(samplePath));
	}


	/// <summary>
	/// Removes the fixture's scratch directory, best-effort.
	/// </summary>
	/// <remarks>
	/// Deliberately cannot fail the run. This directory lives under the test assembly's own output folder and
	/// is recreated by the next setup, so failing to remove it costs nothing — whereas throwing from here
	/// fails the whole FIXTURE and, in CI, the whole job, which is how a leftover file handle turned into a
	/// red build with every test passing.
	/// <para>
	/// The retry is for the handle that is about to be released rather than held: on Windows a directory
	/// created moments earlier can still be open to a scanner or an indexer, and
	/// <see cref="System.IO.Directory.Delete(string, bool)"/> surfaces that as an
	/// <see cref="IOException"/> rather than waiting. Move the current directory out of the tree first, so
	/// the one cause this fixture can create itself — deleting the process's own working directory — cannot
	/// arise even if a test's restore did not run.
	/// </para>
	/// </remarks>
	[OneTimeTearDown]
	public void TeardownOneTime()
	{
		Environment.CurrentDirectory = TestContext.CurrentContext.TestDirectory;
		for (int attempt = 0; attempt < 3; attempt++)
		{
			if (!System.IO.Directory.Exists(ResultDirPath))
			{
				return;
			}
			try
			{
				System.IO.Directory.Delete(ResultDirPath, true);
				return;
			}
			catch (IOException)
			{
				System.Threading.Thread.Sleep(100);
			}
			catch (UnauthorizedAccessException)
			{
				System.Threading.Thread.Sleep(100);
			}
		}
		TestContext.Out.WriteLine(
			$"Could not remove '{ResultDirPath}'. Left in place on purpose: the next build cleans it, and "
			+ "failing the fixture over a scratch directory would report every passing test as a red job.");
	}

}
