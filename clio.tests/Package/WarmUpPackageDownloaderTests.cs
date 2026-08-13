using System;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = System.IO.Abstractions.IFileSystem;
using IOException = System.IO.IOException;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class WarmUpPackageDownloaderTests {

	#region Constants: Private

	private const string WarmUpFileName = "package.zip";

	#endregion

	#region Fields: Private

	private IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private IFileSystem _fileSystem;
	private ILogger _logger;
	private WarmUpPackageDownloader _downloader;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		_fileSystem = new MockFileSystem();
		_logger = Substitute.For<ILogger>();
		_downloader = new WarmUpPackageDownloader(_workingDirectoriesProvider, _fileSystem, _logger);
	}

	[TearDown]
	public void TearDown() {
		_workingDirectoriesProvider.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("A successful warm-up downloads into a file under the temp directory and removes that directory, "
		+ "so no discarded payload is left behind.")]
	public void RunWarmUpDownload_DownloadsIntoTempDirectory_AndRemovesIt_OnSuccess() {
		// Arrange
		const string tempDirectory = "/clio-temp/dir-success";
		_workingDirectoriesProvider.CreateTempDirectory().Returns(tempDirectory);
		string receivedPath = null;

		// Act
		_downloader.RunWarmUpDownload(path => receivedPath = path);

		// Assert
		receivedPath.Should().Be(_fileSystem.Path.Combine(tempDirectory, WarmUpFileName),
			because: "the download must target a file INSIDE the owner-private temp directory, not the shared "
				+ "temp root, so the discarded payload cannot be reached through a predictable path");
		_workingDirectoriesProvider.Received(1).DeleteDirectoryIfExists(tempDirectory);
		// NSubstitute's Received() takes no `because`; stated here: the whole point of owning the lifecycle is
		// that every warm-up removes its own temp directory once the download returns.
	}

	[Test]
	[Description("A throwing download does not escape the worker and its temp directory is still removed, "
		+ "because the warm-up runs on a raw background thread where an unhandled exception would end the process.")]
	public void RunWarmUpDownload_ContainsAndCleansUp_WhenDownloadThrows() {
		// Arrange
		const string tempDirectory = "/clio-temp/dir-throwing";
		_workingDirectoriesProvider.CreateTempDirectory().Returns(tempDirectory);

		// Act
		Action act = () => _downloader.RunWarmUpDownload(_ => throw new InvalidOperationException("boom"));

		// Assert
		act.Should().NotThrow(
			because: "the warm-up is best-effort and runs on a raw background thread — a propagated exception "
				+ "there would terminate the CLI process");
		_workingDirectoriesProvider.Received(1).DeleteDirectoryIfExists(tempDirectory);
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("warm-up") && m.Contains("boom")));
		// Received() takes no `because`; stated here: a failed download must be surfaced AND its temp cleaned.
	}

	[Test]
	[Description("Overlapping warm-ups use distinct temp directories and each removes only its own, so a "
		+ "concurrent warm-up cannot delete another's in-flight artifact.")]
	public void RunWarmUpDownload_UsesDistinctPaths_AndCleansOnlyItsOwn_ForOverlappingRuns() {
		// Arrange
		const string firstDirectory = "/clio-temp/dir-1";
		const string secondDirectory = "/clio-temp/dir-2";
		_workingDirectoriesProvider.CreateTempDirectory().Returns(firstDirectory, secondDirectory);
		string firstPath = null;
		string secondPath = null;

		// Act
		_downloader.RunWarmUpDownload(path => firstPath = path);
		_downloader.RunWarmUpDownload(path => secondPath = path);

		// Assert
		firstPath.Should().NotBe(secondPath,
			because: "each warm-up acquires its own temp directory, so two overlapping runs must never share a "
				+ "download path");
		_workingDirectoriesProvider.Received(1).DeleteDirectoryIfExists(firstDirectory);
		_workingDirectoriesProvider.Received(1).DeleteDirectoryIfExists(secondDirectory);
		// Received() takes no `because`; stated here: each run cleans exactly its own directory, never the other's.
	}

	[Test]
	[Description("A cleanup failure is logged rather than propagated, so a temporary directory that cannot be "
		+ "removed does not crash the background worker.")]
	public void RunWarmUpDownload_LogsCleanupFailure_WithoutThrowing() {
		// Arrange
		const string tempDirectory = "/clio-temp/dir-locked";
		_workingDirectoriesProvider.CreateTempDirectory().Returns(tempDirectory);
		_workingDirectoriesProvider.When(p => p.DeleteDirectoryIfExists(tempDirectory))
			.Do(_ => throw new IOException("directory in use"));

		// Act
		Action act = () => _downloader.RunWarmUpDownload(_ => { });

		// Assert
		act.Should().NotThrow(
			because: "a cleanup failure on the background worker must be surfaced through the log, not thrown");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("clean up") && m.Contains(tempDirectory)));
		// Received() takes no `because`; stated here: the leaked directory must be visible in the log.
	}

	[Test]
	[Description("A failure acquiring the temp directory is contained, because it happens before any file work "
		+ "and must not escape the worker either.")]
	public void RunWarmUpDownload_ContainsFailure_WhenTempDirectoryCannotBeCreated() {
		// Arrange
		_workingDirectoriesProvider.CreateTempDirectory().Returns(_ => throw new IOException("no temp"));
		bool downloadInvoked = false;

		// Act
		Action act = () => _downloader.RunWarmUpDownload(_ => downloadInvoked = true);

		// Assert
		act.Should().NotThrow(
			because: "temp-path acquisition is inside the exception boundary, so even its failure cannot end the "
				+ "process");
		downloadInvoked.Should().BeFalse(
			because: "there is nowhere to download to when the temp directory could not be created");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("warm-up")));
		// Received() takes no `because`; the failure must still be surfaced.
	}

	[Test]
	[Description("StartWarmUpDownload runs the supplied download on a background thread, which is how the warm-up "
		+ "stays fire-and-forget while the caller proceeds to poll.")]
	public void StartWarmUpDownload_InvokesDownload_OnBackgroundThread() {
		// Arrange
		const string tempDirectory = "/clio-temp/dir-async";
		_workingDirectoriesProvider.CreateTempDirectory().Returns(tempDirectory);
		using ManualResetEventSlim invoked = new(false);
		string receivedPath = null;

		// Act
		_downloader.StartWarmUpDownload(path => {
			receivedPath = path;
			invoked.Set();
		});

		// Assert
		invoked.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
			because: "the background worker must actually run the supplied download");
		receivedPath.Should().Be(_fileSystem.Path.Combine(tempDirectory, WarmUpFileName),
			because: "the background worker downloads into the same owner-private temp file as the synchronous core");
	}

	[Test]
	[Description("StartWarmUpDownload rejects a null download action, because a warm-up with nothing to run is a "
		+ "programming error, not a silent no-op.")]
	public void StartWarmUpDownload_Throws_WhenDownloadActionIsNull() {
		// Arrange
		// Nothing to arrange.

		// Act
		Action act = () => _downloader.StartWarmUpDownload(null);

		// Assert
		act.Should().Throw<ArgumentNullException>(
			because: "a null download action cannot be honoured and must fail fast at the call site");
	}

	#endregion

}
