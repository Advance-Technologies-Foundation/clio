using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Xml.Linq;
using Clio.Common;
using Clio.Common.DbHub;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.DbHub;

[TestFixture]
[Platform("Win")]
[Property("Module", "Common")]
public sealed class DbHubInstallerServiceTests : BaseClioModuleTests {
	private string _directory;
	private IProcessExecutor _processExecutor;
	private IDbHubScheduledTaskManager _taskManager;
	private IDbHubHttpClient _httpClient;
	private IDbHubInstallerService _sut;
	private string _installedVersion;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_processExecutor = Substitute.For<IProcessExecutor>();
		_taskManager = Substitute.For<IDbHubScheduledTaskManager>();
		_httpClient = Substitute.For<IDbHubHttpClient>();
		containerBuilder.AddSingleton(_processExecutor);
		containerBuilder.AddSingleton(_taskManager);
		containerBuilder.AddSingleton(_httpClient);
	}

	public override void Setup() {
		base.Setup();
		_directory = Path.Combine(Path.GetTempPath(), $"clio-dbhub-install-{Guid.NewGuid():N}");
		string entryDirectory = Path.Combine(_directory, "node_modules", "@bytebase", "dbhub", "dist");
		Directory.CreateDirectory(entryDirectory);
		File.WriteAllText(Path.Combine(entryDirectory, "index.js"), string.Empty);
		_installedVersion = "0.22.0";
		_processExecutor.ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>()).Returns(call =>
			Task.FromResult(ResultFor(call.Arg<ProcessExecutionOptions>())));
		_taskManager.EnsureAndStart(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DbHubSettings>(), out Arg.Any<string>())
			.Returns(call => { call[3] = null; return true; });
		_taskManager.IsCompatible(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DbHubSettings>()).Returns(true);
		_taskManager.StopIfExists(out Arg.Any<string>()).Returns(call => { call[0] = null; return true; });
		_httpClient.VerifyServer(Arg.Any<DbHubSettings>()).Returns(new DbHubVerificationResult(true, true));
		_sut = Container.GetRequiredService<IDbHubInstallerService>();
	}

	public override void TearDown() {
		if (Directory.Exists(_directory)) {
			Directory.Delete(_directory, recursive: true);
		}
		_processExecutor.ClearReceivedCalls();
		_taskManager.ClearReceivedCalls();
		_httpClient.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Repairs a different globally installed dbHub version to the release-pinned version.")]
	public void InstallOrRepair_ShouldInstallPinnedVersion_WhenInstalledVersionDiffers() {
		// Arrange
		DbHubInstallRequest request = Request();

		// Act
		DbHubInstallationResult result = _sut.InstallOrRepair(request);

		// Assert
		result.Success.Should().BeTrue(because: "all installation and verification dependencies succeeded");
		_processExecutor.Received(1).ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Program == "cmd.exe"
			&& options.Arguments == "/d /s /c \"npm.cmd install -g @bytebase/dbhub@0.23.0\""));
		_taskManager.Received(1).EnsureAndStart(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Is<DbHubSettings>(settings => settings.Host == "127.0.0.1" && settings.Port == 17999),
			out Arg.Any<string>());
		string config = File.ReadAllText(request.ConfigPath);
		config.Should().Contain("# clio-managed-control-source",
			because: "a fresh dbHub server cannot start or hot-reload with an empty sources array");
		config.Should().Contain($"id = \"{DbHubTomlStore.ControlSourceId}\"",
			because: "the harmless in-memory SQLite control source keeps the fresh configuration runnable");
	}

	[Test]
	[Description("Repairs a comments-only TOML with a control source while preserving the user's content.")]
	public void InstallOrRepair_ShouldAddControlSource_WhenExistingTomlHasNoSources() {
		// Arrange
		_installedVersion = DbHubInstallerService.PinnedDbHubVersion;
		DbHubInstallRequest request = Request();
		const string comment = "# user comment that must survive\n";
		File.WriteAllText(request.ConfigPath, comment);

		// Act
		DbHubInstallationResult result = _sut.InstallOrRepair(request);

		// Assert
		result.Success.Should().BeTrue(because: "clio can safely make a syntactically valid comments-only file runnable");
		string config = File.ReadAllText(request.ConfigPath);
		config.Should().StartWith(comment, because: "installer repair must preserve existing user comments and ordering");
		config.Should().Contain($"id = \"{DbHubTomlStore.ControlSourceId}\"",
			because: "dbHub 0.23 requires at least one source at startup");
	}

	[Test]
	[Description("Rejects non-loopback HTTP binding before running any installer process.")]
	public void InstallOrRepair_ShouldRejectNonLoopbackHost() {
		// Arrange
		DbHubInstallRequest request = Request() with { Host = "0.0.0.0" };

		// Act
		DbHubInstallationResult result = _sut.InstallOrRepair(request);

		// Assert
		result.Success.Should().BeFalse(because: "dbHub HTTP has no authentication and must remain loopback-only");
		_processExecutor.DidNotReceive().ExecuteAndCaptureAsync(Arg.Any<ProcessExecutionOptions>());
	}

	[Test]
	[Description("Adopts an already pinned installation without reinstalling or rewriting valid user TOML.")]
	public void InstallOrRepair_ShouldAdoptPinnedInstallation() {
		// Arrange
		_installedVersion = DbHubInstallerService.PinnedDbHubVersion;
		DbHubInstallRequest request = Request();
		const string userToml = "# user-owned\n[[sources]]\nid = \"manual\"\ntype = \"postgres\"\ndsn = \"postgres://manual\"\n[[tools]]\nname = \"execute_sql\"\nsource = \"manual\"\nreadonly = true\n";
		File.WriteAllText(request.ConfigPath, userToml);

		// Act
		DbHubInstallationResult result = _sut.InstallOrRepair(request);

		// Assert
		result.Success.Should().BeTrue(because: "the existing package, TOML, and live server are valid");
		_processExecutor.DidNotReceive().ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments.Contains("npm.cmd install", StringComparison.Ordinal)));
		File.ReadAllText(request.ConfigPath).Should().Be(userToml,
			because: "adoption must never rewrite an existing valid user configuration");
		_taskManager.DidNotReceive().StopIfExists(out Arg.Any<string>());
		_taskManager.DidNotReceive().EnsureAndStart(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<DbHubSettings>(), out Arg.Any<string>());
	}

	[Test]
	[Description("Rejects a port already owned by a non-dbHub process before installation state changes.")]
	public void InstallOrRepair_ShouldRejectPortConflict() {
		// Arrange
		using TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		_httpClient.VerifyServer(Arg.Any<DbHubSettings>()).Returns(new DbHubVerificationResult(false, false));
		DbHubInstallRequest request = new(Path.Combine(_directory, "conflict.toml"), "127.0.0.1", port, true);

		// Act
		DbHubInstallationResult result = _sut.InstallOrRepair(request);

		// Assert
		result.Success.Should().BeFalse(because: "clio must not replace an unrelated listener");
		result.Message.Should().Contain("already in use", because: "the user needs an actionable port diagnosis");
		File.Exists(request.ConfigPath).Should().BeFalse(because: "port validation precedes configuration mutation");
	}

	[Test]
	[Description("Refuses invalid existing TOML without creating or repairing the Scheduled Task.")]
	public void InstallOrRepair_ShouldRejectInvalidExistingToml() {
		// Arrange
		_installedVersion = DbHubInstallerService.PinnedDbHubVersion;
		DbHubInstallRequest request = Request();
		const string invalidToml = "[[sources]\nid = \"broken\"";
		File.WriteAllText(request.ConfigPath, invalidToml);

		// Act
		DbHubInstallationResult result = _sut.InstallOrRepair(request);

		// Assert
		result.Success.Should().BeFalse(because: "the adopted configuration is not valid TOML");
		File.ReadAllText(request.ConfigPath).Should().Be(invalidToml,
			because: "failed adoption must leave the user's original file intact");
		_taskManager.DidNotReceive().EnsureAndStart(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<DbHubSettings>(), out Arg.Any<string>());
		_processExecutor.DidNotReceive().ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments.Contains("npm.cmd install", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Refuses to adopt a healthy listener that is not owned by the exact clio Scheduled Task.")]
	public void InstallOrRepair_ShouldRejectUnownedHealthyListener() {
		// Arrange
		_taskManager.IsCompatible(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DbHubSettings>()).Returns(false);
		DbHubInstallRequest request = Request();

		// Act
		DbHubInstallationResult result = _sut.InstallOrRepair(request);

		// Assert
		result.Success.Should().BeFalse(because: "a healthy response does not prove process ownership or config identity");
		result.Message.Should().Contain("not owned", because: "the user needs to stop the conflicting listener");
		_processExecutor.DidNotReceive().ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments.Contains("npm.cmd install", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Generated Scheduled Task XML directly invokes Node with loopback and explicit config arguments.")]
	public void CreateTaskDocument_ShouldUseDirectSafeArguments() {
		// Arrange
		DbHubSettings settings = new() { ConfigPath = @"C:\Users\tester\dbhub.toml", Host = "127.0.0.1", Port = 7999 };

		// Act
		XDocument document = DbHubScheduledTaskManager.CreateTaskDocument(@"C:\Program Files\nodejs\node.exe",
			@"C:\npm\node_modules\@bytebase\dbhub\dist\index.js", settings);

		// Assert
		XNamespace ns = document.Root.Name.Namespace;
		document.Descendants(ns + "Command").Single().Value.Should().Be(@"C:\Program Files\nodejs\node.exe",
			because: "the task must not depend on shell or profile expansion");
		string arguments = document.Descendants(ns + "Arguments").Single().Value;
		arguments.Should().Contain("--host 127.0.0.1", because: "the unauthenticated server must bind to loopback");
		arguments.Should().Contain("--config \"C:\\Users\\tester\\dbhub.toml\"",
			because: "the task must use the adopted explicit TOML path");
		document.Descendants(ns + "Hidden").Single().Value.Should().Be("true",
			because: "the background MCP server must not open an interactive window at logon");
		document.Descendants(ns + "LogonType").Single().Value.Should().Be("InteractiveToken",
			because: "the task must run only in the current user session without storing credentials");
		document.Descendants(ns + "RunLevel").Single().Value.Should().Be("LeastPrivilege",
			because: "dbHub does not require an elevated task");
	}

	[Test]
	[Description("The exact generated Scheduled Task document satisfies every adoption invariant.")]
	public void IsTaskDocumentCompatible_ShouldReturnTrue_ForGeneratedDocument() {
		// Arrange
		DbHubSettings settings = new() { ConfigPath = @"C:\Users\tester\dbhub.toml", Host = "127.0.0.1", Port = 7999 };
		XDocument expected = DbHubScheduledTaskManager.CreateTaskDocument(@"C:\Program Files\nodejs\node.exe",
			@"C:\npm\node_modules\@bytebase\dbhub\dist\index.js", settings);
		XDocument actual = XDocument.Parse(expected.ToString());

		// Act
		bool compatible = DbHubScheduledTaskManager.IsTaskDocumentCompatible(actual, expected);

		// Assert
		compatible.Should().BeTrue(because: "an unchanged clio-created task must be adopted idempotently");
	}

	[TestCase("Enabled", "false")]
	[TestCase("Hidden", "false")]
	[TestCase("StartWhenAvailable", "false")]
	[TestCase("ExecutionTimeLimit", "PT1H")]
	[TestCase("MultipleInstancesPolicy", "Parallel")]
	[Description("Scheduled Task adoption rejects drift in every required trigger and durability setting.")]
	public void IsTaskDocumentCompatible_ShouldReturnFalse_WhenOwnedSettingDrifts(string elementName,
		string driftedValue) {
		// Arrange
		DbHubSettings settings = new() { ConfigPath = @"C:\Users\tester\dbhub.toml", Host = "127.0.0.1", Port = 7999 };
		XDocument expected = DbHubScheduledTaskManager.CreateTaskDocument(@"C:\Program Files\nodejs\node.exe",
			@"C:\npm\node_modules\@bytebase\dbhub\dist\index.js", settings);
		XDocument actual = XDocument.Parse(expected.ToString());
		XNamespace ns = actual.Root.Name.Namespace;
		actual.Descendants(ns + elementName).Single().Value = driftedValue;

		// Act
		bool compatible = DbHubScheduledTaskManager.IsTaskDocumentCompatible(actual, expected);

		// Assert
		compatible.Should().BeFalse(because: "a drifted owned task contract must be repaired before adoption");
	}

	[TestCase(true, true, DbHubScheduledTaskManager.LegacyTaskName, DbHubScheduledTaskManager.TaskName)]
	[TestCase(true, false, DbHubScheduledTaskManager.LegacyTaskName, null)]
	[TestCase(false, true, DbHubScheduledTaskManager.TaskName, null)]
	[TestCase(false, false, DbHubScheduledTaskManager.TaskName, null)]
	[Description("Scheduled Task adoption selects one active task and identifies a duplicate for cleanup.")]
	public void SelectTaskNames_ShouldReturnSingleActiveTask(bool legacyExists, bool canonicalExists,
		string expectedActive, string expectedRedundant) {
		// Act
		(string active, string redundant) = DbHubScheduledTaskManager.SelectTaskNames(legacyExists, canonicalExists);

		// Assert
		active.Should().Be(expectedActive, because: "existing legacy installations are adopted idempotently");
		redundant.Should().Be(expectedRedundant,
			because: "two known logon tasks could race to bind the same local dbHub port");
	}

	private DbHubInstallRequest Request() => new(Path.Combine(_directory, "dbhub.toml"), "127.0.0.1", 17999, true);

	private ProcessExecutionResult ResultFor(ProcessExecutionOptions options) {
		string output = (options.Program, options.Arguments) switch {
			("node", "--version") => "v22.5.0",
			("cmd.exe", "/d /s /c \"npm.cmd --version\"") => "10.8.0",
			("cmd.exe", "/d /s /c \"npm.cmd list -g @bytebase/dbhub --depth=0 --json\"") => $"{{\"dependencies\":{{\"@bytebase/dbhub\":{{\"version\":\"{_installedVersion}\"}}}}}}",
			("cmd.exe", "/d /s /c \"npm.cmd prefix -g\"") => _directory,
			("where.exe", "node.exe") => @"C:\Program Files\nodejs\node.exe",
			_ => string.Empty
		};
		return new ProcessExecutionResult { Started = true, ExitCode = 0, StandardOutput = output };
	}
}
