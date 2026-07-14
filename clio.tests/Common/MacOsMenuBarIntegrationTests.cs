using System;
using System.IO.Abstractions;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[Category("Unit")]
[Category("Common")]
public class MacOsMenuBarIntegrationTests {

	private Clio.Common.IFileSystem _fileSystem = null!;
	private ILogger _logger = null!;
	private IProcessExecutor _processExecutor = null!;
	private MacOsMenuBarIntegration _service = null!;

	private string SourcePath =>
		string.Join("/", AppContext.BaseDirectory, "finder", "menubar", "ClioMenuBar.swift");
	private string BinaryPath =>
		string.Join("/",
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"Library/Application Support/clio", "ClioMenuBar");
	private string LaunchAgentPath =>
		string.Join("/",
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"Library/LaunchAgents", "com.creatio.clio.menubar.plist");

	[SetUp]
	public void SetUp() {
		_fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		_logger = Substitute.For<ILogger>();
		_processExecutor = Substitute.For<IProcessExecutor>();
		_service = new MacOsMenuBarIntegration(_fileSystem, _logger, _processExecutor);
		_fileSystem.Combine(Arg.Any<string[]>())
			.Returns(call => string.Join("/", (string[])call[0]));
		// By default swiftc is available so tests exercise the full install path.
		_processExecutor.Execute("/bin/bash",
				Arg.Is<string>(a => a.Contains("command -v swiftc")),
				Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns("/usr/bin/swiftc");
	}

	[TearDown]
	public void TearDown() {
		_processExecutor.ClearReceivedCalls();
	}

	[Test]
	[Description("InstallAsync compiles the app, installs the LaunchAgent, and starts it when nothing is installed yet.")]
	public void InstallAsync_ShouldCompileAndInstall_WhenNotInstalled() {
		// Arrange
		_fileSystem.ExistsFile(SourcePath).Returns(true);
		_fileSystem.ExistsFile(LaunchAgentPath).Returns(false);
		_fileSystem.ExistsFile(BinaryPath).Returns(false, true);

		// Act
		_service.InstallAsync().GetAwaiter().GetResult();

		// Assert
		_processExecutor.Received(1).Execute("/bin/bash",
			Arg.Is<string>(a => a.Contains("swiftc -O")),
			true, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
		_fileSystem.Received(1).WriteAllTextToFile(LaunchAgentPath,
			Arg.Is<string>(p => p.Contains("com.creatio.clio.menubar")));
		_processExecutor.Received(1).Execute("/bin/bash",
			Arg.Is<string>(a => a.Contains("launchctl load")),
			true, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("installed and started")));
	}

	[Test]
	[Description("InstallAsync warns and skips everything when swiftc is not available.")]
	public void InstallAsync_ShouldWarn_WhenSwiftcMissing() {
		// Arrange
		_fileSystem.ExistsFile(SourcePath).Returns(true);
		_processExecutor.Execute("/bin/bash",
				Arg.Is<string>(a => a.Contains("command -v swiftc")),
				Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(string.Empty);

		// Act
		_service.InstallAsync().GetAwaiter().GetResult();

		// Assert
		_logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("swiftc")));
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("InstallAsync logs a warning and does nothing when the bundled Swift source is missing.")]
	public void InstallAsync_ShouldWarn_WhenSourceMissing() {
		// Arrange
		_fileSystem.ExistsFile(SourcePath).Returns(false);

		// Act
		_service.InstallAsync().GetAwaiter().GetResult();

		// Assert
		_logger.Received().WriteWarning(Arg.Is<string>(s => s.Contains("not found")));
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("InstallAsync does not recompile or reinstall the LaunchAgent when the app is already up to date.")]
	public void InstallAsync_ShouldStayQuiet_WhenUpToDate() {
		// Arrange
		_fileSystem.ExistsFile(SourcePath).Returns(true);
		_fileSystem.ExistsFile(LaunchAgentPath).Returns(true);
		_fileSystem.ExistsFile(BinaryPath).Returns(true);
		IFileInfo sourceInfo = Substitute.For<IFileInfo>();
		IFileInfo binaryInfo = Substitute.For<IFileInfo>();
		sourceInfo.LastWriteTimeUtc.Returns(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		binaryInfo.LastWriteTimeUtc.Returns(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		_fileSystem.GetFilesInfos(SourcePath).Returns(sourceInfo);
		_fileSystem.GetFilesInfos(BinaryPath).Returns(binaryInfo);

		// Act
		_service.InstallAsync().GetAwaiter().GetResult();

		// Assert
		_processExecutor.DidNotReceive().Execute("/bin/bash",
			Arg.Is<string>(a => a.Contains("swiftc -O")),
			Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
		_fileSystem.DidNotReceive().WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("InstallAsync recompiles the app when the bundled source is newer than the installed binary.")]
	public void InstallAsync_ShouldRecompile_WhenSourceNewer() {
		// Arrange
		_fileSystem.ExistsFile(SourcePath).Returns(true);
		_fileSystem.ExistsFile(LaunchAgentPath).Returns(true);
		_fileSystem.ExistsFile(BinaryPath).Returns(true);
		IFileInfo sourceInfo = Substitute.For<IFileInfo>();
		IFileInfo binaryInfo = Substitute.For<IFileInfo>();
		sourceInfo.LastWriteTimeUtc.Returns(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		binaryInfo.LastWriteTimeUtc.Returns(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		_fileSystem.GetFilesInfos(SourcePath).Returns(sourceInfo);
		_fileSystem.GetFilesInfos(BinaryPath).Returns(binaryInfo);

		// Act
		_service.InstallAsync().GetAwaiter().GetResult();

		// Assert
		_processExecutor.Received(1).Execute("/bin/bash",
			Arg.Is<string>(a => a.Contains("swiftc -O")),
			true, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
	}

	[Test]
	[Description("UninstallAsync unloads the LaunchAgent and deletes both the plist and the compiled binary.")]
	public void UninstallAsync_ShouldRemove_WhenInstalled() {
		// Arrange
		_fileSystem.ExistsFile(LaunchAgentPath).Returns(true);

		// Act
		_service.UninstallAsync().GetAwaiter().GetResult();

		// Assert
		_processExecutor.Received(1).Execute("/bin/bash",
			Arg.Is<string>(a => a.Contains("launchctl unload")),
			true, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
		_fileSystem.Received(1).DeleteFileIfExists(LaunchAgentPath);
		_fileSystem.Received(1).DeleteFileIfExists(BinaryPath);
		_logger.Received().WriteInfo(Arg.Is<string>(s => s.Contains("removed")));
	}

	[Test]
	[Description("IsInstalled reflects the presence of the LaunchAgent plist.")]
	public void IsInstalled_ShouldReflectFileSystemState() {
		// Arrange
		_fileSystem.ExistsFile(LaunchAgentPath).Returns(true);

		// Act / Assert
		_service.IsInstalled().Should().BeTrue("the LaunchAgent plist exists");
	}

}
