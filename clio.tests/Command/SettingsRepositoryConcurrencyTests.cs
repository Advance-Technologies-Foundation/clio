using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
[NonParallelizable]
public sealed class SettingsRepositoryConcurrencyTests {

	private MockFileSystem _fileSystem;

	[SetUp]
	public void SetUp() {
		_fileSystem = TestFileSystem.MockFileSystem();
		_fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(JsonConvert.SerializeObject(
			new Settings {
				ActiveEnvironmentKey = "existing",
				Environments = new Dictionary<string, EnvironmentSettings> {
					["existing"] = new() { Uri = "https://existing.example.com" }
				}
			})));
	}

	[Test]
	[Description("Two repositories created from the same snapshot preserve both environment registrations.")]
	public void ConfigureEnvironment_ShouldPreserveCompletedRegistration_WhenRepositorySnapshotIsStale() {
		// Arrange
		SettingsRepository firstDeployment = new(_fileSystem);
		SettingsRepository laterDeployment = new(_fileSystem);

		// Act
		firstDeployment.ConfigureEnvironment("first", new EnvironmentSettings { Uri = "https://first.example.com" });
		laterDeployment.ConfigureEnvironment("later", new EnvironmentSettings { Uri = "https://later.example.com" });
		SettingsRepository persisted = new(_fileSystem);

		// Assert
		persisted.GetAllEnvironments().Should().ContainKey("existing",
			because: "the environment that existed before either deployment must be preserved");
		persisted.GetAllEnvironments().Should().ContainKey("first",
			because: "the first completed deployment must not be erased by a later deployment");
		persisted.GetAllEnvironments().Should().ContainKey("later",
			because: "the later deployment must also register its environment");
	}

	[Test]
	[Description("An uninstall using a stale repository snapshot preserves an environment registered by a completed deployment.")]
	public void RemoveEnvironment_ShouldPreserveNewRegistration_WhenRepositorySnapshotIsStale() {
		// Arrange
		SettingsRepository deployment = new(_fileSystem);
		SettingsRepository uninstall = new(_fileSystem);

		// Act
		deployment.ConfigureEnvironment("deployed", new EnvironmentSettings { Uri = "https://deployed.example.com" });
		uninstall.RemoveEnvironment("existing");
		SettingsRepository persisted = new(_fileSystem);

		// Assert
		persisted.GetAllEnvironments().Should().ContainKey("deployed",
			because: "uninstall must remove from the latest settings without erasing a concurrently registered environment");
		persisted.GetAllEnvironments().Should().NotContainKey("existing",
			because: "the requested environment must still be unregistered");
	}

	[Test]
	[Description("Conditional unregister preserves a same-name environment whose deployment path changed concurrently.")]
	public void RemoveEnvironmentIfPathMatches_ShouldPreserveReplacement_WhenPathChanged() {
		// Arrange
		string originalPath = Path.Combine(Path.GetTempPath(), "clio", "original");
		string replacementPath = Path.Combine(Path.GetTempPath(), "clio", "replacement");
		SettingsRepository setup = new(_fileSystem);
		setup.ConfigureEnvironment("target", new EnvironmentSettings {
			Uri = "https://original.example.com",
			EnvironmentPath = originalPath
		});
		SettingsRepository uninstall = new(_fileSystem);
		setup.ConfigureEnvironment("target", new EnvironmentSettings {
			Uri = "https://replacement.example.com",
			EnvironmentPath = replacementPath
		});

		// Act
		bool removed = uninstall.RemoveEnvironmentIfPathMatches("target", originalPath);
		SettingsRepository persisted = new(_fileSystem);

		// Assert
		removed.Should().BeFalse(because: "the original uninstall authority no longer matches the registration");
		persisted.FindEnvironment("target").EnvironmentPath.Should().Be(replacementPath,
			because: "a concurrent same-name deployment must not be unregistered by stale uninstall work");
	}

	[Test]
	[Description("Conditional unregister removes the environment when its canonical deployment path still matches.")]
	public void RemoveEnvironmentIfPathMatches_ShouldRemoveMatchingRegistration() {
		// Arrange
		string targetPath = Path.Combine(Path.GetTempPath(), "clio", "target");
		SettingsRepository sut = new(_fileSystem);
		sut.ConfigureEnvironment("target", new EnvironmentSettings {
			Uri = "https://target.example.com",
			EnvironmentPath = targetPath + Path.DirectorySeparatorChar
		});

		// Act
		bool removed = sut.RemoveEnvironmentIfPathMatches("target", targetPath);
		SettingsRepository persisted = new(_fileSystem);

		// Assert
		removed.Should().BeTrue(because: "equivalent canonical paths retain uninstall authority");
		persisted.FindEnvironment("target").Should().BeNull(
			because: "the unchanged matching registration should be removed after successful cleanup");
	}

	[Test]
	[Description("Current environment lookup reloads settings so named uninstall does not act on a stale path snapshot.")]
	public void FindCurrentEnvironment_ShouldReturnLatestRegisteredPath_WhenRepositorySnapshotIsStale() {
		// Arrange
		string originalPath = Path.Combine(Path.GetTempPath(), "clio", "original-current");
		string replacementPath = Path.Combine(Path.GetTempPath(), "clio", "replacement-current");
		SettingsRepository writer = new(_fileSystem);
		writer.ConfigureEnvironment("target", new EnvironmentSettings { EnvironmentPath = originalPath });
		SettingsRepository staleReader = new(_fileSystem);
		writer.ConfigureEnvironment("target", new EnvironmentSettings { EnvironmentPath = replacementPath });

		// Act
		EnvironmentSettings current = staleReader.FindCurrentEnvironment("target");

		// Assert
		current.EnvironmentPath.Should().Be(replacementPath,
			because: "named uninstall must acquire its name lease and then resolve the latest persisted authority");
	}

	[Test]
	[Description("Registering an environment preserves unrelated settings changed after the repository was created.")]
	public void ConfigureEnvironment_ShouldPreserveManualChanges_WhenRepositorySnapshotIsStale() {
		// Arrange
		SettingsRepository deployment = new(_fileSystem);
		Settings manuallyEdited = JsonConvert.DeserializeObject<Settings>(
			_fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile));
		manuallyEdited.RemoteArtefactServerPath = "manual-edit";
		_fileSystem.File.WriteAllText(SettingsRepository.AppSettingsFile,
			JsonConvert.SerializeObject(manuallyEdited));

		// Act
		deployment.ConfigureEnvironment("deployed", new EnvironmentSettings { Uri = "https://deployed.example.com" });
		Settings persisted = JsonConvert.DeserializeObject<Settings>(
			_fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile));

		// Assert
		persisted.RemoteArtefactServerPath.Should().Be("manual-edit",
			because: "the settings file must be reloaded immediately before applying the deployment registration");
		persisted.Environments.Should().ContainKey("deployed",
			because: "the requested environment registration must still be persisted");
	}

	[TestCase("{ invalid json")]
	[TestCase("null")]
	[TestCase("   ")]
	[Description("A settings mutation fails without replacing the file when the latest settings content is unreadable.")]
	public void ConfigureEnvironment_ShouldPreserveFile_WhenLatestSettingsAreUnreadable(string unreadableSettings) {
		// Arrange
		SettingsRepository deployment = new(_fileSystem);
		_fileSystem.File.WriteAllText(SettingsRepository.AppSettingsFile, unreadableSettings);

		// Act
		Action act = () => deployment.ConfigureEnvironment("deployed",
			new EnvironmentSettings { Uri = "https://deployed.example.com" });

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a stale in-memory snapshot must not replace settings that can no longer be read safely");
		_fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile).Should().Be(unreadableSettings,
			because: "the unreadable file must remain untouched for the user to repair or recover");
	}

	[Test]
	[Description("A settings mutation waits while another writer holds the settings lock.")]
	public void ConfigureEnvironment_ShouldWait_WhenAnotherWriterHoldsSettingsLock() {
		// Arrange
		SettingsRepository deployment = new(_fileSystem);
		using ManualResetEventSlim lockAcquired = new(false);
		using ManualResetEventSlim releaseLock = new(false);
		using ManualResetEventSlim mutationStarted = new(false);
		Task lockHolder = Task.Run(() => SettingsRepository.ExecuteWithSettingsLock(_fileSystem, () => {
			lockAcquired.Set();
			releaseLock.Wait();
			return true;
		}));
		lockAcquired.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
			because: "the test writer must hold the real settings lock before the competing mutation starts");

		// Act
		Task mutation = Task.Run(() => {
			mutationStarted.Set();
			deployment.ConfigureEnvironment("deployed", new EnvironmentSettings { Uri = "https://deployed.example.com" });
		});
		mutationStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
			because: "the competing mutation must have started before its blocked state is asserted");
		bool completedWhileLocked = mutation.Wait(TimeSpan.FromMilliseconds(500));
		releaseLock.Set();
		Task.WaitAll([lockHolder, mutation], TimeSpan.FromSeconds(5)).Should().BeTrue(
			because: "both writers must complete after the lock holder releases the settings lock");

		// Assert
		completedWhileLocked.Should().BeFalse(
			because: "a second writer must not enter the read-modify-write section while the lock is held");
		new SettingsRepository(_fileSystem).GetAllEnvironments().Should().ContainKey("deployed",
			because: "the blocked mutation must persist after acquiring the released lock");
	}

	[Test]
	[Description("A repository keeps reading and writing through its own filesystem after another repository is constructed.")]
	public void ConfigureEnvironment_ShouldUseInstanceFileSystem_WhenAnotherRepositoryChangesStaticDefault() {
		// Arrange
		SettingsRepository first = new(_fileSystem);
		MockFileSystem otherFileSystem = TestFileSystem.MockFileSystem();
		otherFileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(JsonConvert.SerializeObject(
			new Settings { Environments = new Dictionary<string, EnvironmentSettings>() })));
		_ = new SettingsRepository(otherFileSystem);

		// Act
		first.ConfigureEnvironment("first", new EnvironmentSettings { Uri = "https://first.example.com" });
		SettingsRepository persistedFirst = new(_fileSystem);
		SettingsRepository persistedOther = new(otherFileSystem);

		// Assert
		persistedFirst.GetAllEnvironments().Should().ContainKey("first",
			because: "the first repository must persist through the filesystem it was constructed with");
		persistedOther.GetAllEnvironments().Should().NotContainKey("first",
			because: "constructing another repository must not redirect the first repository's save into a different filesystem");
	}

	[Test]
	[Description("A settings mutation retries when an editor leaves transient invalid JSON between validation and the exact read.")]
	public void ConfigureEnvironment_ShouldRetry_WhenLatestSettingsAreTransientlyInvalid() {
		// Arrange
		string validSettings = _fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);
		SettingsBootstrapResult healthyResult = new SettingsBootstrapService(_fileSystem).GetResult();
		TransientInvalidSettingsBootstrapService bootstrap = new(_fileSystem, healthyResult, validSettings);
		SettingsRepository deployment = new(_fileSystem, bootstrap);

		// Act
		deployment.ConfigureEnvironment("deployed", new EnvironmentSettings { Uri = "https://deployed.example.com" });

		// Assert
		bootstrap.GetResultCallCount.Should().Be(3,
			because: "the mutation must reload after the transient parse failure instead of failing immediately");
		new SettingsRepository(_fileSystem).GetAllEnvironments().Should().ContainKey("deployed",
			because: "the retried mutation must apply to the restored valid settings");
	}

	private sealed class TransientInvalidSettingsBootstrapService(
		MockFileSystem fileSystem,
		SettingsBootstrapResult healthyResult,
		string validSettings) : ISettingsBootstrapService {

		public int GetResultCallCount { get; private set; }

		public SettingsBootstrapResult GetResult() {
			GetResultCallCount++;
			if (GetResultCallCount == 2) {
				fileSystem.File.WriteAllText(SettingsRepository.AppSettingsFile, "{ transient invalid json");
			}
			else if (GetResultCallCount == 3) {
				fileSystem.File.WriteAllText(SettingsRepository.AppSettingsFile, validSettings);
			}
			return healthyResult;
		}

		public SettingsBootstrapReport GetReport() {
			return GetResult().Report;
		}

		// This double models the read side only; the repair/no-repair distinction is irrelevant here, so
		// the non-repairing read follows the same scripted sequence.
		public SettingsBootstrapResult GetResultWithoutRepairs() {
			return GetResult();
		}
	}

	[Test]
	[Description("A settings publish that a contending reader refuses is retried until it lands instead of failing the command.")]
	public void ConfigureEnvironment_ShouldRetryThePublish_WhenAContendingReaderRefusesIt() {
		// Arrange
		// The refusal shape is the one Windows produces when a FOREIGN reader holds appsettings.json:
		// File.ReadAllText opens with FileShare.Read, which denies the DELETE access the publish needs, and
		// the BCL reports it as a PATH-LESS IOException (File.Move/File.Replace pass no path to
		// Win32Marshal). The repository is constructed BEFORE the script is armed so the bootstrap's own
		// migration write is not counted as a publish attempt.
		ScriptedPublishFailureFileSystem fileSystem = new();
		SeedExistingSettings(fileSystem);
		SettingsRepository deployment = new(fileSystem);
		fileSystem.ArmPublishRefusals(new IOException(
			"The process cannot access the file because it is being used by another process."), 3);

		// Act
		deployment.ConfigureEnvironment("deployed",
			new EnvironmentSettings { Uri = "https://deployed.example.com" });

		// Assert
		fileSystem.PublishAttempts.Should().Be(4,
			because: "the publish must be re-attempted after each refusal — three refusals and one success is four calls, and without the retry the count would be one and the command would have failed");
		new SettingsRepository(fileSystem).GetAllEnvironments().Should().ContainKey("deployed",
			because: "a registration must survive a reader that momentarily refuses the publish, not be lost with it");
	}

	[Test]
	[Description("A publish failure that is not contention surfaces on the first attempt instead of being spun on until the deadline.")]
	public void ConfigureEnvironment_ShouldNotRetryThePublish_WhenTheFailureIsNotContention() {
		// Arrange
		ScriptedPublishFailureFileSystem fileSystem = new();
		SeedExistingSettings(fileSystem);
		SettingsRepository deployment = new(fileSystem);
		fileSystem.ArmPublishRefusals(new FileNotFoundException("The temporary settings file vanished."), 1);

		// Act
		Action act = () => deployment.ConfigureEnvironment("deployed",
			new EnvironmentSettings { Uri = "https://deployed.example.com" });

		// Assert
		act.Should().Throw<FileNotFoundException>(
			because: "a vanished temporary file is a real error, not a contending handle, so it must reach the caller unchanged");
		fileSystem.PublishAttempts.Should().Be(1,
			because: "a non-contention failure must not be retried into a multi-second delay before the same error is reported anyway");
	}

	[Test]
	[Description("A publish that stays refused for the whole window must name appsettings.json, say that clio retried and for how long, and keep the original refusal reachable, instead of surfacing the BCL's pathless sentence.")]
	public void ConfigureEnvironment_ShouldNameTheFileAndTheRetryInTheFailure_WhenTheRefusalNeverClears() {
		// Arrange
		// The BCL gives this failure NO path and NO stack — File.Move and File.Replace pass no path to
		// Win32Marshal — so the single sentence below is everything the user and the next investigator got.
		IOException refusal = new("The process cannot access the file because it is being used by another process.");
		ScriptedPublishFailureFileSystem fileSystem = new();
		SeedExistingSettings(fileSystem);
		SettingsRepository deployment = new(fileSystem,
			new SettingsRepository.SettingsPublishRetryPolicy(ShortPublishRetryWindow, _ => 0));
		fileSystem.ArmPublishRefusals(refusal, int.MaxValue);

		// Act
		Action act = () => deployment.ConfigureEnvironment("deployed",
			new EnvironmentSettings { Uri = "https://deployed.example.com" });

		// Assert
		IOException thrown = act.Should().Throw<IOException>(
			because: "an exhausted publish is still a failure; the retry buys probability, not a guarantee, and the caller must be told the registration did not land")
			.Which;
		thrown.Message.Should().Contain(SettingsRepository.AppSettingsFile,
			because: "the refusal the platform raises names no file at all, so the destination clio was publishing has to come from clio");
		ReportedRetrySeconds(thrown.Message).Should().BeGreaterThanOrEqualTo(ShortPublishRetryWindow.TotalSeconds,
			because: "without the retry duration the message reads like a one-shot failure, and the next investigator repeats the work of discovering that clio already waited");
		thrown.Message.ToLowerInvariant().Should().Contain("another process",
			because: "a foreign handle on the file is the usual cause, and naming it is what turns the message into something the user can act on");
		thrown.InnerException.Should().BeSameAs(refusal,
			because: "the platform refusal carries the real error code and must stay reachable rather than being swallowed by the friendlier wrapper");
	}

	[Test]
	[Description("A refused publish keeps re-attempting for the whole configured window rather than a fixed number of tries, so the window is what decides how much contention clio absorbs.")]
	public void ConfigureEnvironment_ShouldKeepRetryingForTheWholeWindow_WhenTheRefusalNeverClears() {
		// Arrange
		ScriptedPublishFailureFileSystem fileSystem = new();
		SeedExistingSettings(fileSystem);
		SettingsRepository deployment = new(fileSystem,
			new SettingsRepository.SettingsPublishRetryPolicy(ShortPublishRetryWindow, _ => 0));
		fileSystem.ArmPublishRefusals(
			new IOException("The process cannot access the file because it is being used by another process."),
			int.MaxValue);
		// Derived from the backoff rather than restated: the longest a single wait can be is the capped
		// sleep plus its jitter, so the window must pay for at least this many attempts.
		int guaranteedAttempts = (int)(ShortPublishRetryWindow.TotalMilliseconds
			/ (AtomicPublishRetry.BackoffCapMilliseconds + AtomicPublishRetry.BackoffJitterMilliseconds));
		Stopwatch elapsed = Stopwatch.StartNew();

		// Act
		Action act = () => deployment.ConfigureEnvironment("deployed",
			new EnvironmentSettings { Uri = "https://deployed.example.com" });

		// Assert
		act.Should().Throw<IOException>(
			because: "the arrangement never lets the publish through, so the window has to end somewhere");
		elapsed.Elapsed.Should().BeGreaterThanOrEqualTo(ShortPublishRetryWindow,
			because: "giving up before the window is spent throws away exactly the tail of contention the window was widened to cover");
		fileSystem.PublishAttempts.Should().BeGreaterThanOrEqualTo(guaranteedAttempts,
			because: $"the bound is a deadline, not an attempt count, so a {ShortPublishRetryWindow.TotalMilliseconds} ms window must buy at least {guaranteedAttempts} attempts however the backoff curve is later retuned");
	}

	[Test]
	[Description("The publish window must stay small enough that spending it on every update attempt cannot outlast the settings lock, or raising it would only move the failure to a concurrent clio process timing out on the lock.")]
	public void DefaultPublishRetryWindow_ShouldLeaveHeadroomUnderTheSettingsLock_WhenEveryUpdateAttemptSpendsIt() {
		// Arrange
		// ExecuteWithSettingsLock holds the lock across the publish, and UpdateSettingsIfChanged can run
		// the mutation SettingsUpdateAttemptLimit times inside one hold when another writer keeps winning.
		TimeSpan lockTimeout = TimeSpan.FromSeconds(SettingsRepository.SettingsLockTimeoutSeconds);

		// Act
		TimeSpan worstCaseHold = SettingsRepository.SettingsPublishRetryPolicy.Default.Window
			* SettingsRepository.SettingsUpdateAttemptLimit;

		// Assert
		worstCaseHold.Should().BeLessThan(lockTimeout,
			because: $"a {worstCaseHold.TotalSeconds} s worst-case hold against a {lockTimeout.TotalSeconds} s lock timeout is the actual ceiling on this window — past it, widening the retry stops rescuing the writer and starts failing whoever is waiting for the lock");
	}

	[Test]
	[Description("A failure while the temp file is being written leaves the destination appsettings.json byte-for-byte unchanged instead of a torn file, because the destination is never opened for writing until the temp file is complete.")]
	public void ConfigureEnvironment_ShouldLeaveDestinationUntouched_WhenTheTempFileWriteFails() {
		// Arrange
		FaultingTempWriteFileSystem fileSystem = new();
		SeedExistingSettings(fileSystem);
		SettingsRepository deployment = new(fileSystem);
		// Captured AFTER construction: the constructor's own bootstrap can rewrite the seeded file (e.g.
		// applying a pending migration), so the pre-mutation baseline is whatever is on disk once
		// construction is done — the same baseline ConfigureEnvironment itself starts from.
		string originalContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);
		IOException writeFailure = new("Simulated disk failure while writing the temp settings file.");
		fileSystem.ArmTempWriteFailure(writeFailure);

		// Act
		Action act = () => deployment.ConfigureEnvironment("deployed",
			new EnvironmentSettings { Uri = "https://deployed.example.com" });

		// Assert
		act.Should().Throw<IOException>(
			because: "a failure while writing the temp file is a real error that must reach the caller, not be swallowed")
			.Which.Should().BeSameAs(writeFailure,
				because: "the original write failure must stay reachable rather than being replaced by a cleanup-time exception");
		fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile).Should().Be(originalContent,
			because: "the destination file is only ever replaced by a FINISHED temp file, so a write failure that happens before the temp file is complete must never reach the destination — a reader must see either the old complete file or the new one, never a partial one");
		fileSystem.AllFiles.Should().NotContain(path => path.EndsWith(".tmp", StringComparison.Ordinal),
			because: "the failed temp file must be cleaned up rather than left behind as an orphaned partial artifact");
	}

	[Test]
	[Description("A successful save leaves no temporary artifact behind: the temp file used for the atomic replace is gone once the destination has been published.")]
	public void ConfigureEnvironment_ShouldLeaveNoTemporaryArtifact_WhenTheSaveSucceeds() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SeedExistingSettings(fileSystem);
		SettingsRepository deployment = new(fileSystem);

		// Act
		deployment.ConfigureEnvironment("deployed", new EnvironmentSettings { Uri = "https://deployed.example.com" });

		// Assert
		fileSystem.AllFiles.Should().NotContain(path => path.EndsWith(".tmp", StringComparison.Ordinal),
			because: "a successful publish must leave only the destination file behind, not the temp file it was atomically replaced from");
		new SettingsRepository(fileSystem).GetAllEnvironments().Should().ContainKey("deployed",
			because: "the atomic replace must have actually landed the new content at the destination path");
	}

	// Short enough to keep the suite fast, long enough to reach the capped tail of the backoff. The
	// subject of these tests is that the window ENDS and what it says when it does, not how long
	// production waits, so burning the production window here would buy nothing.
	private static readonly TimeSpan ShortPublishRetryWindow = TimeSpan.FromMilliseconds(400);

	// Reads back the retry duration clio reported, so the assertion pins the PROPERTY (at least the
	// window elapsed) rather than restating whatever the message is formatted as.
	private static double ReportedRetrySeconds(string message) {
		Match match = Regex.Match(message, @"([0-9]+(?:\.[0-9]+)?) s\b");
		match.Success.Should().BeTrue(
			because: $"the failure must state how long clio retried, and '{message}' carries no duration in seconds");
		return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
	}

	private static void SeedExistingSettings(MockFileSystem fileSystem) {
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(JsonConvert.SerializeObject(
			new Settings {
				ActiveEnvironmentKey = "existing",
				Environments = new Dictionary<string, EnvironmentSettings> {
					["existing"] = new() { Uri = "https://existing.example.com" }
				}
			})));
	}

	// Substitutes the PUBLISH of appsettings.json so the contended path runs on every platform.
	//
	// LIMIT OF THIS DOUBLE, stated because it is easy to over-read: a substituted file system is not
	// System.IO.Abstractions.FileSystem, so SaveSettingsUnlocked treats it as not-real and commits through
	// the File.Move branch of CommitSettingsFile rather than the File.Replace branch a real CLI takes. Both
	// go through the same PublishSettingsFile wrapper, and that wrapper is what these tests pin. The
	// platform semantic of the replace itself is not reproducible here and needs a Windows host.
	private sealed class ScriptedPublishFailureFileSystem : MockFileSystem {

		private readonly ScriptedPublishFailureFile _file;

		public ScriptedPublishFailureFileSystem() {
			_file = new ScriptedPublishFailureFile(this);
		}

		public override IFile File => _file;

		public int PublishAttempts => _file.PublishAttempts;

		public void ArmPublishRefusals(Exception refusal, int refusalCount) =>
			_file.Arm(refusal, refusalCount);
	}

	private sealed class ScriptedPublishFailureFile(IMockFileDataAccessor fileDataAccessor)
		: MockFile(fileDataAccessor) {

		private Exception _refusal;
		private int _refusalsRemaining;

		public int PublishAttempts { get; private set; }

		public void Arm(Exception refusal, int refusalCount) {
			_refusal = refusal;
			_refusalsRemaining = refusalCount;
			PublishAttempts = 0;
		}

		public override void Move(string sourceFileName, string destFileName, bool overwrite) {
			if (_refusal is not null
				&& string.Equals(destFileName, SettingsRepository.AppSettingsFile, StringComparison.Ordinal)) {
				PublishAttempts++;
				if (_refusalsRemaining > 0) {
					_refusalsRemaining--;
					throw _refusal;
				}
			}
			base.Move(sourceFileName, destFileName, overwrite);
		}
	}

	// Substitutes the WRITE of the TEMP file (the step before the atomic replace) so a test can prove
	// the destination is never touched by a failure that happens while the temp file is still being
	// produced. Some bytes are written to the temp file before the fault fires — mirroring the shape of
	// a real mid-write interruption (crash, disk failure) — specifically so the assertion is "the
	// destination never sees a partial file", not merely "the temp file was never created at all".
	private sealed class FaultingTempWriteFileSystem : MockFileSystem {

		private readonly FaultingTempWriteFile _file;

		public FaultingTempWriteFileSystem() {
			_file = new FaultingTempWriteFile(this);
		}

		public override IFile File => _file;

		public void ArmTempWriteFailure(Exception failure) => _file.Arm(failure);
	}

	private sealed class FaultingTempWriteFile(IMockFileDataAccessor fileDataAccessor)
		: MockFile(fileDataAccessor) {

		private Exception _failure;

		public void Arm(Exception failure) => _failure = failure;

		public override StreamWriter CreateText(string path) {
			StreamWriter writer = base.CreateText(path);
			if (_failure is not null && path.EndsWith(".tmp", StringComparison.Ordinal)) {
				Exception failure = _failure;
				_failure = null;
				// A few bytes really do land in the temp file before the fault fires, so the destination
				// staying clean is proof the commit step (not luck) is what protects it.
				writer.Write("{\"Environments\":{\"partial");
				writer.Flush();
				throw failure;
			}
			return writer;
		}
	}

}

