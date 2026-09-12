using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Common;
using FluentAssertions;
using Clio.Workspaces;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class PropsBuilder_Tests
{

	private const string RootPath = "rootPath";
	private const string NugetFolderPath = ".nuget";
	private const string PackageFolderPath = "packages";
	private const string PackageName = "testPackage";
	private const string MockPropItemTemplate = @"<Reference Include=""#dll-name-here#"">
	<HintPath>Libs/#dll-name-here#.dll</HintPath>
</Reference>";

	private static readonly Func<string> MockCsProjWithNugetContent = () => @"
		<Project Sdk=""Microsoft.NET.Sdk"">
			<PropertyGroup>
				<TargetFramework>netstandard2.0</TargetFramework>
			</PropertyGroup>
			<ItemGroup Label=""Core References"">
				<Reference Include=""Terrasoft.Common"">
					<HintPath>$(CoreLibPath)/Terrasoft.Common.dll</HintPath>
					<SpecificVersion>False</SpecificVersion>
					<Private>False</Private>
				</Reference>
			</ItemGroup>
			<ItemGroup Label=""3rd Party References"">
				<PackageReference Include=""ATF.Repository"" Version=""2.0.1.5"" />
			</ItemGroup>
		</Project>";
	#region Setup/Teardown

	[SetUp]
	public void SetUp(){
		_fileSystem = Substitute.For<IFileSystem>();
		_fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(true);
		_logger = Substitute.For<ILogger>();
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		
		_workspacePathBuilder.RootPath.Returns(RootPath);
		_workspacePathBuilder.NugetFolderPath.Returns(Path.Combine(RootPath, NugetFolderPath));
		_workspacePathBuilder.PackagesFolderPath.Returns(Path.Combine(RootPath, PackageFolderPath));
		_workspacePathBuilder.BuildPackagePropsPath(Arg.Is(PackageName), Arg.Any<string>())
			.Returns(ci => ExpectedPropsPath(ci.ArgAt<string>(1)));
		_workspacePathBuilder.BuildPackageProjectPath(Arg.Is(PackageName))
			.Returns(Path.Combine(RootPath, PackageFolderPath, PackageName, PackageName + ".csproj"));
		_sut = new PropsBuilder(_fileSystem, _logger, _workspacePathBuilder);
	}

	#endregion

	#region Fields: Private

	private PropsBuilder _sut;
	private IFileSystem _fileSystem;
	private ILogger _logger;
	private IWorkspacePathBuilder _workspacePathBuilder;

	#endregion

	[Test]
	[Description("Reads the dlls of both monikers from the nuget bin folders")]
	public void Build_ReadsDllsOfBothMonikers(){
		//Arrange
		string[] files = ["ATF.Repository.dll", "Castle.Core.dll", $"{PackageName}.dll", "Terrasoft.Common.dll"];
		_fileSystem.GetFiles(
			Arg.Is(ExpectedPath("net472")),
			Arg.Is("*.dll"), 
			Arg.Is(SearchOption.TopDirectoryOnly)
		).Returns(files);
		_fileSystem.GetFiles(
			Arg.Is(ExpectedPath("netstandard")),
			Arg.Is("*.dll"), 
			Arg.Is(SearchOption.TopDirectoryOnly)
		).Returns(files);
		
		MockCsProjAndTemplateReads();
		
		//Act
		_sut.Build(PackageName);

		//Assert
		_fileSystem.Received(1).GetFiles(
			Arg.Is(ExpectedPath("net472")),
			Arg.Is("*.dll"), 
			Arg.Is(SearchOption.TopDirectoryOnly)
			);
		
		_fileSystem.Received(1).GetFiles(
			Arg.Is(ExpectedPath("netstandard")),
			Arg.Is("*.dll"), 
			Arg.Is(SearchOption.TopDirectoryOnly)
			);
		
		return;

		
		//rootPath\.nuget\testPackage\bin\net472
		string ExpectedPath(string moniker) => Path.Combine(RootPath, NugetFolderPath, PackageName, "bin", moniker);
	}

	[Test]
	[Description("Reports no props and does not throw when the nuget bin folder does not exist (issue 263)")]
	public void Build_ReturnsNoProps_When_BinFolderMissing(){
		//Arrange
		_fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(false);

		//Act
		PropsBuildResult actual = _sut.Build(PackageName);

		//Assert
		actual.HasAnyProps.Should().BeFalse(
			because: "a nuget project that failed to build produces no bin folder to read");
		_fileSystem.Received(0).GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>());
	}

	[Test]
	[Description("Does not write a props file when the moniker has no dependency dll (issue 263)")]
	public void Build_DoesNotWritePropsFile_When_NoDllsFound(){
		//Arrange
		_fileSystem.GetFiles(Arg.Any<string>(), Arg.Is("*.dll"), Arg.Is(SearchOption.TopDirectoryOnly))
			.Returns(Array.Empty<string>());

		//Act
		PropsBuildResult actual = _sut.Build(PackageName);

		//Assert
		actual.Net472PropsCreated.Should().BeFalse(
			because: "there is nothing to reference for net472");
		actual.NetStandardPropsCreated.Should().BeFalse(
			because: "there is nothing to reference for netstandard");
		actual.HasAnyProps.Should().BeFalse(because: "no props file was written at all");
		_fileSystem.Received(0).WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
		_fileSystem.Received(1).DeleteFileIfExists(Arg.Is(ExpectedPropsPath("net472")));
		_fileSystem.Received(1).DeleteFileIfExists(Arg.Is(ExpectedPropsPath("netstandard")));
	}

	[Test]
	[Description("Reports which props files were written when only one moniker has dlls (issue 263)")]
	public void Build_ReportsPerMonikerResult_When_OnlyOneMonikerHasDlls(){
		//Arrange
		_fileSystem.GetFiles(Arg.Is(ExpectedBinPath("net472")), Arg.Is("*.dll"),
				Arg.Is(SearchOption.TopDirectoryOnly))
			.Returns(new[] {Path.Combine(ExpectedBinPath("net472"), "ATF.Repository.dll")});
		_fileSystem.GetFiles(Arg.Is(ExpectedBinPath("netstandard")), Arg.Is("*.dll"),
				Arg.Is(SearchOption.TopDirectoryOnly))
			.Returns(Array.Empty<string>());
		MockCsProjAndTemplateReads();

		//Act
		PropsBuildResult actual = _sut.Build(PackageName);

		//Assert
		actual.Net472PropsCreated.Should().BeTrue(because: "net472 has a dependency dll");
		actual.NetStandardPropsCreated.Should().BeFalse(because: "netstandard has none");
		actual.HasAnyProps.Should().BeTrue(because: "one props file was written");
		actual.MaterializedAssemblies.Should().Contain("ATF.Repository",
			because: "the copied assembly must be reported so its package reference can be commented out");
		_fileSystem.Received(1).WriteAllTextToFile(
			Arg.Is(ExpectedPropsPath("net472")),
			Arg.Is<string>(c => c.Contains("<Project>") && c.Contains("ATF.Repository")));
		_fileSystem.Received(0).WriteAllTextToFile(Arg.Is(ExpectedPropsPath("netstandard")), Arg.Any<string>());
	}

	[Test]
	[Description("Keeps a dependency whose file name merely ends with the package name (issue 263)")]
	public void Build_KeepsDependency_WhoseNameEndsWithPackageName(){
		//Arrange
		_fileSystem.GetFiles(Arg.Any<string>(), Arg.Is("*.dll"), Arg.Is(SearchOption.TopDirectoryOnly))
			.Returns(new[] {
				Path.Combine(ExpectedBinPath("net472"), $"Contoso.{PackageName}.dll"),
				Path.Combine(ExpectedBinPath("net472"), $"{PackageName}.dll")
			});
		MockCsProjAndTemplateReads();

		//Act
		_sut.Build(PackageName);

		//Assert
		_fileSystem.Received(1).WriteAllTextToFile(
			Arg.Is(ExpectedPropsPath("net472")),
			Arg.Is<string>(c => c.Contains($"Contoso.{PackageName}")));
		_fileSystem.Received(0).WriteAllTextToFile(
			Arg.Any<string>(),
			Arg.Is<string>(c => c.Contains($"Include=\"{PackageName}\"")));
	}

	[Test]
	[Description("Removes from Files/Libs/<moniker> exactly the assemblies the previous props file "
		+ "declared when a later run leaves that moniker without dependencies, so a stale dll stops "
		+ "shipping in the Creatio package while a hand-placed dll is left alone (issue 1311)")]
	public void Build_RemovesPreviouslyMaterializedAssemblies_When_MonikerLosesItsLastDependency(){
		//Arrange
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "clio-1311-props");
		MockFileSystem mockFileSystem = new();
		IFileSystem realFileSystem = new FileSystem(mockFileSystem);
		IWorkspacePathBuilder pathBuilder = BuildPathBuilderFor(workspaceRoot);
		PropsBuilder sut = new(realFileSystem, _logger, pathBuilder);
		string net472BinDir = Path.Combine(workspaceRoot, NugetFolderPath, PackageName, "bin", "net472");
		string netStandardBinDir = Path.Combine(workspaceRoot, NugetFolderPath, PackageName, "bin", "netstandard");
		string net472LibsDir = Path.Combine(workspaceRoot, PackageFolderPath, PackageName, "Files", "Libs", "net472");
		string materializedDll = Path.Combine(net472LibsDir, "Castle.Core.dll");
		string handPlacedDll = Path.Combine(net472LibsDir, "Contoso.HandPlaced.dll");
		foreach (string moniker in new[] {"net472", "netstandard"}) {
			mockFileSystem.AddFile(
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tpl", $"propItem-{moniker}.xml.tpl"),
				new MockFileData(MockPropItemTemplate));
		}
		mockFileSystem.AddFile(
			Path.Combine(workspaceRoot, PackageFolderPath, PackageName, PackageName + ".csproj"),
			new MockFileData(MockCsProjWithNugetContent()));
		mockFileSystem.AddFile(Path.Combine(net472BinDir, "Castle.Core.dll"), new MockFileData("dll"));
		mockFileSystem.AddDirectory(netStandardBinDir);

		//Act
		sut.Build(PackageName);
		bool materializedByFirstRun = mockFileSystem.File.Exists(materializedDll);
		//A dll the developer put into Libs by hand and references through its own HintPath.
		mockFileSystem.AddFile(handPlacedDll, new MockFileData("dll"));
		//The dependency is dropped from the package, so the helper project builds nothing for net472.
		mockFileSystem.File.Delete(Path.Combine(net472BinDir, "Castle.Core.dll"));
		PropsBuildResult secondRun = sut.Build(PackageName);

		//Assert
		materializedByFirstRun.Should().BeTrue(
			because: "the first run must copy the dependency into the package Libs folder");
		secondRun.HasAnyProps.Should().BeFalse(
			because: "neither moniker has a dependency left to reference");
		mockFileSystem.File.Exists(materializedDll).Should().BeFalse(
			because: "its import is gone, so the assembly must not stay in the deployed Creatio package");
		mockFileSystem.File.Exists(handPlacedDll).Should().BeTrue(
			because: "the folder also holds dlls this command never materialized");
	}

	//This one test needs real copy, clear and delete semantics, which a substituted IFileSystem
	//cannot express, so it drives the production FileSystem over an in-memory one.
	private static IWorkspacePathBuilder BuildPathBuilderFor(string workspaceRoot){
		IWorkspacePathBuilder pathBuilder = Substitute.For<IWorkspacePathBuilder>();
		pathBuilder.RootPath.Returns(workspaceRoot);
		pathBuilder.NugetFolderPath.Returns(Path.Combine(workspaceRoot, NugetFolderPath));
		pathBuilder.PackagesFolderPath.Returns(Path.Combine(workspaceRoot, PackageFolderPath));
		pathBuilder.BuildPackageProjectPath(Arg.Is(PackageName))
			.Returns(Path.Combine(workspaceRoot, PackageFolderPath, PackageName, PackageName + ".csproj"));
		pathBuilder.BuildPackagePropsPath(Arg.Is(PackageName), Arg.Any<string>())
			.Returns(ci => Path.Combine(workspaceRoot, PackageFolderPath, PackageName, "Files",
				$"{PackageName}-{ci.ArgAt<string>(1)}.nuget.props"));
		return pathBuilder;
	}

	private void MockCsProjAndTemplateReads(){
		_fileSystem.ReadAllText(Arg.Is<string>(s => s.EndsWith(".tpl")))
			.Returns(MockPropItemTemplate);
		_fileSystem.ReadAllText(Arg.Is<string>(s => s.EndsWith(".csproj")))
			.Returns(MockCsProjWithNugetContent());
	}

	//rootPath\.nuget\testPackage\bin\<moniker>
	private static string ExpectedBinPath(string moniker) =>
		Path.Combine(RootPath, NugetFolderPath, PackageName, "bin", moniker);

	//rootPath\packages\testPackage\Files\testPackage-<moniker>.nuget.props
	private static string ExpectedPropsPath(string moniker) =>
		Path.Combine(RootPath, PackageFolderPath, PackageName, "Files",
			$"{PackageName}-{moniker}.nuget.props");

}
