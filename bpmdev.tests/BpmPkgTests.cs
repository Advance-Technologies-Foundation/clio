using System;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using static bpmcli.tests.AssertionExtensions;
using File = System.IO.File;


namespace bpmcli.tests
{
	public class BpmPkgTests
	{

		private const string PackageName = "TestPackage";
		private const string PackageUId = "7133B6CF-E7AB-488E-8E03-80BBF38FD12A";
		private const string Maintainer = "TestCompany";
		private const string ResultDir = "TestResult";
		private const string ExpectFilesDir = "samplefiles";

		private static readonly DateTime TestCreatedOn = new DateTime(2018, 1, 1, 1, 12, 10, 200, DateTimeKind.Utc);

		private class BpmPkgMock : BpmPkg
		{

			public BpmPkgMock(bool setDirectory = true) : base(BpmPkgTests.PackageName, BpmPkgTests.Maintainer) {
				ProjectId = Guid.Parse(PackageUId);
				CreatedOn = TestCreatedOn;
				if (setDirectory) {
					FullPath = Path.Combine(Environment.CurrentDirectory, ResultDir);
				}
			}

			public void CreateDescriptor() {
				CreatePkgDescriptor();
			}

			public void CreateProjFile() {
				CreateProj();
			}

			public void CreateNugetPackageConfig() {
				CreatePackageConfig();
			}

			public void CreateAssemblyProps() {
				CreateAssemblyInfo();
			}

		}

		[OneTimeSetUp]
		public void SetupOneTime() {
			if (!System.IO.Directory.Exists(ResultDir)) {
				System.IO.Directory.CreateDirectory(ResultDir);
			}
		}

		[Test, Category("Integration")]
		[TestCase(BpmPkg.DescriptorName, BpmPkg.DescriptorName, "CreateDescriptor", TestName = "Check Correct Descriptor")]
		[TestCase(PackageName + "." + BpmPkg.CsprojExtension, "Proj.csproj", "CreateProjFile",
			TestName = "Check Correct ProjectFile")]
		[TestCase(BpmPkg.PackageConfigName, BpmPkg.PackageConfigName, "CreateNugetPackageConfig",
			TestName = "Check Correct PackageConfig")]
		[TestCase(BpmPkg.PropertiesDirName + "\\" + BpmPkg.AssemblyInfoName, BpmPkg.AssemblyInfoName,
			"CreateAssemblyProps", TestName = "Check Correct AssemblyInfo")]
		public void BpmPkg_Create_CheckCorrectFiles(string resultFileName, string sampleFileName, string methodName) {
			var pkg = new BpmPkgMock();
			pkg.GetType().GetMethod(methodName).Invoke(pkg, null);
			var resultPath = Path.Combine(pkg.FullPath, resultFileName);
			var samplePath = Path.Combine(Environment.CurrentDirectory, ExpectFilesDir, sampleFileName);
			File(resultPath).Should().Exist();
			File.ReadAllText(resultPath).Should().BeEquivalentTo(File.ReadAllText(samplePath));
		}

		[Test, Category("Integration")]
		public void BpmPkg_Create_CheckPackageStructure() {
			var oldEnvironment = Environment.CurrentDirectory;
			Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, ResultDir);
			var pkg = BpmPkg.CreatePackage(PackageName, Maintainer);
			Environment.CurrentDirectory = oldEnvironment;
			pkg.Create();
			File(Path.Combine(pkg.FullPath, BpmPkg.DescriptorName)).Should().Exist();
			File(Path.Combine(pkg.FullPath, PackageName + "." + BpmPkg.CsprojExtension)).Should().Exist();
			File(Path.Combine(pkg.FullPath, BpmPkg.PackageConfigName)).Should().Exist();
			File(Path.Combine(pkg.FullPath, BpmPkg.PropertiesDirName + "\\" + BpmPkg.AssemblyInfoName))
				.Should().Exist();
			File(Path.Combine(pkg.FullPath, "Files\\cs", "EmptyClass.cs")).Should().Exist();
			File(Path.Combine(pkg.FullPath, "Assemblies"+ "\\" + BpmPkg.PlaceholderFileName)).Should().Exist();
			File(Path.Combine(pkg.FullPath, "Data" + "\\" + BpmPkg.PlaceholderFileName)).Should().Exist();
			File(Path.Combine(pkg.FullPath, "Resources" + "\\" + BpmPkg.PlaceholderFileName)).Should().Exist();
			File(Path.Combine(pkg.FullPath, "Schemas" + "\\" + BpmPkg.PlaceholderFileName)).Should().Exist();
			File(Path.Combine(pkg.FullPath, "SqlScripts" + "\\" + BpmPkg.PlaceholderFileName)).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "Assemblies")).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "Data")).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "Resources")).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "Schemas")).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "SqlScripts")).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "Files")).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "Files\\cs")).Should().Exist();
		}

		[Test, Category("Integration")]
		public void BpmPkg_Create_CheckCorrectTplFilePathGettingFromPath() {
			var oldCD = Environment.CurrentDirectory;
			var oldPath = Environment.GetEnvironmentVariable("PATH");
			Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, ResultDir);
			Environment.SetEnvironmentVariable("PATH", oldCD + ";C:\\Program Files");
			var pkg = new BpmPkgMock(false);
			pkg.CreateNugetPackageConfig();
			Environment.CurrentDirectory = oldCD;
			Environment.SetEnvironmentVariable("PATH", oldPath);
			var resultPath = Path.Combine(pkg.FullPath, BpmPkg.PackageConfigName);
			var samplePath = Path.Combine(Environment.CurrentDirectory, ExpectFilesDir, BpmPkg.PackageConfigName);
			File(resultPath).Should().Exist();
			File.ReadAllText(resultPath).Should().BeEquivalentTo(File.ReadAllText(samplePath));
		}


		[OneTimeTearDown]
		public void TeardownOneTime() {
			if (System.IO.Directory.Exists(ResultDir)) {
				System.IO.Directory.Delete(ResultDir, true);
			}
		}

	}
}