using System;
using System.IO;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Confinement of the nuget2dll paths against real symbolic links. The lexical prefix check cannot see
/// them, so these run against the real file system rather than a substitute.
/// </summary>
[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class NugetMaterializerConfinementTests
{

	#region Constants: Private

	private const string PackageName = "Good";

	#endregion

	#region Fields: Private

	private string _workspaceRoot;
	private string _outsideRoot;
	private IFileSystem _fileSystem;
	private ILogger _logger;
	private IProcessExecutor _processExecutor;
	private IPropsBuilder _propsBuilder;
	private IWorkspacePathBuilder _workspacePathBuilder;
	private NugetMaterializer _sut;
	private PropsBuilder _realPropsBuilder;

	#endregion

	#region Setup/Teardown

	[SetUp]
	public void Setup(){
		string testRoot = Path.Combine(Path.GetTempPath(), "clio-1311-" + Guid.NewGuid().ToString("N"));
		_workspaceRoot = Path.Combine(testRoot, "workspace");
		_outsideRoot = Path.Combine(testRoot, "outside");
		Directory.CreateDirectory(Path.Combine(_workspaceRoot, "packages"));
		Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".nuget"));
		Directory.CreateDirectory(_outsideRoot);

		_fileSystem = new FileSystem(new System.IO.Abstractions.FileSystem());
		_logger = Substitute.For<ILogger>();
		_propsBuilder = Substitute.For<IPropsBuilder>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		_processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(_ => Task.FromResult(new ProcessExecutionResult {Started = true, ExitCode = 0}));
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_workspacePathBuilder.RootPath.Returns(_workspaceRoot);
		_workspacePathBuilder.PackagesFolderPath.Returns(Path.Combine(_workspaceRoot, "packages"));
		_workspacePathBuilder.BuildPackagePath(Arg.Any<string>())
			.Returns(ci => Path.Combine(_workspaceRoot, "packages", ci.ArgAt<string>(0)));
		_workspacePathBuilder.BuildPackageProjectPath(Arg.Any<string>())
			.Returns(ci => Path.Combine(_workspaceRoot, "packages", ci.ArgAt<string>(0),
				ci.ArgAt<string>(0) + ".csproj"));
		_workspacePathBuilder.BuildPackagePropsPath(Arg.Any<string>(), Arg.Any<string>())
			.Returns(ci => Path.Combine(_workspaceRoot, "packages", ci.ArgAt<string>(0), "Files",
				$"{ci.ArgAt<string>(0)}-{ci.ArgAt<string>(1)}.nuget.props"));
		_workspacePathBuilder.NugetFolderPath.Returns(Path.Combine(_workspaceRoot, ".nuget"));
		_sut = new NugetMaterializer(_workspacePathBuilder, _fileSystem, _logger, _processExecutor, _propsBuilder);
		_realPropsBuilder = new PropsBuilder(_fileSystem, _logger, _workspacePathBuilder);
	}

	[TearDown]
	public void TearDown(){
		string testRoot = Directory.GetParent(_workspaceRoot)?.FullName;
		if (testRoot is not null && Directory.Exists(testRoot)) {
			//Deleting a link removes the link, not what it points at.
			Directory.Delete(testRoot, true);
		}
	}

	#endregion

	#region Methods: Private

	private static void CreateDirectoryLinkOrIgnore(string path, string target){
		try {
			Directory.CreateSymbolicLink(path, target);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			Assert.Ignore("This platform does not let the test process create a symbolic link: "
				+ exception.Message);
		}
	}

	private static void CreateFileLinkOrIgnore(string path, string target){
		try {
			File.CreateSymbolicLink(path, target);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			Assert.Ignore("This platform does not let the test process create a symbolic link: "
				+ exception.Message);
		}
	}

	private static string CreateSentinel(string directoryPath){
		Directory.CreateDirectory(directoryPath);
		string sentinelPath = Path.Combine(directoryPath, "sentinel.txt");
		File.WriteAllText(sentinelPath, "must survive");
		return sentinelPath;
	}

	#endregion

	[Test]
	[Description("Refuses a package folder that is a symbolic link, so the csproj, its backup and the props "
		+ "files are never written through it into another directory (issue 1311)")]
	public void Materializer_Refuses_PackageFolderThatIsASymbolicLink(){
		// Arrange
		string victimFolder = Path.Combine(_outsideRoot, "victim");
		string sentinelPath = CreateSentinel(victimFolder);
		File.WriteAllText(Path.Combine(victimFolder, PackageName + ".csproj"), @"
			<Project Sdk=""Microsoft.NET.Sdk"">
				<ItemGroup>
					<PackageReference Include=""Nuget1"" Version=""1.1.1"" />
				</ItemGroup>
			</Project>");
		CreateDirectoryLinkOrIgnore(Path.Combine(_workspaceRoot, "packages", PackageName), victimFolder);

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(1,
			because: "the lexical prefix check passes, yet the folder resolves outside the workspace");
		File.Exists(sentinelPath).Should().BeTrue(because: "nothing outside the workspace may be touched");
		File.Exists(Path.Combine(victimFolder, PackageName + ".csproj.bak")).Should().BeFalse(
			because: "no file may be written through the link");
		_propsBuilder.DidNotReceive().Build(Arg.Any<string>());
	}

	[Test]
	[Description("Accepts a workspace whose own packages folder is a symbolic link: the root is the "
		+ "workspace's layout, not a path inside it, so the walk terminates there instead of probing it. "
		+ "Probing the root refused every conversion in that layout, which master allows (issue 1311)")]
	public void Materializer_Accepts_PackagesFolderThatIsItselfASymbolicLink(){
		// Arrange
		//A `packages` folder that is a junction onto another drive is an ordinary setup, so the whole
		//workspace is relocated behind a link and only the ROOT is a link - every segment below it is real.
		string relocatedPackages = Path.Combine(_outsideRoot, "relocated-packages");
		string packagesRoot = Path.Combine(_workspaceRoot, "packages");
		Directory.Delete(packagesRoot);
		Directory.CreateDirectory(Path.Combine(relocatedPackages, PackageName));
		File.WriteAllText(Path.Combine(relocatedPackages, PackageName, PackageName + ".csproj"), @"
			<Project Sdk=""Microsoft.NET.Sdk"">
				<ItemGroup>
					<PackageReference Include=""Nuget1"" Version=""1.1.1"" />
				</ItemGroup>
			</Project>");
		CreateDirectoryLinkOrIgnore(packagesRoot, relocatedPackages);

		// Act
		bool hasLinkWithin = _fileSystem.HasLinkWithin(packagesRoot,
			Path.Combine(packagesRoot, PackageName, PackageName + ".csproj"));

		// Assert
		hasLinkWithin.Should().BeFalse(
			because: "the confinement root terminates the walk; only the segments BELOW it are probed, "
				+ "so a linked packages folder is not itself a confinement breach");
	}

	[Test]
	[Description("Refuses a helper project folder that is a symbolic link, so the recursive bin/obj delete "
		+ "never runs outside the workspace (issue 1311)")]
	public void Materializer_Refuses_HelperFolderThatIsASymbolicLink(){
		// Arrange
		string packageFolder = Path.Combine(_workspaceRoot, "packages", PackageName);
		Directory.CreateDirectory(packageFolder);
		File.WriteAllText(Path.Combine(packageFolder, PackageName + ".csproj"), @"
			<Project Sdk=""Microsoft.NET.Sdk"">
				<ItemGroup>
					<PackageReference Include=""Nuget1"" Version=""1.1.1"" />
				</ItemGroup>
			</Project>");
		string victimFolder = Path.Combine(_outsideRoot, "victim");
		string sentinelPath = CreateSentinel(Path.Combine(victimFolder, "bin"));
		CreateDirectoryLinkOrIgnore(Path.Combine(_workspaceRoot, ".nuget", PackageName), victimFolder);

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(1, because: "the helper project resolves outside the workspace");
		File.Exists(sentinelPath).Should().BeTrue(
			because: "the recursive bin delete must not run through the link");
		_processExecutor.DidNotReceiveWithAnyArgs().ExecuteAndCaptureAsync(default);
		_propsBuilder.DidNotReceive().Build(Arg.Any<string>());
	}

	[Test]
	[Description("Refuses to write props or reconcile Libs when the package Files folder is a symbolic "
		+ "link, so the folder clearing does not empty a directory outside the workspace (issue 1311)")]
	public void PropsBuilder_Refuses_WhenPackageFilesFolderIsASymbolicLink(){
		// Arrange
		string packageFolder = Path.Combine(_workspaceRoot, "packages", PackageName);
		Directory.CreateDirectory(packageFolder);
		string victimFolder = Path.Combine(_outsideRoot, "victim");
		string sentinelPath = CreateSentinel(Path.Combine(victimFolder, "Libs", "net472"));
		CreateDirectoryLinkOrIgnore(Path.Combine(packageFolder, "Files"), victimFolder);

		//Act
		PropsBuildResult actual = _realPropsBuilder.Build(PackageName);

		//Assert
		actual.HasAnyProps.Should().BeFalse(
			because: "no props file may be written through a link out of the workspace");
		File.Exists(sentinelPath).Should().BeTrue(
			because: "the Libs reconciliation must not reach a directory outside the workspace");
	}

	[Test]
	[Description("Deletes no props file when the csproj backup is a symbolic link, so the repair never "
		+ "leaves the project importing a props file it has already removed (issue 1311)")]
	public void Repair_DeletesNothing_WhenTheCsProjBackupIsASymbolicLink(){
		// Arrange
		string packageFolder = Path.Combine(_workspaceRoot, "packages", PackageName);
		Directory.CreateDirectory(Path.Combine(packageFolder, "Files"));
		string propsFilePath = Path.Combine(packageFolder, "Files", $"{PackageName}-net472.nuget.props");
		File.WriteAllText(propsFilePath, string.Empty);
		string csprojPath = Path.Combine(packageFolder, PackageName + ".csproj");
		File.WriteAllText(csprojPath, $@"
			<Project Sdk=""Microsoft.NET.Sdk"">
				<Import Project=""{PackageName}-net472.nuget.props"" />
			</Project>");
		string victimFolder = Path.Combine(_outsideRoot, "victim");
		string sentinelPath = CreateSentinel(victimFolder);
		CreateFileLinkOrIgnore(csprojPath + ".bak", sentinelPath);

		//Act
		int actual = _sut.Materialize(PackageName);

		//Assert
		actual.Should().Be(1, because: "the csproj carries no package reference to materialize");
		File.Exists(propsFilePath).Should().BeTrue(
			because: "the repair must refuse before it deletes what it cannot then save the csproj over");
		File.ReadAllText(csprojPath).Should().Contain($"{PackageName}-net472.nuget.props",
			because: "the on-disk project must keep matching the props files that are still there");
		File.ReadAllText(sentinelPath).Should().Be("must survive",
			because: "the backup copy may not be written through a link out of the workspace");
	}

}
