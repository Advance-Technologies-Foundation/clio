using System;
using System.IO;
using System.Xml.Linq;
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
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_workspacePathBuilder.BuildPackageProjectPath(Arg.Is(PackageName)).Returns(CsprojFileName);
		_workspacePathBuilder.RootPath.Returns(RootPath);
		_workspacePathBuilder.BuildPackagePropsPath(Arg.Is(PackageName), Arg.Any<string>())
			.Returns(ci => $"{PackageName}-{ci.ArgAt<string>(1)}.nuget.props");
		_logger = Substitute.For<ILogger>();
		_fileSystem = Substitute.For<IFileSystem>();
		_processExecutor= Substitute.For<IProcessExecutor>();
		_propsBuilder = Substitute.For<IPropsBuilder>();
		_sut = new NugetMaterializer(_workspacePathBuilder, _fileSystem, _logger, _processExecutor, _propsBuilder);
	}

	[TearDown]
	public void TearDown(){
		_fileSystem?.ClearReceivedCalls();
		_logger?.ClearReceivedCalls();
		_propsBuilder?.ClearReceivedCalls();
	}

	#endregion

	#region Constants: Private

	private const string CsprojFileName = "test-package" + ".csproj";
	private const string PackageName = "test-package";
	private const string RootPath = "root-path";

	//The nuget packages declared by MockCsProjWithNugetContent, as the assemblies they produce
	private static readonly string[] MaterializedNugets = ["Nuget1", "Nuget2", "Nuget3"];

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

	//The same project after a developer edit, used to prove a second conversion re-snapshots it
	private static readonly Func<string> MockEditedCsProjWithNugetContent = () => @"
		<Project Sdk=""Microsoft.NET.Sdk"">
			<PropertyGroup>
				<TargetFramework>net472</TargetFramework>
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

	private static readonly Func<string> MockCsProjWithoutNugetButWithImports = () => $@"
		<Project Sdk=""Microsoft.NET.Sdk"">
			<PropertyGroup>
				<TargetFramework>netstandard2.0</TargetFramework>
			</PropertyGroup>
			<!--<PackageReference Include=""Nuget1"" Version=""1.1.1"" />-->
			<Import Condition=""'$(TargetFramework)' == 'net472'"" Project=""{PackageName}-net472.nuget.props"" />
			<Import Condition=""'$(TargetFramework)' == 'netstandard2.0'"" Project=""{PackageName}-netstandard.nuget.props"" />
		</Project>";

	private static readonly Func<string> MockCsProjBroken = () => @"
		<>
		";

	private ILogger _logger;
	private IWorkspacePathBuilder _workspacePathBuilder;
	private IFileSystem _fileSystem;
	private IProcessExecutor _processExecutor;
	private NugetMaterializer _sut;
	private IPropsBuilder _propsBuilder;

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
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, true, MaterializedNugets));
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
		actual.Should().Be(0, because: "the package was converted successfully");
	}

	[Test]
	[Description("Reuses the existing nuget project instead of recreating it")]
	public void Materializer_DoesNotCreateProj_WhenOneExists(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, true, MaterializedNugets));
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

		actual.Should().Be(0, because: "the package was converted successfully");
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
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(false, false, Array.Empty<string>()));

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(1,
			because: "a package whose nuget dependencies produced no dll cannot be materialized");
		_fileSystem.Received(0).CopyFile(CsprojFileName, $"{CsprojFileName}.bak", true);
		_logger.Received(1).WriteError(
			$"Could not find any dll to reference for {PackageName}. "
			+ "No package reference was converted");
	}

	[Test]
	[Description("Imports only the props files that were actually created (issue 263)")]
	public void Materializer_AddsOnlyCreatedPropsImports(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, false, MaterializedNugets));

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
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, true, MaterializedNugets));

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

	[Test]
	[Description("Treats an assembly named after the package id as materialized, as NUnit ships nunit.framework.dll (issue 263)")]
	public void Materializer_CommentsOutPackageReference_When_AssemblyNameExtendsPackageId(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
		_propsBuilder.Build(PackageName)
			.Returns(new PropsBuildResult(true, true, ["Nuget1.Core", "Nuget2", "Nuget3"]));

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(0, because: "every package reference was materialized");
		_logger.Received(0).WriteWarning(Arg.Is<string>(m => m.Contains("Keeping the Nuget1")));
	}

	[Test]
	[Description("Keeps a package reference that produced no assembly, such as an analyzer (issue 263)")]
	public void Materializer_KeepsPackageReference_When_NugetProducedNoAssembly(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, true, ["Nuget1"]));

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(0, because: "one materialized dependency is enough to convert the package");
		foreach (string keptNuget in new[] {"Nuget2", "Nuget3"}) {
			_logger.Received(1).WriteWarning(
				$"Keeping the {keptNuget} package reference in the {CsprojFileName} file, "
				+ "because it produced no assembly to reference");
		}
	}

	[Test]
	[Description("Removes an import left behind for a props file that no longer exists (issue 263)")]
	public void Materializer_RemovesStaleImport_When_PropsFileNotCreated(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithExistingImports());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, false, ["Nuget1"]));

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(0, because: "the net472 props file was created");
		_logger.Received(1).WriteInfo(
			$"Removed the {PackageName}-netstandard.nuget.props import from the {CsprojFileName} file, "
			+ "because the props file does not exist");
	}

	[Test]
	[Description("Repairs a csproj broken by an earlier clio version even when every package reference is already commented out (issue 263)")]
	public void Materializer_RepairsEmptyPropsImport_When_NoPackageReferenceLeft(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithoutNugetButWithImports());
		string net472Props = $"{PackageName}-net472.nuget.props";
		string netStandardProps = $"{PackageName}-netstandard.nuget.props";
		_fileSystem.ExistsFile(net472Props).Returns(true);
		_fileSystem.ReadAllText(net472Props).Returns(string.Empty);
		_fileSystem.ExistsFile(netStandardProps).Returns(false);

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(1, because: "there is nothing left to materialize");
		_fileSystem.Received(1).DeleteFileIfExists(net472Props);
		_logger.Received(1).WriteInfo(
			$"Removed the {PackageName}-net472.nuget.props import from the {CsprojFileName} file, "
			+ "because the props file does not exist");
		_logger.Received(1).WriteInfo(
			$"Removed the {PackageName}-netstandard.nuget.props import from the {CsprojFileName} file, "
			+ "because the props file does not exist");
	}

	[Test]
	[Description("Rewrites a utf-16 declaration as utf-8 so the produced bytes reload; WriteAllTextToFile emits UTF-8 without a BOM, and a declaration still claiming utf-16 fails XDocument.Parse (issue 263)")]
	public void Materializer_NormalizesXmlDeclaration_When_ProjectDeclaresUtf16(){
		// Arrange
		string savedCsproj = null;
		_fileSystem.ReadAllText(CsprojFileName).Returns(
			"<?xml version=\"1.0\" encoding=\"utf-16\"?>" + Environment.NewLine + MockCsProjWithNugetContent());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, true, MaterializedNugets));
		_fileSystem.When(fs => fs.WriteAllTextToFile(CsprojFileName, Arg.Any<string>()))
			.Do(ci => savedCsproj = ci.ArgAt<string>(1));

		// Act
		_sut.Materialize(PackageName);

		// Assert
		savedCsproj.Should().NotBeNull(because: "the csproj must be written through the file system");
		savedCsproj.Should().NotContain("utf-16",
			because: "the bytes are UTF-8, so a declaration claiming utf-16 describes a file that does not exist");
		savedCsproj.Should().Contain("encoding=\"utf-8\"",
			because: "the declaration must name the encoding actually written");
		Action reload = () => XDocument.Parse(savedCsproj);
		reload.Should().NotThrow(
			because: "a utf-16 declaration over UTF-8 bytes fails with 'There is no Unicode byte order mark. "
				+ "Cannot switch to Unicode.' on the next load");
	}

	[Test]
	[Description("The stale-import repair keeps the pre-conversion backup instead of copying the already-converted csproj over it, so the recovery copy survives (issue 263)")]
	public void Materializer_KeepsExistingBackup_When_RepairSavesTheCsproj(){
		// Arrange
		string backupPath = $"{CsprojFileName}.bak";
		//No PackageReference is left, so this run is the stale-import repair, not a conversion.
		_fileSystem.ReadAllText(CsprojFileName).Returns(MockCsProjWithoutNugetButWithImports());
		//A backup from the original conversion is already on disk.
		_fileSystem.ExistsFile(backupPath).Returns(true);

		// Act
		_sut.Materialize(PackageName);

		// Assert
		_fileSystem.DidNotReceive().CopyFile(CsprojFileName, backupPath, Arg.Any<bool>());
		_logger.Received().WriteInfo($"Keeping the existing csproj backup file {backupPath}");
	}

	[Test]
	[Description("A second conversion refreshes the backup, so recovery does not restore the dependencies of the first run and lose the edits made in between (issue 263)")]
	public void Materializer_RefreshesBackup_When_ConvertedAgainAfterAnEdit(){
		// Arrange
		string backupPath = $"{CsprojFileName}.bak";
		//The project is edited between the two conversions - here, its target framework changes.
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent(), MockEditedCsProjWithNugetContent());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, true, MaterializedNugets));
		//The first conversion has already left a backup behind.
		_fileSystem.ExistsFile(backupPath).Returns(true);

		// Act
		_sut.Materialize(PackageName);
		_sut.Materialize(PackageName);

		// Assert
		_fileSystem.Received(2).CopyFile(CsprojFileName, backupPath, true);
		_logger.DidNotReceive().WriteInfo($"Keeping the existing csproj backup file {backupPath}");
	}

	[Test]
	[Description("Writes a csproj that imports the created props file and comments out the converted reference (issue 263)")]
	public void Materializer_WritesExpectedCsprojXml(){
		// Arrange
		string savedCsproj = null;
		_fileSystem.ReadAllText(CsprojFileName).Returns(MockCsProjWithNugetContent());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, true, MaterializedNugets));
		_fileSystem.When(fs => fs.WriteAllTextToFile(CsprojFileName, Arg.Any<string>()))
			.Do(ci => savedCsproj = ci.ArgAt<string>(1));

		//Act
		_sut.Materialize(PackageName);

		//Assert
		savedCsproj.Should().NotBeNull(because: "the csproj must be written through the file system");
		savedCsproj.Should().Contain(
			$"<Import Condition=\"'$(TargetFramework)' == 'net472'\" Project=\"{PackageName}-net472.nuget.props\" />",
			because: "the net472 props file was created and must be imported");
		savedCsproj.Should().Contain(
			$"<Import Condition=\"'$(TargetFramework)' == 'netstandard2.0'\" Project=\"{PackageName}-netstandard.nuget.props\" />",
			because: "the netstandard props file was created and must be imported");
		savedCsproj.Should().Contain("<!--<PackageReference Include=\"Nuget1\" Version=\"1.1.1\" />-->",
			because: "a materialized package reference is replaced by a comment");
	}

	[Test]
	[Description("Never leaves an import of a props file that was not created (issue 263)")]
	public void Materializer_WritesNoImport_ForPropsFileThatWasNotCreated(){
		// Arrange
		string savedCsproj = null;
		_fileSystem.ReadAllText(CsprojFileName).Returns(MockCsProjWithExistingImports());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, false, MaterializedNugets));
		_fileSystem.When(fs => fs.WriteAllTextToFile(CsprojFileName, Arg.Any<string>()))
			.Do(ci => savedCsproj = ci.ArgAt<string>(1));

		//Act
		_sut.Materialize(PackageName);

		//Assert
		savedCsproj.Should().NotContain($"{PackageName}-netstandard.nuget.props",
			because: "importing a props file that does not exist fails the whole project with MSB4019");
		savedCsproj.Should().Contain($"{PackageName}-net472.nuget.props",
			because: "the net472 props file was created and its import must stay");
	}

	[Test]
	[Description("Removes stale imports when nothing could be materialized at all (issue 263)")]
	public void Materializer_RemovesStaleImports_When_NothingMaterialized(){
		// Arrange
		string savedCsproj = null;
		_fileSystem.ReadAllText(CsprojFileName).Returns(MockCsProjWithExistingImports());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(false, false, Array.Empty<string>()));
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(false);
		_fileSystem.When(fs => fs.WriteAllTextToFile(CsprojFileName, Arg.Any<string>()))
			.Do(ci => savedCsproj = ci.ArgAt<string>(1));

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(1, because: "no package reference could be converted");
		savedCsproj.Should().NotBeNull(because: "the stale imports had to be removed");
		savedCsproj.Should().NotContain(".nuget.props",
			because: "both props files are gone, so neither import may survive");
	}

	[Test]
	[Description("Keeps a usable props file and its import untouched while repairing (issue 263)")]
	public void Materializer_KeepsUsablePropsFile_WhileRepairing(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName).Returns(MockCsProjWithoutNugetButWithImports());
		string net472Props = $"{PackageName}-net472.nuget.props";
		string netStandardProps = $"{PackageName}-netstandard.nuget.props";
		_fileSystem.ExistsFile(net472Props).Returns(true);
		_fileSystem.ReadAllText(net472Props).Returns("<Project></Project>");
		_fileSystem.ExistsFile(netStandardProps).Returns(false);

		//Act
		_sut.Materialize(PackageName);

		//Assert
		_fileSystem.Received(0).DeleteFileIfExists(net472Props);
		_logger.Received(0).WriteInfo(Arg.Is<string>(m => m.Contains($"Removed the {net472Props}")));
		_logger.Received(1).WriteInfo(
			$"Removed the {netStandardProps} import from the {CsprojFileName} file, "
			+ "because the props file does not exist");
	}

}
