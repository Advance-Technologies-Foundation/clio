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
	private static string MarkerPath(int processId) =>
		System.IO.Path.Combine(SettingsRepository.AppSettingsFolderPath, $"mcp-server.{processId}.lock");

	[Test]
	[Description("Writes a marker naming the current process, its clio version and its start time, so another clio process can see the resident host.")]
	public void Register_Should_Write_Marker_For_Current_Process() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		McpHostPresenceRegistry registry = new(fileSystem, probe);

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
		McpHostPresenceRegistry registry = new(fileSystem, probe);
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
		McpHostPresenceRegistry registry = new(fileSystem, probe);

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
		McpHostPresenceRegistry registry = new(fileSystem, probe);

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
		fileSystem.AddDirectory(SettingsRepository.AppSettingsFolderPath);
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		McpHostPresenceRegistry registry = new(fileSystem, probe);

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
			System.IO.Path.Combine(SettingsRepository.AppSettingsFolderPath, "mcp-server.not-a-pid.lock"),
			new MockFileData("{}"));
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		McpHostPresenceRegistry registry = new(fileSystem, probe);

		// Act
		McpHostPresenceMarker marker = registry.FindLiveHost();

		// Assert
		marker.Should().BeNull(
			because: "a file that carries no process identifier cannot claim a resident host");
		probe.DidNotReceive().IsAlive(Arg.Any<int>(), Arg.Any<DateTimeOffset?>());
	}

	[Test]
	[Description("The MCP host writes no presence marker when it runs as a short-lived worker child.")]
	public void RegisterHostPresence_Should_Write_No_Marker_For_A_Worker() {
		// Arrange
		IMcpHostPresenceRegistry registry = Substitute.For<IMcpHostPresenceRegistry>();
		Clio.Command.McpServer.McpServerCommandOptions options = new() { Worker = true };

		// Act
		string markerFilePath =
			Clio.Command.McpServer.McpServerCommand.RegisterHostPresence(options, registry);

		// Assert
		markerFilePath.Should().BeNull(
			because: "a worker lives for one call; one marker per spawned worker would defer updates for a host that is already gone");
		registry.DidNotReceive().Register();
	}

	[Test]
	[Description("Treats a marker whose pid now belongs to a process that started later as stale, so a recycled identifier cannot defer updates forever.")]
	public void FindLiveHost_Should_Reject_A_Process_That_Started_After_The_Marker() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		string markerPath = MarkerPath(4242);
		fileSystem.AddFile(markerPath, new MockFileData(
			"{\"pid\":4242,\"clio-version\":\"8.1.0.120\",\"started-at-utc\":\"2026-09-12T10:00:00.0000000+00:00\"}"));
		IProcessLivenessProbe probe = Substitute.For<IProcessLivenessProbe>();
		probe.IsAlive(4242, Arg.Any<DateTimeOffset?>()).Returns(false);
		McpHostPresenceRegistry registry = new(fileSystem, probe);

		// Act
		McpHostPresenceMarker marker = registry.FindLiveHost();

		// Assert
		marker.Should().BeNull(
			because: "a stranger that inherited the recorded pid is not the host the marker claims");
		probe.Received(1).IsAlive(4242, new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero));
		fileSystem.File.Exists(markerPath).Should().BeFalse(
			because: "the marker no longer describes anything and must not survive the scan");
	}
}
