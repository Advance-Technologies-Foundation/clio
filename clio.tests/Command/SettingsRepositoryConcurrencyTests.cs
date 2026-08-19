using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
				ProcessStartInfo startInfo = new("dotnet") {
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				startInfo.Environment["CLIO_HOME"] = clioHome;
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
					because: $"each registration must succeed; stderr was: {process.StandardError.ReadToEnd()}");
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
