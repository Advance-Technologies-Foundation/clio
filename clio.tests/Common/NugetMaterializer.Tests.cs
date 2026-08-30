using System;
using System.IO;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class NugetMaterializerTests
{

	#region Setup/Teardown

	[SetUp]
	public void Setup(){
		_logger = Substitute.For<ILogger>();
		_fileSystem = Substitute.For<IFileSystem>();
		_processExecutor= Substitute.For<IProcessExecutor>();
		_propsBuilder = Substitute.For<IPropsBuilder>();
		_sut = new NugetMaterializer(_workspacePathBuilder, _fileSystem, _logger, _processExecutor, _propsBuilder);
	}

	#endregion

	#region Constants: Private

	private const string CsprojFileName = "test-package" + ".csproj";
	private const string PackageName = "test-package";
	private const string RootPath = "root-path";

	#endregion

	#region Fields: Private

	private static readonly Func<string> MockEmptyXmlContent = () => string.Empty;

	private static readonly Func<string> MockCsProjWithoutNugetContent = () => @"
		<Project Sdk=""Microsoft.NET.Sdk"">
			<PropertyGroup>
				<TargetFramework>netstandard2.0</TargetFramework>
			</PropertyGroup>
		</Project>";

	private static readonly Func<string> MockCsProjWithNugetContent = () => @"
		<Project Sdk=""Microsoft.NET.Sdk"">
			<PropertyGroup>
				<TargetFramework>netstandard2.0</TargetFramework>
			</PropertyGroup>
			<ItemGroup Label=""3rd Party References"">
				<PackageReference Include=""Nuget1"" Version=""1.1.1"" />
				<PackageReference Include=""Nuget2"" Version=""1.1.2"" />
				<PackageReference Include=""Nuget3"" Version=""1.1.3"" />
			</ItemGroup>
		</Project>";

	private static readonly Func<string> MockCsProjWithExistingImports = () => $@"
		<Project Sdk=""Microsoft.NET.Sdk"">
			<PropertyGroup>
				<TargetFramework>netstandard2.0</TargetFramework>
			</PropertyGroup>
			<ItemGroup Label=""3rd Party References"">
				<PackageReference Include=""Nuget1"" Version=""1.1.1"" />
			</ItemGroup>
			<Import Condition=""'$(TargetFramework)' == 'net472'"" Project=""{PackageName}-net472.nuget.props"" />
			<Import Condition=""'$(TargetFramework)' == 'netstandard2.0'"" Project=""{PackageName}-netstandard.nuget.props"" />
		</Project>";

	private static readonly Func<string> MockCsProjBroken = () => @"
		<>
		";

	private ILogger _logger;
	private readonly IWorkspacePathBuilder _workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
	private IFileSystem _fileSystem;
	private IProcessExecutor _processExecutor;
	private NugetMaterializer _sut;
	private IPropsBuilder _propsBuilder;

	#endregion

	#region Constructors: Public

	public NugetMaterializerTests(){
		_workspacePathBuilder.BuildPackageProjectPath(Arg.Is(PackageName)).Returns(CsprojFileName);
		_workspacePathBuilder.RootPath.Returns(RootPath);
	}

	#endregion

	[Test]
	[Description("Reports a parse error and does not build props when the csproj cannot be parsed")]
	public void Materializer_ExistsWithMessage_When_CsprojIsBroken(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjBroken());

		//Act
		_sut.Materialize(PackageName);

		//Assert
		_logger.Received(1)
			.WriteWarning($"Could not find any PackageReference references in the {CsprojFileName} file");

		_logger.Received(1)
			.WriteError($"Could not parse {CsprojFileName} file");
		_propsBuilder.Received(0).Build(PackageName);
	}

	[Test]
	[Description("Reports a warning and does not build props when the csproj has no PackageReference")]
	public void Materializer_ExistsWithMessage_When_NoNugetDetected(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithoutNugetContent());

		//Act
		_sut.Materialize(PackageName);

		//Assert
		_logger.Received(1)
			.WriteWarning($"Could not find any PackageReference references in the {CsprojFileName} file");
		_propsBuilder.Received(0).Build(PackageName);
	}

	[Test]
	[Description("Materializes nuget references and creates the nuget project when it does not exist yet")]
	public void Materializer_CreatesProject_WhenProjDoesNotExist(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, true));
		string nugetProjectFolderPath = Path.Combine(RootPath,".nuget", PackageName);
		string nugetCsprojPath = Path.Combine(nugetProjectFolderPath, $"{PackageName}.csproj");
		_fileSystem.ExistsFile(nugetCsprojPath).Returns(false);
		_fileSystem
			.ReadAllText(Arg.Is<string>(s => s.EndsWith("NugetProject.csproj.tpl")))
			.Returns("tpl content");
		
		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		_fileSystem.CreateDirectoryIfNotExists(nugetProjectFolderPath);
		_fileSystem.Received(1).ReadAllText(Arg.Is<string>(s => s.EndsWith("NugetProject.csproj.tpl")));
		_fileSystem.Received(1).ExistsFile(nugetCsprojPath);
		_fileSystem.Received(1).WriteAllTextToFile(Path.Combine(nugetCsprojPath), "tpl content");
		
		for(int i = 1; i<4; i++) {
			string command = $"add package Nuget{i} -v 1.1.{i}";
			_processExecutor.Received(1).Execute(
				Arg.Is("dotnet"),
				Arg.Is(command),
				Arg.Is(true),
				Arg.Is(nugetProjectFolderPath),
				Arg.Is(false)
			);
		}
		_processExecutor.Received(1).Execute(
			Arg.Is("dotnet"),
			Arg.Is($"build {PackageName}.csproj -c Release --no-incremental"),
			Arg.Is(true),
			Arg.Is(nugetProjectFolderPath),
			Arg.Is(false)
		);
		
		_propsBuilder.Received(1).Build(PackageName);
		actual.Should().Be(0);
	}

	[Test]
	[Description("Reuses the existing nuget project instead of recreating it")]
	public void Materializer_DoesNotCreateProj_WhenOneExists(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, true));
		string nugetProjectFolderPath = Path.Combine(RootPath,".nuget", PackageName);
		string nugetCsprojPath = Path.Combine(nugetProjectFolderPath, $"{PackageName}.csproj");
		_fileSystem.ExistsFile(nugetCsprojPath).Returns(true);
		
		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		_fileSystem.CreateDirectoryIfNotExists(nugetProjectFolderPath);
		_fileSystem.Received(1).ExistsFile(nugetCsprojPath);
		
		for(int i = 1; i<4; i++) {
			string command = $"add package Nuget{i} -v 1.1.{i}";
			_processExecutor.Received(1).Execute(
				Arg.Is("dotnet"),
				Arg.Is(command),
				Arg.Is(true),
				Arg.Is(nugetProjectFolderPath),
				Arg.Is(false)
			);
			
		}

		actual.Should().Be(0);
		_propsBuilder.Received(1).Build(PackageName);
	}

	[Test]
	[Description("Reports an error when the csproj file is empty")]
	public void Materializer_ThrowsException_WhenCsprojFileIsEmpty(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockEmptyXmlContent());

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		_logger.Received(1)
			.WriteError($"{CsprojFileName} file is empty");
		actual.Should().Be(1);
		_propsBuilder.Received(0).Build(PackageName);
	}

	[Test]
	[Description("Leaves the csproj untouched and fails when no props file could be created (issue 263)")]
	public void Materializer_DoesNotTouchCsproj_When_NoPropsFileCreated(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(false, false));

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(1,
			because: "a package whose nuget dependencies produced no dll cannot be materialized");
		_fileSystem.Received(0).CopyFile(CsprojFileName, $"{CsprojFileName}.bak", true);
		_logger.Received(1).WriteError(
			$"Could not find any dll to reference for {PackageName}. "
			+ $"The {CsprojFileName} file was left unchanged");
	}

	[Test]
	[Description("Imports only the props files that were actually created (issue 263)")]
	public void Materializer_AddsOnlyCreatedPropsImports(){
		// Arrange
		string savedCsproj = null;
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, false));
		_fileSystem.When(fs => fs.WriteAllTextToFile(CsprojFileName, Arg.Any<string>()))
			.Do(ci => savedCsproj = ci.ArgAt<string>(1));

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(0, because: "one usable props file is enough to materialize the package");
		_logger.Received(1).WriteWarning(
			$"Skipping {PackageName}-netstandard.nuget.props import in the {CsprojFileName} file, "
			+ "because the props file was not created");
	}

	[Test]
	[Description("Does not duplicate an existing props import and reports it as information, not as a failure (issue 263)")]
	public void Materializer_DoesNotDuplicateExistingImport(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithExistingImports());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, true));

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(0, because: "existing imports are a valid state, not an error");
		_logger.Received(1).WriteInfo(
			$"{PackageName}-net472.nuget.props import already exists in the {CsprojFileName} file, skipping");
		_logger.Received(1).WriteInfo(
			$"{PackageName}-netstandard.nuget.props import already exists in the {CsprojFileName} file, skipping");
		_logger.Received(0).WriteWarning(Arg.Is<string>(m => m.Contains("Could not add")));
	}

}
