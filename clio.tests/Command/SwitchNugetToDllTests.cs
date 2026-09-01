using System;
using Clio.Command;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
[Category("Unit")]
public class SwitchNugetToDllTests
{

	#region Fields: Private

	private static readonly Action<bool, IWorkspace> SetWorkspace =
		(value, ws) => ws.IsWorkspace.Returns(value);

	private static readonly ReadmeChecker ReadmeChecker = ClioTestsSetup.GetService<ReadmeChecker>();
	private SwitchNugetToDllCommand _toDllCommand;
	private readonly IWorkspace _workspace = Substitute.For<IWorkspace>();
	private readonly IWorkspacePathBuilder _workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
	private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
	private readonly ILogger _logger = Substitute.For<ILogger>();
	private readonly INugetMaterializer _nugetMaterializer = Substitute.For<INugetMaterializer>();

	#endregion

	#region Methods: Public

	[TestCase(0)]
	[TestCase(1)]
	public void Command_ShouldReturn_ResultOfMaterialization(int expectedResult){
		//Arrange
		const string packageName = "test-package";
		const string csProjFilePath = packageName + ".csproj";

		_toDllCommand = new SwitchNugetToDllCommand(_workspace, _workspacePathBuilder, _logger, _fileSystem,
			_nugetMaterializer);
		SwitchNugetToDllOptions toDllOptions = new() {
			PackageName = packageName
		};
		SetWorkspace(true, _workspace);

		_fileSystem.ExistsFile(csProjFilePath).Returns(true);
		_workspacePathBuilder.BuildPackageProjectPath(Arg.Is(packageName))
			.Returns(csProjFilePath);

		_workspace.WorkspaceSettings.Returns(new WorkspaceSettings {
			Packages = new[] {"test-package"}
		});

		_nugetMaterializer.IsPackageNameWithinPackagesFolder(Arg.Is(packageName)).Returns(true);
		_nugetMaterializer.Materialize(Arg.Is(packageName)).Returns(expectedResult);

		//Act
		int actual = _toDllCommand.Execute(toDllOptions);

		//Assert
		actual.Should().Be(expectedResult);
	}

	#endregion

	[Test]
	public void Command_ShouldExit_WhenNoCSProjectFound(){
		//Arrange
		const string packageName = "test-package";
		const string csProjFilePath = packageName + ".csproj";

		_toDllCommand = new SwitchNugetToDllCommand(_workspace, _workspacePathBuilder, _logger, _fileSystem,
			_nugetMaterializer);
		SwitchNugetToDllOptions toDllOptions = new() {
			PackageName = packageName
		};
		SetWorkspace(true, _workspace);

		_fileSystem.ExistsFile(csProjFilePath).Returns(false);
		_nugetMaterializer.IsPackageNameWithinPackagesFolder(Arg.Is(packageName)).Returns(true);

		_workspacePathBuilder.BuildPackageProjectPath(Arg.Is(packageName))
			.Returns(csProjFilePath);

		_workspace.WorkspaceSettings.Returns(new WorkspaceSettings {
			Packages = new[] {"test-package"}
		});

		//Act
		int actual = _toDllCommand.Execute(toDllOptions);

		//Assert
		actual.Should().Be(1);
		_logger.Received(1).WriteLine($"{toDllOptions.PackageName} does not contain C# projects... exiting");
	}

	[Test]
	public void Command_ShouldExit_WhenPackageIsNotDeclaredByWorkspace(){
		//Arrange
		_toDllCommand = new SwitchNugetToDllCommand(_workspace, _workspacePathBuilder, _logger, _fileSystem,
			_nugetMaterializer);
		SwitchNugetToDllOptions toDllOptions = new() {
			PackageName = "../Victim"
		};
		SetWorkspace(true, _workspace);
		_workspace.WorkspaceSettings.Returns(new WorkspaceSettings {
			Packages = new[] {"test-package"}
		});

		//Act
		int actual = _toDllCommand.Execute(toDllOptions);

		//Assert
		actual.Should().Be(1);
		_logger.Received(1).WriteLine("../Victim is not a package of this workspace... exiting");
	}

	[Test]
	public void Command_ShouldExit_BeforeProbing_WhenDeclaredPackageNameIsUnsafe(){
		//Arrange
		//A malformed workspace can declare a name that escapes the packages folder. Exact membership
		//then passes, so the containment check is the only thing standing between the name and the
		//path derivation plus the filesystem read that follows it.
		const string unsafePackageName = @"..\..\Victim";
		//The fixture's substitutes are shared by every test in it, so the "was never called"
		//assertions below need substitutes only this test has touched.
		IWorkspace workspace = Substitute.For<IWorkspace>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		INugetMaterializer nugetMaterializer = Substitute.For<INugetMaterializer>();
		SwitchNugetToDllCommand command = new(workspace, workspacePathBuilder, _logger, fileSystem,
			nugetMaterializer);
		SwitchNugetToDllOptions toDllOptions = new() {
			PackageName = unsafePackageName
		};
		SetWorkspace(true, workspace);
		workspace.WorkspaceSettings.Returns(new WorkspaceSettings {
			Packages = new[] {unsafePackageName}
		});
		nugetMaterializer.IsPackageNameWithinPackagesFolder(Arg.Is(unsafePackageName)).Returns(false);

		//Act
		int actual = command.Execute(toDllOptions);

		//Assert
		actual.Should().Be(1);
		workspacePathBuilder.DidNotReceive().BuildPackageProjectPath(Arg.Any<string>());
		fileSystem.DidNotReceive().ExistsFile(Arg.Any<string>());
		nugetMaterializer.DidNotReceive().Materialize(Arg.Any<string>());
	}

	[Test]
	public void Command_ShouldHave_DescriptionBlock_InReadmeFile() =>
		ReadmeChecker
			.IsInReadme(typeof(SwitchNugetToDllOptions))
			.Should()
			.BeTrue("{0} is a command and needs a be described in README.md", this);

	[Test]
	public void Command_ShouldNotExecute_OutsideWorkspace(){
		//Arrange
		_toDllCommand = new SwitchNugetToDllCommand(_workspace, _workspacePathBuilder, _logger, _fileSystem,
			_nugetMaterializer);
		SwitchNugetToDllOptions toDllOptions = new() {
			PackageName = "test-package"
		};
		SetWorkspace(false, _workspace);

		//Act
		int actual = _toDllCommand.Execute(toDllOptions);

		//Assert
		_logger.Received(1).WriteLine("This command cannot be run outside of a workspace");
		actual.Should().Be(1);
	}

}