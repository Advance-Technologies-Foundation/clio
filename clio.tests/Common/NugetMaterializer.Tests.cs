using System;
using System.IO;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
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
	public void Materializer_CreatesProject_WhenProjDoesNotExist(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
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
	public void Materializer_DoesNotCreateProj_WhenOneExists(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
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

}