[TestFixture]
[Category("Integration")]
[Property("Module", "Command")]
[NonParallelizable]
public sealed class SettingsRepositoryProcessConcurrencyTests {

	[Test]
	[Description("Independent clio processes sharing CLIO_HOME preserve every concurrent environment registration.")]
	public void RegWebApp_ShouldPreserveEveryEnvironment_WhenProcessesRunConcurrently() {
		// Arrange
		const int processCount = 8;
		string clioHome = Path.Combine(Path.GetTempPath(), $"clio-settings-concurrency-{Guid.NewGuid():N}");
		Directory.CreateDirectory(clioHome);
		string clioAssemblyPath = typeof(SettingsRepository).Assembly.Location;
		List<Process> processes = [];

		try {
			// Act
			for (int index = 1; index <= processCount; index++) {
				// Output is intentionally inherited. Redirecting an unused pipe and then eagerly calling
				// ReadToEnd in an assertion can wait forever when a descendant retains the writer handle.
				ProcessStartInfo startInfo = new("dotnet") {
					UseShellExecute = false,
					CreateNoWindow = true
				};
				startInfo.Environment["CLIO_HOME"] = clioHome;
				startInfo.Environment["CLIO_NO_UPDATE_CHECK"] = "true";
				startInfo.ArgumentList.Add(clioAssemblyPath);
				startInfo.ArgumentList.Add("reg-web-app");
				startInfo.ArgumentList.Add($"env-{index}");
				startInfo.ArgumentList.Add("--uri");
				startInfo.ArgumentList.Add($"http://env-{index}");
				startInfo.ArgumentList.Add("-i");
				startInfo.ArgumentList.Add("true");
				Process process = Process.Start(startInfo)
					?? throw new InvalidOperationException("Failed to start the clio regression-test process.");
				processes.Add(process);
			}

			foreach (Process process in processes) {
				process.WaitForExit(30_000).Should().BeTrue(
					because: "every concurrent registration process must finish without waiting indefinitely for the settings lock");
				process.ExitCode.Should().Be(0,
					because: "each registration must succeed; child output is inherited by the test host for diagnostics");
			}

			Settings persisted = JsonConvert.DeserializeObject<Settings>(
				File.ReadAllText(Path.Combine(clioHome, "appsettings.json")));
			string[] environmentNames = persisted.Environments.Keys.OrderBy(name => name).ToArray();

			// Assert
			environmentNames.Should().HaveCount(processCount,
				because: "the cross-process file lock must prevent any registration from overwriting another");
			for (int index = 1; index <= processCount; index++) {
				environmentNames.Should().Contain($"env-{index}",
					because: "every process must append its environment to the latest persisted settings");
			}
			Directory.GetFiles(clioHome, "*.tmp").Should().BeEmpty(
				because: "atomic settings replacement must clean up every temporary file");
		}
		finally {
			foreach (Process process in processes) {
				if (!process.HasExited) {
					process.Kill(entireProcessTree: true);
				}
				process.Dispose();
			}
			Directory.Delete(clioHome, recursive: true);
		}
	}
}
