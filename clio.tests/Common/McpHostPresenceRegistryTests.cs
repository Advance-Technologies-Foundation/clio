using System;
using System.IO.Abstractions.TestingHelpers;
using Clio.Common;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class McpHostPresenceRegistryTests {
	private const string UserHome = "/test-user-home";

	private static string MarkerFolder =>
		System.IO.Path.Combine(UserHome, ".clio", McpHostPresenceRegistry.MarkerFolderName);

	private static string MarkerPath(int processId) =>
		System.IO.Path.Combine(MarkerFolder, $"mcp-server.{processId}.lock");

	/// <summary>A home provider pinned to a fixed path, so markers never touch the real user profile.</summary>
	private static Clio.Common.Skills.IUserHomeProvider HomeProvider(string userHome = UserHome) {
		Clio.Common.Skills.IUserHomeProvider provider =
			Substitute.For<Clio.Common.Skills.IUserHomeProvider>();
		provider.GetClioDir().Returns(System.IO.Path.Combine(userHome, ".clio"));
		return provider;
	}

	[Test]
	[Description("Writes a marker naming the current process, its clio version and its start time, so another clio process can see the resident host.")]
	public void Register_Should_Write_Marker_For_Current_Process() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		McpHostPresenceRegistry registry = new(fileSystem, probe, HomeProvider());

		// Act
		string markerFilePath = registry.Register();

		// Assert
		markerFilePath.Should().Be(MarkerPath(Environment.ProcessId),
			because: "the marker is keyed on the process id so a scan can test liveness without reading the file");
		string content = fileSystem.File.ReadAllText(markerFilePath);
		content.Should().Contain("\"pid\"",
			because: "the marker records the process identifier it claims");
		content.Should().Contain("clio-version",
			because: "the deferral message names the version of the host it defers to");
		content.Should().Contain("started-at-utc",
			because: "the start time tells an operator how long the host has been resident");
	}

	[Test]
	[Description("Removes the marker so a cleanly stopped host stops deferring clio self-updates.")]
	public void Unregister_Should_Delete_The_Marker() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		McpHostPresenceRegistry registry = new(fileSystem, probe, HomeProvider());
		string markerFilePath = registry.Register();

		// Act
		registry.Unregister(markerFilePath);

		// Assert
		fileSystem.File.Exists(markerFilePath).Should().BeFalse(
			because: "a stopped host must leave nothing behind that would defer the next update");
	}

	[Test]
	[Description("Reports a marker whose process is still running as a live resident host.")]
	public void FindLiveHost_Should_Return_Marker_When_Process_Is_Alive() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(MarkerPath(4242), new MockFileData(
			"{\"pid\":4242,\"clio-version\":\"8.1.0.120\",\"started-at-utc\":\"2026-09-12T10:00:00.0000000+00:00\"}"));
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		probe.IsAlive(4242, Arg.Any<DateTimeOffset?>()).Returns(true);
		McpHostPresenceRegistry registry = new(fileSystem, probe, HomeProvider());

		// Act
		McpHostPresenceMarker marker = registry.FindLiveHost();

		// Assert
		marker.Should().NotBeNull(
			because: "a live resident host is exactly what the startup update check has to see");
		marker!.ProcessId.Should().Be(4242,
			because: "the deferral message names the process the update is deferred for");
		marker.ClioVersion.Should().Be("8.1.0.120",
			because: "the recorded version identifies which build is resident");
	}

	[Test]
	[Description("Ignores and deletes a marker whose process is gone, so a killed host cannot defer updates forever.")]
	public void FindLiveHost_Should_Delete_Stale_Marker_And_Report_No_Host() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		string stalePath = MarkerPath(4242);
		fileSystem.AddFile(stalePath, new MockFileData("{\"pid\":4242,\"clio-version\":\"8.1.0.120\"}"));
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		probe.IsAlive(4242, Arg.Any<DateTimeOffset?>()).Returns(false);
		McpHostPresenceRegistry registry = new(fileSystem, probe, HomeProvider());

		// Act
		McpHostPresenceMarker marker = registry.FindLiveHost();

		// Assert
		marker.Should().BeNull(
			because: "a marker left by a dead process is not a resident host");
		fileSystem.File.Exists(stalePath).Should().BeFalse(
			because: "a stale marker must be removed or it would defer every future update");
	}

	[Test]
	[Description("Reports no host when the clio home folder holds no marker at all.")]
	public void FindLiveHost_Should_Report_No_Host_When_No_Marker_Exists() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddDirectory(MarkerFolder);
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		McpHostPresenceRegistry registry = new(fileSystem, probe, HomeProvider());

		// Act
		McpHostPresenceMarker marker = registry.FindLiveHost();

		// Assert
		marker.Should().BeNull(
			because: "no marker means no resident host and the update must proceed as before");
		probe.DidNotReceive().IsAlive(Arg.Any<int>(), Arg.Any<DateTimeOffset?>());
	}

	[Test]
	[Description("Skips a file in the clio home whose name is not a process marker.")]
	public void FindLiveHost_Should_Ignore_Files_That_Are_Not_Markers() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(
			System.IO.Path.Combine(MarkerFolder, "mcp-server.not-a-pid.lock"),
			new MockFileData("{}"));
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		McpHostPresenceRegistry registry = new(fileSystem, probe, HomeProvider());

		// Act
		McpHostPresenceMarker marker = registry.FindLiveHost();

		// Assert
		marker.Should().BeNull(
			because: "a file that carries no process identifier cannot claim a resident host");
		probe.DidNotReceive().IsAlive(Arg.Any<int>(), Arg.Any<DateTimeOffset?>());
	}

	[Test]
	[Description("Reports the running test process as alive, so the probe's positive answer is proven against a real process rather than a substitute.")]
	public void IsAlive_Should_Report_The_Current_Process_As_Alive() {
		// Arrange
		ProcessLivenessProbe probe = new();

		// Act
		bool alive = probe.IsAlive(Environment.ProcessId);

		// Assert
		alive.Should().BeTrue(
			because: "the process running this assertion is, definitionally, running");
	}

	[Test]
	[Description("Accepts the current process when the marker was written after it started, which is what a real host's marker looks like.")]
	public void IsAlive_Should_Accept_The_Current_Process_When_The_Marker_Is_Newer_Than_It() {
		// Arrange
		ProcessLivenessProbe probe = new();

		// Act
		bool alive = probe.IsAlive(Environment.ProcessId, DateTimeOffset.UtcNow);

		// Assert
		alive.Should().BeTrue(
			because: "a marker is always written after the process it describes started, so this is the normal case and must not be read as a recycled pid");
	}

	[Test]
	[Description("Rejects the current process when the marker claims a start time long before it, which is what a recycled process identifier looks like.")]
	public void IsAlive_Should_Reject_The_Current_Process_When_The_Marker_Predates_It() {
		// Arrange
		ProcessLivenessProbe probe = new();

		// Act
		bool alive = probe.IsAlive(Environment.ProcessId, new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero));

		// Assert
		alive.Should().BeFalse(
			because: "a process that started decades after the marker was written cannot be the process the marker describes");
	}

	[Test]
	[Description("Reports a process identifier that cannot exist as not alive.")]
	public void IsAlive_Should_Report_An_Impossible_Process_Id_As_Not_Alive() {
		// Arrange
		ProcessLivenessProbe probe = new();

		// Act
		bool aliveForZero = probe.IsAlive(0);
		bool aliveForNegative = probe.IsAlive(-1);

		// Assert
		aliveForZero.Should().BeFalse(
			because: "zero is not a process identifier and must never keep a marker alive");
		aliveForNegative.Should().BeFalse(
			because: "a negative identifier is not a process either");
	}

	[Test]
	[Description("Deletes a marker whose body cannot be parsed, so an empty or hand-created file cannot defer clio updates forever.")]
	public void FindLiveHost_Should_Delete_A_Marker_With_An_Unparsable_Body() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		string markerPath = MarkerPath(1);
		fileSystem.AddFile(markerPath, new MockFileData(string.Empty));
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		McpHostPresenceRegistry registry = new(fileSystem, probe, HomeProvider());

		// Act
		McpHostPresenceMarker marker = registry.FindLiveHost();

		// Assert
		marker.Should().BeNull(
			because: "a marker with no readable identity claims nothing");
		fileSystem.File.Exists(markerPath).Should().BeFalse(
			because: "`touch mcp-server.1.lock` must not be able to disable clio updates on the machine permanently");
		probe.DidNotReceive().IsAlive(Arg.Any<int>(), Arg.Any<DateTimeOffset?>());
	}

	[Test]
	[Description("Refuses a marker with no start time, because without one the recycled-identifier guard would silently switch itself off.")]
	public void FindLiveHost_Should_Refuse_A_Marker_Without_A_Start_Time() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		string markerPath = MarkerPath(4242);
		fileSystem.AddFile(markerPath, new MockFileData("{\"pid\":4242,\"clio-version\":\"8.1.0.120\"}"));
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		McpHostPresenceRegistry registry = new(fileSystem, probe, HomeProvider());

		// Act
		McpHostPresenceMarker marker = registry.FindLiveHost();

		// Assert
		marker.Should().BeNull(
			because: "a marker that cannot be checked against a recycled identifier is not trustworthy enough to defer an update on");
		fileSystem.File.Exists(markerPath).Should().BeFalse(
			because: "an untrustworthy marker must not survive to be re-read on every future run");
	}

	[Test]
	[Description("Refuses to read a marker larger than a marker can be, so a file swapped for a large or endless one cannot stall every clio command.")]
	public void FindLiveHost_Should_Refuse_An_Oversized_Marker() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		string markerPath = MarkerPath(4242);
		fileSystem.AddFile(markerPath,
			new MockFileData(new string('x', McpHostPresenceRegistry.MaxMarkerBytes + 1)));
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		McpHostPresenceRegistry registry = new(fileSystem, probe, HomeProvider());

		// Act
		McpHostPresenceMarker marker = registry.FindLiveHost();

		// Assert
		marker.Should().BeNull(
			because: "a marker holds three short fields; anything larger is not one and must not be read");
		probe.DidNotReceive().IsAlive(Arg.Any<int>(), Arg.Any<DateTimeOffset?>());
	}

	[Test]
	[Description("Keeps scanning after an unusable marker, so one bad file cannot hide a live host and let an update replace its binaries.")]
	public void FindLiveHost_Should_Keep_Scanning_After_An_Unusable_Marker() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(MarkerPath(1), new MockFileData("not json at all"));
		fileSystem.AddFile(MarkerPath(4242), new MockFileData(
			"{\"pid\":4242,\"clio-version\":\"8.1.0.120\",\"started-at-utc\":\"2026-09-12T10:00:00.0000000+00:00\"}"));
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		probe.IsAlive(4242, Arg.Any<DateTimeOffset?>()).Returns(true);
		McpHostPresenceRegistry registry = new(fileSystem, probe, HomeProvider());

		// Act
		McpHostPresenceMarker marker = registry.FindLiveHost();

		// Assert
		marker.Should().NotBeNull(
			because: "the live host is what decides whether a self-update is safe, and it is listed after the unusable file");
		marker!.ProcessId.Should().Be(4242,
			because: "the scan must report the marker that names a live process");
	}

	[Test]
	[Description("Keeps markers outside the clio home, so a host started with a custom CLIO_HOME is still visible to a clio started without one.")]
	public void Register_Should_Write_The_Marker_Outside_The_Clio_Home() {
		// Arrange
		// The updater replaces ONE per-user tool installation however many clio homes exist, so a marker
		// scoped to a clio home would leave a host invisible to the process able to overwrite it.
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		McpHostPresenceRegistry registry = new(fileSystem, probe, HomeProvider());

		// Act
		string markerFilePath = registry.Register();

		// Assert
		markerFilePath.Should().StartWith(MarkerFolder,
			because: "the marker belongs to the user, like the tool installation it guards");
		markerFilePath.Should().NotContain(SettingsRepository.AppSettingsFolderPath,
			because: "a CLIO_HOME-relative marker is invisible to a clio started with a different (or no) CLIO_HOME");
	}

	[Test]
	[Description("A host writing under one settings folder is found by a registry reading under another, because both resolve the same per-user marker root.")]
	public void FindLiveHost_Should_See_A_Host_Registered_From_A_Different_Settings_Folder() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessLivenessProbe writerProbe = Substitute.For<IProcessLivenessProbe>();
		McpHostPresenceRegistry hostRegistry = new(fileSystem, writerProbe, HomeProvider());
		string originalFolder = SettingsRepository.AppSettingsFolderPath;
		string markerFilePath = hostRegistry.Register();
		IProcessLivenessProbe readerProbe = Substitute.For<IProcessLivenessProbe>();
		readerProbe.IsAlive(Environment.ProcessId, Arg.Any<DateTimeOffset?>()).Returns(true);
		McpHostPresenceRegistry cliRegistry = new(fileSystem, readerProbe, HomeProvider());

		// Act
		McpHostPresenceMarker marker = cliRegistry.FindLiveHost();

		// Assert
		markerFilePath.Should().NotBeNull(
			because: "the host must have written a marker for the scan to find");
		marker.Should().NotBeNull(
			because: "a host and a CLI that disagree about CLIO_HOME still share one tool installation, so they must share the marker root");
		marker!.ProcessId.Should().Be(Environment.ProcessId,
			because: "the marker names the process that wrote it");
		SettingsRepository.AppSettingsFolderPath.Should().Be(originalFolder,
			because: "the marker root must not depend on the settings folder at all");
	}

	[Test]
	[Description("Skips a marker whose fields hold the wrong JSON type and keeps scanning, instead of failing the whole scan and reporting no host at all.")]
	public void FindLiveHost_Should_Skip_A_Wrong_Typed_Marker_And_Find_The_Live_One() {
		// Arrange
		// Value<string>() on an object throws InvalidCastException, which is not a JsonException: it used
		// to escape the scan entirely, so one mangled file hid every live host on the machine.
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		string malformedPath = MarkerPath(1);
		fileSystem.AddFile(malformedPath, new MockFileData(
			"{\"pid\":1,\"clio-version\":{},\"started-at-utc\":\"2026-09-12T10:00:00.0000000+00:00\"}"));
		fileSystem.AddFile(MarkerPath(4242), new MockFileData(
			"{\"pid\":4242,\"clio-version\":\"8.1.0.120\",\"started-at-utc\":\"2026-09-12T10:00:00.0000000+00:00\"}"));
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		probe.IsAlive(4242, Arg.Any<DateTimeOffset?>()).Returns(true);
		McpHostPresenceRegistry registry = new(fileSystem, probe, HomeProvider());

		// Act
		McpHostPresenceMarker marker = registry.FindLiveHost();

		// Assert
		marker.Should().NotBeNull(
			because: "a live host must still be found when an unrelated marker file is malformed");
		marker!.ProcessId.Should().Be(4242,
			because: "the scan must report the marker that names a live process");
		fileSystem.File.Exists(malformedPath).Should().BeFalse(
			because: "a marker whose fields hold the wrong type can never name a live process and must not be re-read forever");
	}
}
