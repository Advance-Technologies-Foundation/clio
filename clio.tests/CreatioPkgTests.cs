using FluentAssertions;
using NUnit.Framework;
using System;
using System.IO;
using static Clio.Tests.AssertionExtensions;
using File = System.IO.File;


namespace Clio.Tests
{
	public class CreatioPkgTests
	{

		private const string PackageName = "TestPackage";
		private const string PackageUId = "7133B6CF-E7AB-488E-8E03-80BBF38FD12A";
		private const string Maintainer = "TestCompany";
		private const string ResultDir = "TestResult";
		private const string ExpectFilesDir = "samplefiles";

		private static readonly DateTime TestCreatedOn = new DateTime(2018, 1, 1, 1, 12, 10, 200, DateTimeKind.Utc);

		private class CreatioPkgMock : CreatioPackage
		{

			public CreatioPkgMock(bool setDirectory = true) : base(CreatioPkgTests.PackageName, CreatioPkgTests.Maintainer)
			{
				ProjectId = Guid.Parse(PackageUId);
				CreatedOn = TestCreatedOn;
				if (setDirectory)
				{
					FullPath = Path.Combine(Environment.CurrentDirectory, ResultDir);
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
			if (!System.IO.Directory.Exists(ResultDir))
			{
				System.IO.Directory.CreateDirectory(ResultDir);
				var a  ="";
			}
		}

		[Test, Category("Integration")]
		[TestCase(CreatioPackage.DescriptorName, CreatioPackage.DescriptorName, "CreateDescriptor", TestName = "Check Correct Descriptor")]
		[TestCase(PackageName + "." + CreatioPackage.CsprojExtension, "Proj.csproj", "CreateProjFile",
			TestName = "Check Correct ProjectFile")]
		[TestCase(CreatioPackage.PackageConfigName, CreatioPackage.PackageConfigName, "CreateNugetPackageConfig",
			TestName = "Check Correct PackageConfig")]
		[TestCase(CreatioPackage.PropertiesDirName + "\\" + CreatioPackage.AssemblyInfoName, CreatioPackage.AssemblyInfoName,
			"CreateAssemblyProps", TestName = "Check Correct AssemblyInfo file")]
		public void CreatioPkg_Create_CheckCorrectFiles(string resultFileName, string sampleFileName, string methodName)
		{
			var pkg = new CreatioPkgMock();
			pkg.GetType().GetMethod(methodName).Invoke(pkg, null);
			var resultPath = Path.Combine(pkg.FullPath, resultFileName);
			var samplePath = Path.Combine(Environment.CurrentDirectory, ExpectFilesDir, sampleFileName);
			File(resultPath).Should().Exist();
			File.ReadAllText(resultPath).Should().BeEquivalentTo(File.ReadAllText(samplePath));
		}

		[Test, Category("Integration")]
		public void CreatioPkg_Create_CheckPackageStructure()
		{
			var oldEnvironment = Environment.CurrentDirectory;
			Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, ResultDir);
			var pkg = CreatioPackage.CreatePackage(PackageName, Maintainer);
			Environment.CurrentDirectory = oldEnvironment;
			pkg.Create();
			File(Path.Combine(pkg.FullPath, CreatioPackage.DescriptorName)).Should().Exist();
			File(Path.Combine(pkg.FullPath, PackageName + "." + CreatioPackage.CsprojExtension)).Should().Exist();
			File(Path.Combine(pkg.FullPath, CreatioPackage.PackageConfigName)).Should().Exist();
			File(Path.Combine(pkg.FullPath, CreatioPackage.PropertiesDirName + "\\" + CreatioPackage.AssemblyInfoName))
				.Should().Exist();
			File(Path.Combine(pkg.FullPath, "Files\\cs", "EmptyClass.cs")).Should().Exist();
			File(Path.Combine(pkg.FullPath, "Assemblies" + "\\" + CreatioPackage.PlaceholderFileName)).Should().Exist();
			File(Path.Combine(pkg.FullPath, "Data" + "\\" + CreatioPackage.PlaceholderFileName)).Should().Exist();
			File(Path.Combine(pkg.FullPath, "Resources" + "\\" + CreatioPackage.PlaceholderFileName)).Should().Exist();
			File(Path.Combine(pkg.FullPath, "Schemas" + "\\" + CreatioPackage.PlaceholderFileName)).Should().Exist();
			File(Path.Combine(pkg.FullPath, "SqlScripts" + "\\" + CreatioPackage.PlaceholderFileName)).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "Assemblies")).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "Data")).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "Resources")).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "Schemas")).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "SqlScripts")).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "Files")).Should().Exist();
			Directory(Path.Combine(pkg.FullPath, "Files\\cs")).Should().Exist();
		}

		[Test, Category("Integration")]
		public void CreatioPkg_Create_CheckCorrectTplFilePathGettingFromPath()
		{
			var oldCD = Environment.CurrentDirectory;
			var oldPath = Environment.GetEnvironmentVariable("PATH");
			Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, ResultDir);
			Environment.SetEnvironmentVariable("PATH", oldCD + ";C:\\Program Files\\dotnet");
			var pkg = CreatioPackage.CreatePackage(PackageName, Maintainer);
			pkg.Create();
			Environment.CurrentDirectory = oldCD;
			Environment.SetEnvironmentVariable("PATH", oldPath);
			var resultPath = Path.Combine(pkg.FullPath, CreatioPackage.PackageConfigName);
			var samplePath = Path.Combine(Environment.CurrentDirectory, ExpectFilesDir, CreatioPackage.PackageConfigName);
			File(resultPath).Should().Exist();
			File.ReadAllText(resultPath).Should().BeEquivalentTo(File.ReadAllText(samplePath));
		}


		[OneTimeTearDown]
		public void TeardownOneTime()
		{
			if (System.IO.Directory.Exists(ResultDir))
			{
				System.IO.Directory.Delete(ResultDir, true);
			}
		}

	}
}