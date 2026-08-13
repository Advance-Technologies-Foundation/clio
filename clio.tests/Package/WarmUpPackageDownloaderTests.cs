using System;
using System.IO;
using System.Threading;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class WarmUpPackageDownloaderTests {

	#region Constants: Private

	private const string WarmUpFileName = "package.zip";

	#endregion

	#region Fields: Private

	private ILogger _logger;
	private WarmUpPackageDownloader _downloader;

	#endregion

	#region Nested type: Private

	/// <summary>A downloader whose private-directory creation always fails, to drive the fail-closed path.</summary>
	private sealed class FailingDirectoryWarmUpPackageDownloader : WarmUpPackageDownloader {

		public FailingDirectoryWarmUpPackageDownloader(ILogger logger) : base(logger) { }

		protected override string CreateOwnerPrivateDirectory() =>
			throw new IOException("cannot establish a private directory");

	}

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_logger = Substitute.For<ILogger>();
		_downloader = new WarmUpPackageDownloader(_logger);
	}

	[TearDown]
	public void TearDown() {
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("A successful warm-up creates a real owner-private directory (0700 on Unix), downloads into a "
		+ "file inside it, and removes the directory afterwards, so no discarded payload is left behind.")]
	public void RunWarmUpDownload_CreatesOwnerPrivateDir_DownloadsIntoIt_AndRemovesIt() {
		// Arrange
		string capturedDirectory = null;

		// Act
		_downloader.RunWarmUpDownload(path => {
			capturedDirectory = Path.GetDirectoryName(path);
			Path.GetFileName(path).Should().Be(WarmUpFileName,
				because: "the download must target the warm-up file INSIDE the private directory");
			File.WriteAllText(path, "discarded-payload");
			if (!OperatingSystem.IsWindows()) {
				(File.GetUnixFileMode(capturedDirectory) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite
						| UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite
						| UnixFileMode.OtherExecute))
					.Should().Be(UnixFileMode.None,
						because: "on Unix the directory must be owner-only (0700) so another local user cannot "
							+ "reach the archive the external downloader writes into it");
			}
		});

		// Assert
		capturedDirectory.Should().NotBeNull(because: "the download must have run");
		Directory.Exists(capturedDirectory).Should().BeFalse(
			because: "every warm-up removes its own private directory once the download returns");
	}

	[Test]
	[Description("A download that writes a partial artifact and then throws is still fully cleaned up and does "
		+ "not escape the worker, because the warm-up runs on a thread where an unhandled exception is fatal.")]
	public void RunWarmUpDownload_RemovesDirectory_WhenDownloadWritesThenThrows() {
		// Arrange
		string capturedDirectory = null;

		// Act
		Action act = () => _downloader.RunWarmUpDownload(path => {
			capturedDirectory = Path.GetDirectoryName(path);
			File.WriteAllText(path, "partial");
			throw new InvalidOperationException("boom");
		});

		// Assert
		act.Should().NotThrow(
			because: "the warm-up is best-effort and a propagated exception on its thread would end the CLI");
		Directory.Exists(capturedDirectory).Should().BeFalse(
			because: "a partially written archive must be removed together with its directory");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("warm-up") && m.Contains("boom")));
		// NSubstitute's Received() takes no `because`; stated here: the failure must be surfaced in the log.
	}

	[Test]
	[Description("When an owner-private directory cannot be established the download is skipped entirely "
		+ "(fail-closed), so a payload is never written into an unprotected location.")]
	public void RunWarmUpDownload_SkipsDownload_WhenPrivateDirectoryCannotBeCreated() {
		// Arrange
		WarmUpPackageDownloader downloader = new FailingDirectoryWarmUpPackageDownloader(_logger);
		bool downloadInvoked = false;

		// Act
		Action act = () => downloader.RunWarmUpDownload(_ => downloadInvoked = true);

		// Assert
		act.Should().NotThrow(
			because: "failing to create the private directory must be contained, not thrown from the worker");
		downloadInvoked.Should().BeFalse(
			because: "fail-closed: with no verified owner-private location there is nowhere safe to download to");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("skipped") && m.Contains("private")));
		// Received() takes no `because`; stated here: the skipped warm-up must still be surfaced.
	}

	[Test]
	[Description("Two overlapping warm-ups running concurrently use distinct directories that coexist while both "
		+ "callbacks are live, and each directory is removed only after its own callback exits.")]
	public void RunWarmUpDownload_OverlappingRuns_UseDistinctDirs_AndEachRemovedAfterItsOwnCallback() {
		// Arrange
		using CountdownEvent bothInside = new(2);
		using ManualResetEventSlim release = new(false);
		string firstDirectory = null;
		string secondDirectory = null;

		Action<string> makeCallback(Action<string> capture) => path => {
			capture(path);
			File.WriteAllText(path, "payload");
			bothInside.Signal();   // announce this worker is live inside its callback
			release.Wait(TimeSpan.FromSeconds(5)); // hold both callbacks live simultaneously
		};

		Thread first = new(() => _downloader.RunWarmUpDownload(makeCallback(p => firstDirectory = Path.GetDirectoryName(p))));
		Thread second = new(() => _downloader.RunWarmUpDownload(makeCallback(p => secondDirectory = Path.GetDirectoryName(p))));

		// Act
		first.Start();
		second.Start();
		bothInside.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
			because: "both warm-ups must reach their download callback so the two runs genuinely overlap");

		// Assert — both live at once
		firstDirectory.Should().NotBe(secondDirectory,
			because: "each concurrent warm-up must acquire its own directory, never a shared path");
		Directory.Exists(firstDirectory).Should().BeTrue(
			because: "while its own callback is still live a worker's directory must exist");
		Directory.Exists(secondDirectory).Should().BeTrue(
			because: "the other concurrent worker must not have deleted this one's in-flight directory");

		// Act — let both finish
		release.Set();
		first.Join(TimeSpan.FromSeconds(5)).Should().BeTrue(because: "the first worker must complete");
		second.Join(TimeSpan.FromSeconds(5)).Should().BeTrue(because: "the second worker must complete");

		// Assert — each cleaned only after its own callback exited
		Directory.Exists(firstDirectory).Should().BeFalse(because: "the first worker removes its own directory");
		Directory.Exists(secondDirectory).Should().BeFalse(because: "the second worker removes its own directory");
	}

	[Test]
	[Description("StartWarmUpDownload runs the download on a foreground thread, so the process stays alive until "
		+ "the temporary directory is cleaned up rather than exiting mid-warm-up and leaking it.")]
	public void StartWarmUpDownload_RunsDownload_OnForegroundThread() {
		// Arrange
		using ManualResetEventSlim invoked = new(false);
		bool? wasBackgroundThread = null;
		string capturedPath = null;

		// Act
		_downloader.StartWarmUpDownload(path => {
			capturedPath = path;
			wasBackgroundThread = Thread.CurrentThread.IsBackground;
			invoked.Set();
		});

		// Assert
		invoked.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
			because: "the worker must actually run the supplied download");
		wasBackgroundThread.Should().BeFalse(
			because: "a foreground thread keeps the process alive so cleanup in finally is not skipped at exit");
		Path.GetFileName(capturedPath).Should().Be(WarmUpFileName,
			because: "the background worker downloads into the same warm-up file as the synchronous core");
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
