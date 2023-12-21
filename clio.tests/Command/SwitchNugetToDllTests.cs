using System;
using Clio.Command;
using Clio.Common;
using Clio.Workspace;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
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