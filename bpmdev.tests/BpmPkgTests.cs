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

			public BpmPkgMock() : base(BpmPkgTests.PackageName, BpmPkgTests.Maintainer) {
				ProjectId = Guid.Parse(PackageUId);
				Directory = Path.Combine(Environment.CurrentDirectory, ResultDir);
				CreatedOn = TestCreatedOn;
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
			if (!Directory.Exists(ResultDir)) {
				Directory.CreateDirectory(ResultDir);
			}
		}

		[Test, Category("Integration")]
		[TestCase(BpmPkg.DesriptorName, BpmPkg.DesriptorName, "CreateDescriptor", TestName = "Check Correct Descriptor")]
		[TestCase(PackageName + "." + BpmPkg.CsprojExtension, "Proj.csproj", "CreateProjFile",
			TestName = "Check Correct ProjectFile")]
		[TestCase(BpmPkg.PackageConfigName, BpmPkg.PackageConfigName, "CreateNugetPackageConfig",
			TestName = "Check Correct PackageConfig")]
		[TestCase(BpmPkg.PropertiesDirName + "\\" + BpmPkg.AssemblyInfoName, BpmPkg.AssemblyInfoName,
			"CreateAssemblyProps", TestName = "Check Correct AssemblyInfo")]
		public void BpmPkg_Create_CheckCorrectFiles(string resultFileName, string sampleFileName, string methodName) {
			var pkg = new BpmPkgMock();
			pkg.GetType().GetMethod(methodName).Invoke(pkg, null);
			var resultPath = Path.Combine(pkg.Directory, resultFileName);
			var samplePath = Path.Combine(Environment.CurrentDirectory, ExpectFilesDir, sampleFileName);
			File(resultPath).Should().Exist();
			File.ReadAllText(resultPath).Should().BeEquivalentTo(File.ReadAllText(samplePath));
		}


		[OneTimeTearDown]
		public void TearodwnOneTime() {
			if (Directory.Exists(ResultDir)) {
				Directory.Delete(ResultDir, true);
			}
			;
		}

	}
}