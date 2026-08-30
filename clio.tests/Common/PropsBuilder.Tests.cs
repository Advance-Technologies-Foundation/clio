using System;
using System.IO;
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
		string[] files = new []{"ATF.Repository.dll", "Castle.Core.dll", $"{PackageName}.dll", "Terrasoft.Common.dll"};
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
		
		_fileSystem
			.ReadAllText(Arg.Is<string>(s=>!string.IsNullOrEmpty(s)))
			.Returns(MockCsProjWithNugetContent());
		
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
			.Returns(new[] {"ATF.Repository.dll"});
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
			.Returns(new[] {$"Contoso.{PackageName}.dll", $"{PackageName}.dll"});
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
