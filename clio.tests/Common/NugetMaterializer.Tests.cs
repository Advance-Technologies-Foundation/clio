using System;
using System.IO;
using System.Threading.Tasks;
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
		_workspacePathBuilder.PackagesFolderPath.Returns(PackagesFolderPath);
		_workspacePathBuilder.BuildPackagePath(Arg.Any<string>())
			.Returns(ci => Path.Combine(PackagesFolderPath, ci.ArgAt<string>(0)));
		_logger = Substitute.For<ILogger>();
		_fileSystem = Substitute.For<IFileSystem>();
		_processExecutor= Substitute.For<IProcessExecutor>();
		_processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(_ => Task.FromResult(SucceededRun));
		_propsBuilder = Substitute.For<IPropsBuilder>();
		_sut = new NugetMaterializer(_workspacePathBuilder, _fileSystem, _logger, _processExecutor, _propsBuilder);
	}

	[TearDown]
	public void TearDown(){
		_processExecutor?.ClearReceivedCalls();
		_fileSystem?.ClearReceivedCalls();
		_logger?.ClearReceivedCalls();
		_propsBuilder?.ClearReceivedCalls();
	}

	#endregion

	#region Constants: Private

	private const string CsprojFileName = "test-package" + ".csproj";
	private const string PackageName = "test-package";
	private const string RootPath = "root-path";
	private static readonly string PackagesFolderPath = Path.Combine(RootPath, "packages");

	//What a dotnet invocation looks like when it worked; the guards read ExitCode, not the text.
	private static readonly ProcessExecutionResult SucceededRun = new() {Started = true, ExitCode = 0};
	private static readonly ProcessExecutionResult FailedRun =
		new() {Started = true, ExitCode = 1, StandardError = "dotnet said no"};

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
		_fileSystem.Received(1).WriteAllTextToFile(Path.Combine(nugetCsprojPath), "tpl content");

		for(int i = 1; i<4; i++) {
			string command = $"add package Nuget{i} -v 1.1.{i}";
			AssertRan(command, nugetProjectFolderPath);
		}
		AssertRan($"build {PackageName}.csproj -c Release --no-incremental", nugetProjectFolderPath);

		_propsBuilder.Received(1).Build(PackageName);
		actual.Should().Be(0, because: "the package was converted successfully");
	}

	[Test]
	[Description("Rewrites the helper project and drops its previous output, so a run never reads "
		+ "back the reference set of an earlier one")]
	public void Materializer_RecreatesProj_WhenOneExists(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
		_propsBuilder.Build(PackageName).Returns(new PropsBuildResult(true, true, MaterializedNugets));
		string nugetProjectFolderPath = Path.Combine(RootPath,".nuget", PackageName);
		string nugetCsprojPath = Path.Combine(nugetProjectFolderPath, $"{PackageName}.csproj");
		_fileSystem.ExistsFile(nugetCsprojPath).Returns(true);
		_fileSystem
			.ReadAllText(Arg.Is<string>(s => s.EndsWith("NugetProject.csproj.tpl")))
			.Returns("tpl content");

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		_fileSystem.CreateDirectoryIfNotExists(nugetProjectFolderPath);
		_fileSystem.Received(1).WriteAllTextToFile(nugetCsprojPath, "tpl content");
		_fileSystem.Received(1).DeleteDirectoryIfExists(Path.Combine(nugetProjectFolderPath, "bin"));
		_fileSystem.Received(1).DeleteDirectoryIfExists(Path.Combine(nugetProjectFolderPath, "obj"));

		for(int i = 1; i<4; i++) {
			string command = $"add package Nuget{i} -v 1.1.{i}";
			AssertRan(command, nugetProjectFolderPath);

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


	[Test]
	[Description("Stops before building props when dotnet add fails, instead of converting only the "
		+ "packages that happened to resolve")]
	public void Materializer_Fails_When_AddPackageFails(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
		_processExecutor
			.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(
				o => DescribeArguments(o).StartsWith("add package Nuget2")))
			.Returns(_ => Task.FromResult(FailedRun));

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(1, because: "a dependency the helper project could not add is not converted");
		_logger.Received(1).WriteError($"Could not add the Nuget2 package to the {PackageName} "
			+ "helper project. No package reference was converted");
		AssertDidNotRun("add package Nuget3 -v 1.1.3");
		AssertDidNotRun($"build {PackageName}.csproj -c Release --no-incremental");
		_propsBuilder.DidNotReceive().Build(Arg.Any<string>());
		_fileSystem.DidNotReceive().WriteAllTextToFile(CsprojFileName, Arg.Any<string>());
	}

	[Test]
	[Description("Stops before building props when the helper project fails to build, so its stale "
		+ "output is never read back as this run's result")]
	public void Materializer_Fails_When_HelperProjectBuildFails(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName)
			.Returns(MockCsProjWithNugetContent());
		_processExecutor
			.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(o => DescribeArguments(o).StartsWith("build ")))
			.Returns(_ => Task.FromResult(FailedRun));

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(1, because: "props built after a failed build would describe the previous run");
		_logger.Received(1).WriteError($"Could not build the {PackageName} helper project. "
			+ "No package reference was converted");
		_propsBuilder.DidNotReceive().Build(Arg.Any<string>());
		_fileSystem.DidNotReceive().WriteAllTextToFile(CsprojFileName, Arg.Any<string>());
	}

	[TestCase("../Victim")]
	[TestCase(@"..\Victim")]
	[TestCase("packages/Victim")]
	[TestCase("..")]
	[TestCase("")]
	[Description("Rejects a package name that addresses a folder outside the workspace packages "
		+ "folder, before touching the filesystem")]
	public void Materializer_Rejects_PackageNameOutsidePackagesFolder(string packageName){
		//Act
		int actual = _sut.Materialize(packageName);

		//Assert
		actual.Should().Be(1, because: "the name does not address a package of this workspace");
		_fileSystem.ReceivedCalls().Should().BeEmpty("no path derived from the name may be touched");
		_processExecutor.ReceivedCalls().Should().BeEmpty();
		_propsBuilder.DidNotReceive().Build(Arg.Any<string>());
	}

	[Test]
	[Description("Rejects an absolute package name before touching the filesystem")]
	public void Materializer_Rejects_RootedPackageName(){
		//Act
		int actual = _sut.Materialize(Path.Combine(Path.GetTempPath(), "Victim"));

		//Assert
		actual.Should().Be(1, because: "an absolute name escapes the packages folder");
		_fileSystem.ReceivedCalls().Should().BeEmpty("no path derived from the name may be touched");
		_processExecutor.ReceivedCalls().Should().BeEmpty();
	}

	[TestCase("Nuget1 --source https://attacker.example/v3/index.json",
		TestName = "OptionBearingInclude_Source")]
	[TestCase("Nuget1 -s https://attacker.example/v3/index.json --interactive",
		TestName = "OptionBearingInclude_ShortSourceAndFlag")]
	[TestCase("--source https://attacker.example/v3/index.json", TestName = "OptionBearingInclude_OnlyOptions")]
	[TestCase("../../Victim", TestName = "OptionBearingInclude_RelativePath")]
	[Description("Refuses a PackageReference Include that is not a NuGet package identifier, so a project-controlled "
		+ "value cannot reach dotnet add as extra options and steer the restore at another feed")]
	public void Materializer_Rejects_IncludeThatIsNotAPackageIdentifier(string maliciousInclude){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName).Returns($@"
			<Project Sdk=""Microsoft.NET.Sdk"">
				<ItemGroup Label=""3rd Party References"">
					<PackageReference Include=""{maliciousInclude}"" Version=""1.1.1"" />
				</ItemGroup>
			</Project>");

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(1, because: "an Include carrying options is not a package and must not be restored");
		_logger.Received(1).WriteError($"The '{maliciousInclude}' PackageReference Include is not a NuGet package "
			+ $"identifier. No package reference was converted in the {PackageName} package");
		_processExecutor.DidNotReceiveWithAnyArgs().ExecuteAndCaptureAsync(default);
		// because: the value must be refused before the process starts, not merely quoted on the way in
		_propsBuilder.DidNotReceive().Build(Arg.Any<string>());
	}

	[Test]
	[Description("Passes the package identifier and version to dotnet add as separate argument tokens, leaving "
		+ "Arguments empty, so the child process cannot re-split a project-controlled value into options")]
	public void Materializer_PassesTokenizedArguments_ToDotnetAdd(){
		// Arrange
		_fileSystem.ReadAllText(CsprojFileName).Returns(MockCsProjWithNugetContent());

		//Act
		_sut.Materialize(PackageName);

		//Assert
		_processExecutor.Received(1).ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(o =>
			o.ArgumentList.Count == 5
			&& o.ArgumentList[0] == "add"
			&& o.ArgumentList[1] == "package"
			&& o.ArgumentList[2] == "Nuget1"
			&& o.ArgumentList[3] == "-v"
			&& o.ArgumentList[4] == "1.1.1"
			&& string.IsNullOrEmpty(o.Arguments)));
		// because: ProcessStartInfo throws when both Arguments and ArgumentList are set, and one interpolated
		// string is exactly what let a crafted Include become an option
	}

	#region Methods: Private

	// The command line is passed as tokens, not as one interpolated string, so the assertions describe the
	// tokens rather than reading Arguments - which is deliberately left empty when ArgumentList is used.
	private static string DescribeArguments(ProcessExecutionOptions options) =>
		string.Join(' ', options.ArgumentList ?? Array.Empty<string>());

	private void AssertRan(string arguments, string workingDirectory) =>
		_processExecutor.Received(1).ExecuteAndCaptureAsync(
			Arg.Is<ProcessExecutionOptions>(o => o.Program == "dotnet"
				&& DescribeArguments(o) == arguments
				&& o.WorkingDirectory == workingDirectory));

	private void AssertDidNotRun(string arguments) =>
		_processExecutor.DidNotReceive().ExecuteAndCaptureAsync(
			Arg.Is<ProcessExecutionOptions>(o => DescribeArguments(o) == arguments));

	#endregion

}
