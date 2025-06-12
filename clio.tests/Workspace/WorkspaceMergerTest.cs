using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Common;
using Clio.Package;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Workspace;

[TestFixture]
internal class WorkspaceMergerTest {

	#region Setup/Teardown

	#region SetUp and TearDown

	[SetUp]
	public void SetUp() {
		SetupDefaultMocks();
	}

	#endregion

	#endregion

	#region Fields: Private

	private IWorkspacePathBuilder _workspacePathBuilder;
	private IPackageArchiver _packageArchiver;
	private IPackageInstaller _packageInstaller;
	private IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private MockFileSystem _fileSystem;
	private IFileSystem _clioFileSystemMock;
	private ILogger _logger;
	private EnvironmentSettings _testEnvironmentSettings;
	private WorkspaceMerger _workspaceMerger;

	#endregion

	#region Methods: Private

	private void SetupDefaultMocks() {
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_packageArchiver = Substitute.For<IPackageArchiver>();
		_packageInstaller = Substitute.For<IPackageInstaller>();
		_workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		_fileSystem = new MockFileSystem();
		_clioFileSystemMock = new FileSystem(_fileSystem);
		_logger = Substitute.For<ILogger>();

		_testEnvironmentSettings = new EnvironmentSettings {
			Uri = "https://test-environment.creatio.com/",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		};

		// Setup workspace path builder behavior
		_workspacePathBuilder.PackagesFolderPath.Returns("/path/to/packages");

		// Setup working directories provider to execute action with a temp directory
		_workingDirectoriesProvider
			.When(w => w.CreateTempDirectory(Arg.Any<Action<string>>()))
			.Do(callInfo => {
				Action<string> action = callInfo.ArgAt<Action<string>>(0);
				action("/temp");
			});

		_workspaceMerger = new WorkspaceMerger(_testEnvironmentSettings,
			_workspacePathBuilder,
			_packageArchiver,
			_packageInstaller,
			_workingDirectoriesProvider,
			_clioFileSystemMock,
			_logger);
	}

	#endregion

	#region Test Methods

	[Test]
	public void MergeAndInstall_WithValidWorkspaces_ShouldCallPackageInstallerWithMergedZip() {
		// Arrange
		string[] workspacePaths = ["/path/to/workspace1", "/path/to/workspace2"];
		const string tempDir = "/temp";
		string resultZipPath = Path.Combine(tempDir, "MergedCreatioPackages.zip");

		// Setup sequence of RootPath calls
		_workspacePathBuilder.RootPath.Returns(workspacePaths[0], workspacePaths[1]);

		// Setup sequence of PackagesFolderPath calls
		_workspacePathBuilder.PackagesFolderPath
							.Returns(Path.Combine(workspacePaths[0], "packages"),
								Path.Combine(workspacePaths[1], "packages"));

		foreach (string workspacePath in workspacePaths) {
			string packagesPath = Path.Combine(workspacePath, "packages");
			_fileSystem.Directory.CreateDirectory(workspacePath);
			_fileSystem.Directory.CreateDirectory(packagesPath);

			for (int i = 1; i <= 4; i++) {
				string packageDir = Path.Combine(packagesPath, $"testpkg_{i}");
				_fileSystem.Directory.CreateDirectory(packageDir);
			}
		}

		// Act
		_workspaceMerger.MergeAndInstall(workspacePaths);

		// Assert
		_packageInstaller.Received(1).Install(resultZipPath, _testEnvironmentSettings, null);
	}

	[Test]
	public void MergeAndInstall_WithSkipBackup_ShouldPassOptionToPackageInstaller() {
		// Arrange
		string[] workspacePaths = ["/path/to/workspace1", "/path/to/workspace2"];
		const string tempDir = "/temp";
		string resultZipPath = Path.Combine(tempDir, "MergedCreatioPackages.zip");

		// Setup sequence of RootPath calls
		_workspacePathBuilder.RootPath.Returns(workspacePaths[0], workspacePaths[1]);

		// Setup sequence of PackagesFolderPath calls
		_workspacePathBuilder.PackagesFolderPath
							.Returns(Path.Combine(workspacePaths[0], "packages"),
								Path.Combine(workspacePaths[1], "packages"));

		foreach (string workspacePath in workspacePaths) {
			string packagesPath = Path.Combine(workspacePath, "packages");
			_fileSystem.Directory.CreateDirectory(workspacePath);
			_fileSystem.Directory.CreateDirectory(packagesPath);

			for (int i = 1; i <= 4; i++) {
				string packageDir = Path.Combine(packagesPath, $"testpkg_{i}");
				_fileSystem.Directory.CreateDirectory(packageDir);
			}
		}

		// Act
		_workspaceMerger.MergeAndInstall(workspacePaths, skipBackup: true);

		// Assert
		_packageInstaller.Received(1).Install(
			Arg.Is<string>(s => s == resultZipPath),
			Arg.Is<EnvironmentSettings>(e => e == _testEnvironmentSettings),
			Arg.Is<PackageInstallOptions>(o => o != null && o.SkipBackup == true));
	}

