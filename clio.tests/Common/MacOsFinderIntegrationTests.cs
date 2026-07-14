using System;
using System.IO.Abstractions;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[Category("Unit")]
[Category("Common")]
public class MacOsFinderIntegrationTests {

	private Clio.Common.IFileSystem _fileSystem = null!;
	private ILogger _logger = null!;
	private IProcessExecutor _processExecutor = null!;
	private MacOsFinderIntegration _service = null!;

	private string SourcePath =>
		string.Join("/", AppContext.BaseDirectory, "finder", "DeployCreatio.workflow");
	private string DestinationPath =>
		string.Join("/",
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"Library/Services", "DeployCreatio.workflow");

	[SetUp]
	public void SetUp() {
		_fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		_logger = Substitute.For<ILogger>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		_service = new MacOsFinderIntegration(_fileSystem, _logger, _processExecutor);
		// Combine concatenates the supplied fragments so each resolved path stays distinct.
		_fileSystem.Combine(Arg.Any<string[]>())
			.Returns(call => string.Join("/", (string[])call[0]));
	}

	[Test]
	[Description("InstallAsync copies the bundled Quick Action into the user Services folder when it is not yet installed.")]
	public void InstallAsync_ShouldCopy_WhenNotInstalled() {
		// Arrange
		_fileSystem.ExistsDirectory(SourcePath).Returns(true);
		_fileSystem.ExistsDirectory(DestinationPath).Returns(false);

		// Act
		_service.InstallAsync().GetAwaiter().GetResult();

		// Assert
		_fileSystem.Received(1).CopyDirectory(SourcePath, DestinationPath, true);
		_processExecutor.Received(1).Execute("/bin/bash",
			Arg.Is<string>(a => a.Contains("NSServicesStatus") && a.Contains("ContextMenu = 1")),
			true, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("installed or updated")));
	}

	[Test]
	[Description("InstallAsync does not attempt to enable the context menu when the copy step is skipped.")]
	public void InstallAsync_ShouldNotEnableContextMenu_WhenUpToDate() {
		// Arrange
		_fileSystem.ExistsDirectory(SourcePath).Returns(true);
		_fileSystem.ExistsDirectory(DestinationPath).Returns(true);
		IDirectoryInfo sourceInfo = Substitute.For<IDirectoryInfo>();
		IDirectoryInfo destinationInfo = Substitute.For<IDirectoryInfo>();
		sourceInfo.LastWriteTimeUtc.Returns(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		destinationInfo.LastWriteTimeUtc.Returns(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		_fileSystem.GetDirectoryInfo(SourcePath).Returns(sourceInfo);
		_fileSystem.GetDirectoryInfo(DestinationPath).Returns(destinationInfo);

		// Act
		_service.InstallAsync().GetAwaiter().GetResult();

		// Assert
		_processExecutor.DidNotReceiveWithAnyArgs().Execute(default, default, default);
	}

	[Test]
	[Description("InstallAsync skips the copy when the installed Quick Action is already up to date.")]
	public void InstallAsync_ShouldSkip_WhenInstalledAndNotNewer() {
		// Arrange
		_fileSystem.ExistsDirectory(SourcePath).Returns(true);
		_fileSystem.ExistsDirectory(DestinationPath).Returns(true);
		IDirectoryInfo sourceInfo = Substitute.For<IDirectoryInfo>();
		IDirectoryInfo destinationInfo = Substitute.For<IDirectoryInfo>();
		sourceInfo.LastWriteTimeUtc.Returns(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		destinationInfo.LastWriteTimeUtc.Returns(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		_fileSystem.GetDirectoryInfo(SourcePath).Returns(sourceInfo);
		_fileSystem.GetDirectoryInfo(DestinationPath).Returns(destinationInfo);

		// Act
		_service.InstallAsync().GetAwaiter().GetResult();

		// Assert
		_fileSystem.DidNotReceive().CopyDirectory(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
	}

	[Test]
	[Description("InstallAsync refreshes the installed Quick Action when the bundled copy is newer.")]
	public void InstallAsync_ShouldUpdate_WhenBundledIsNewer() {
		// Arrange
		_fileSystem.ExistsDirectory(SourcePath).Returns(true);
		_fileSystem.ExistsDirectory(DestinationPath).Returns(true);
		IDirectoryInfo sourceInfo = Substitute.For<IDirectoryInfo>();
		IDirectoryInfo destinationInfo = Substitute.For<IDirectoryInfo>();
		sourceInfo.LastWriteTimeUtc.Returns(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		destinationInfo.LastWriteTimeUtc.Returns(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		_fileSystem.GetDirectoryInfo(SourcePath).Returns(sourceInfo);
		_fileSystem.GetDirectoryInfo(DestinationPath).Returns(destinationInfo);

		// Act
		_service.InstallAsync().GetAwaiter().GetResult();

		// Assert
		_fileSystem.Received(1).DeleteDirectoryIfExists(DestinationPath);
		_fileSystem.Received(1).CopyDirectory(SourcePath, DestinationPath, true);
	}

	[Test]
	[Description("InstallAsync logs a warning and does nothing when the bundled Quick Action is missing.")]
	public void InstallAsync_ShouldWarn_WhenBundledWorkflowMissing() {
		// Arrange
		_fileSystem.ExistsDirectory(SourcePath).Returns(false);

		// Act
		_service.InstallAsync().GetAwaiter().GetResult();

		// Assert
		_fileSystem.DidNotReceive().CopyDirectory(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
		_logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("not found")));
	}

	[Test]
	[Description("UninstallAsync removes the Quick Action from the user Services folder when present.")]
	public void UninstallAsync_ShouldDelete_WhenInstalled() {
		// Arrange
		_fileSystem.ExistsDirectory(DestinationPath).Returns(true);

		// Act
		_service.UninstallAsync().GetAwaiter().GetResult();

		// Assert
		_fileSystem.Received(1).DeleteDirectoryIfExists(DestinationPath);
		_processExecutor.Received(1).Execute("/bin/bash",
			Arg.Is<string>(a => a.Contains("pbs -flush")),
			true, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("removed")));
	}

	[Test]
	[Description("IsInstalled reflects the presence of the Quick Action in the user Services folder.")]
	public void IsInstalled_ShouldReflectFileSystemState() {
		// Arrange
		_fileSystem.ExistsDirectory(DestinationPath).Returns(true);

		// Act / Assert
		_service.IsInstalled().Should().BeTrue();
	}

}