	[Test]
	public void MergeAndInstall_WithDuplicatePackages_ShouldSkipDuplicates() {
		// Arrange
		string[] workspacePaths = ["/path/to/workspace1", "/path/to/workspace2"];
		foreach (string workspacePath in workspacePaths) {
			string packagesPath = Path.Combine(workspacePath, "packages");
			_fileSystem.Directory.CreateDirectory(workspacePath);
			_fileSystem.Directory.CreateDirectory(packagesPath);
		}

		_workspacePathBuilder.RootPath
							.Returns(workspacePaths[0], workspacePaths[1]);

		_workspacePathBuilder.PackagesFolderPath
							.Returns(Path.Combine(workspacePaths[0], "packages"),
								Path.Combine(workspacePaths[1], "packages"));

		// Set up common and unique package directories
		const string commonDir = "Common";
		const string unique1Dir = "Unique1";
		const string unique2Dir = "Unique2";

		_fileSystem.Directory.CreateDirectory(Path.Combine(workspacePaths[0], "packages", commonDir));
		_fileSystem.Directory.CreateDirectory(Path.Combine(workspacePaths[0], "packages", unique1Dir));
		_fileSystem.Directory.CreateDirectory(Path.Combine(workspacePaths[1], "packages", commonDir));
		_fileSystem.Directory.CreateDirectory(Path.Combine(workspacePaths[1], "packages", unique2Dir));

		// Act
		_workspaceMerger.MergeAndInstall(workspacePaths);

		// Assert
		// Verify that Pack is called exactly 3 times - once for each unique package
		_packageArchiver.Received(3).Pack(Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Is<bool>(x => x == true),
			Arg.Is<bool>(x => x == true));

		// Verify warning for duplicate package was logged
		_logger.Received(1).WriteWarning(Arg.Is<string>(s => s.Contains("already processed")));
		
		// Verify package installer is called with null options
		_packageInstaller.Received(1).Install(
			Arg.Any<string>(), 
			_testEnvironmentSettings, 
			null);
	}

	[Test]
	public void MergeToZip_WithValidWorkspaces_ReturnsCorrectZipPath() {
		// Arrange
		string[] workspacePaths = ["/path/to/workspace1", "/path/to/workspace2"];
		const string outputPath = "/output";
		const string zipFileName = "TestMergedPackages";
		string expectedResultPath = Path.Combine(outputPath, $"{zipFileName}.zip");

		_workspacePathBuilder.RootPath
							.Returns(workspacePaths[0], workspacePaths[1]);

		_workspacePathBuilder.PackagesFolderPath
							.Returns(Path.Combine(workspacePaths[0], "packages"),
								Path.Combine(workspacePaths[1], "packages"));

		// Setup filesystem
		foreach (string workspacePath in workspacePaths) {
			string packagesPath = Path.Combine(workspacePath, "packages");
			_fileSystem.Directory.CreateDirectory(workspacePath);
			_fileSystem.Directory.CreateDirectory(packagesPath);

			// Add test package to each workspace
			string packageDir = Path.Combine(packagesPath, "TestPackage");
			_fileSystem.Directory.CreateDirectory(packageDir);
		}

		// Act
		_packageArchiver.When(w => w.ZipPackages(Arg.Any<string>(), Arg.Any<string>(), true))
						.Do(callInfo => { _clioFileSystemMock.CreateFile(callInfo.ArgAt<string>(1)); });
		string result = _workspaceMerger.MergeToZip(workspacePaths, outputPath, zipFileName);

		// Assert
		result.Should().Be(expectedResultPath);
		_fileSystem.File.Exists(expectedResultPath);
	}

	[Test]
	public void MergeAndInstall_WithNoWorkspacePaths_ThrowsArgumentException() {
		// Arrange
		string[] emptyWorkspacePaths = Array.Empty<string>();

		// Act & Assert
		Assert.Throws<ArgumentException>(() =>
			_workspaceMerger.MergeAndInstall(emptyWorkspacePaths));
	}

	[Test]
	public void MergeAndInstall_WithNonExistingWorkspace_ThrowsDirectoryNotFoundException() {
		// Arrange
		string[] workspacePaths = new[] {"/path/to/nonexistent"};

		// Act & Assert
		Action act = () => _workspaceMerger.MergeAndInstall(workspacePaths);
		act.Should().Throw<DirectoryNotFoundException>();
	}

	[Test]
	public void MergeToZip_WithNoPackagesFound_ThrowsInvalidOperationException() {
		// Arrange
		string[] workspacePaths = ["/path/to/workspace1", "/path/to/workspace2"];
		const string outputPath = "/output";
		const string zipFileName = "TestMergedPackages";

		_workspacePathBuilder.RootPath
							.Returns(workspacePaths[0], workspacePaths[1]);

		_workspacePathBuilder.PackagesFolderPath
							.Returns(Path.Combine(workspacePaths[0], "packages"),
								Path.Combine(workspacePaths[1], "packages"));

		// Setup filesystem with empty packages directories
		foreach (string workspacePath in workspacePaths) {
			string packagesPath = Path.Combine(workspacePath, "packages");
			_fileSystem.Directory.CreateDirectory(workspacePath);
			_fileSystem.Directory.CreateDirectory(packagesPath);
		}

		// Act & Assert
		Action act = () => _workspaceMerger.MergeToZip(workspacePaths, outputPath, zipFileName);
		act.Should().Throw<InvalidOperationException>();
	}

	#endregion

}
