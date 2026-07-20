using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClioRing.Services;
using ClioRing.Ipc;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace ClioRing.Tests;

[TestFixture]
[Category("Unit")]
public sealed class ClioToolUpdateServiceTests {
	private IClioToolProcessRunner _runner = null!;
	private IClioToolProcessInspector _inspector = null!;
	private IClioToolInstallation _installation = null!;
	private IClioUpdateStateStore _stateStore = null!;
	private ClioToolUpdateService _sut = null!;
	private string _installedVersion = null!;
	private const string TargetPath = "C:\\Users\\test\\.dotnet\\tools\\clio.exe";

	[SetUp]
	public void SetUp() {
		_runner = Substitute.For<IClioToolProcessRunner>();
		_inspector = Substitute.For<IClioToolProcessInspector>();
		_installation = Substitute.For<IClioToolInstallation>();
		_stateStore = Substitute.For<IClioUpdateStateStore>();
		_installedVersion = "8.1.0.84";
		_installation.TargetPath.Returns(TargetPath);
		_installation.DotNetHostPath.Returns("C:\\Program Files\\dotnet\\dotnet.exe");
		_installation.IsInstalled.Returns(true);
		_inspector.FindLockingTrustedProcesses(Arg.Any<string>()).Returns(Array.Empty<ClioToolProcess>());
		_runner.RunAsync("C:\\Program Files\\dotnet\\dotnet.exe",
			Arg.Is<IReadOnlyList<string>>(arguments => IsInventoryCommand(arguments)), Arg.Any<TimeSpan>(),
			Arg.Any<CancellationToken>()).Returns(_ => InventoryResult(_installedVersion));
		var services = new ServiceCollection();
		services.AddSingleton(new HttpClient(new StaticJsonHandler(RegistrationJson)));
		services.AddSingleton(_runner);
		services.AddSingleton(_inspector);
		services.AddSingleton(_installation);
		services.AddSingleton(_stateStore);
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<IClioProcessGate, ClioProcessGate>();
		services.AddSingleton<ClioToolUpdateService>();
		_sut = services.BuildServiceProvider().GetRequiredService<ClioToolUpdateService>();
	}

	[Test]
	[Description("The checker selects the newest listed stable package and ignores prerelease and unlisted registrations.")]
	public async Task CheckAsync_ShouldSelectLatestListedStableVersion() {
		// Arrange

		// Act
		ClioToolUpdateCheck? result = await _sut.CheckAsync();

		// Assert
		result.Should().NotBeNull(because: "an installed Release tool and a listed stable package are available");
		result!.InstalledVersion.Should().Be("8.1.0.84", because: "the installed store is authoritative");
		result.AvailableVersion.Should().Be("8.1.0.86", because: "unlisted and prerelease packages are not updates");
		result.IsUpdateAvailable.Should().BeTrue(because: "8.1.0.86 is newer than 8.1.0.84");
	}

	[Test]
	[Description("A recent persisted check is reused across Ring restarts without contacting dotnet or NuGet again.")]
	public async Task CheckAsync_ShouldReuseRecentPersistedSnapshot() {
		// Arrange
		var cached = new ClioUpdateState(DateTimeOffset.UtcNow, "8.1.0.84", "8.1.0.86", null);
		_stateStore.Read().Returns(cached);

		// Act
		ClioToolUpdateCheck? result = await _sut.CheckAsync();

		// Assert
		result.Should().NotBeNull(because: "the recent non-sensitive snapshot is still within eight hours");
		result!.AvailableVersion.Should().Be("8.1.0.86", because: "restart throttling preserves the presented update");
		await _runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, default, default);
	}

	[Test]
	[Description("An ordinary updater failure never offers process termination when Restart Manager found no shim locker.")]
	public async Task UpdateAsync_ShouldFailWithoutKillPrompt_WhenFailureIsNotCorroboratedAsLock() {
		// Arrange
		ClioToolUpdateCheck update = NewUpdate();
		_runner.RunAsync(Arg.Any<string>(),
			Arg.Is<IReadOnlyList<string>>(arguments => IsUpdateCommand(arguments)), Arg.Any<TimeSpan>(),
			Arg.Any<CancellationToken>()).Returns(new ClioToolProcessRunResult(1, string.Empty, "feed unavailable"));
		_inspector.FindLockingTrustedProcesses(TargetPath).Returns(Array.Empty<ClioToolProcess>());

		// Act
		ClioToolUpdateResult result = await _sut.UpdateAsync(update);

		// Assert
		result.Outcome.Should().Be(ClioToolUpdateOutcome.Failed,
			because: "running processes may be terminated only when Restart Manager corroborates the lock");
	}

	[Test]
	[Description("An externally changed installed version invalidates the persisted snapshot before any updater runs.")]
	public async Task UpdateAsync_ShouldRequireRefresh_WhenInstalledVersionChangedExternally() {
		// Arrange
		ClioToolUpdateCheck staleUpdate = NewUpdate();
		_installedVersion = "8.1.0.85";
		_stateStore.Read().Returns(new ClioUpdateState(DateTimeOffset.UtcNow, "8.1.0.84",
			"8.1.0.86", "8.1.0.86"));

		// Act
		ClioToolUpdateResult result = await _sut.UpdateAsync(staleUpdate);
		ClioToolUpdateCheck? refreshed = await _sut.CheckAsync(force: true);

		// Assert
		result.Outcome.Should().Be(ClioToolUpdateOutcome.RefreshRequired,
			because: "the presented update is no longer bound to the installed tool inventory");
		await _runner.DidNotReceive().RunAsync(Arg.Any<string>(),
			Arg.Is<IReadOnlyList<string>>(arguments => IsUpdateCommand(arguments)),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
		refreshed!.InstalledVersion.Should().Be("8.1.0.85",
			because: "forced refresh bypasses the stale persisted check even when it remains readable");
		_stateStore.Received().Write(Arg.Is<ClioUpdateState>(state =>
			state != null && state.InstalledVersion == "8.1.0.85"
			&& state.NotifiedVersion == "8.1.0.86"));
	}

	[Test]
	[Description("An update invokes dotnet without a shell, pins the presented version, and verifies the installed store afterward.")]
	public async Task UpdateAsync_ShouldInstallAndVerifyPresentedVersion() {
		// Arrange
		ClioToolUpdateCheck update = NewUpdate();
		string? isolatedConfig = null;
		string? isolatedConfigPath = null;
		_runner.RunAsync("C:\\Program Files\\dotnet\\dotnet.exe",
			Arg.Is<IReadOnlyList<string>>(arguments => IsUpdateCommand(arguments)), Arg.Any<TimeSpan>(),
			Arg.Any<CancellationToken>()).Returns(_ => {
				IReadOnlyList<string> arguments = _.ArgAt<IReadOnlyList<string>>(1);
				int configIndex = arguments.ToList().IndexOf("--configfile") + 1;
				isolatedConfigPath = arguments[configIndex];
				isolatedConfig = File.ReadAllText(isolatedConfigPath);
				_installedVersion = update.AvailableVersion;
				return new ClioToolProcessRunResult(0, "updated", string.Empty);
			});

		// Act
		ClioToolUpdateResult result = await _sut.UpdateAsync(update);

		// Assert
		result.Outcome.Should().Be(ClioToolUpdateOutcome.Success,
			because: "the exact presented version was installed and verified");
		await _runner.Received(1).RunAsync("C:\\Program Files\\dotnet\\dotnet.exe",
			Arg.Is<IReadOnlyList<string>>(arguments =>
				arguments != null && IsUpdateCommand(arguments) && arguments.Contains("--configfile")),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
		isolatedConfig.Should().Contain("<clear />",
			because: "ambient machine and user package sources must not participate");
		isolatedConfig.Should().Contain("https://api.nuget.org/v3/index.json",
			because: "the presented package version comes from NuGet.org");
		isolatedConfigPath.Should().NotBeNull(because: "the updater receives an explicit config path");
		File.Exists(isolatedConfigPath!).Should().BeFalse(because: "the credential-free temporary config is cleaned up");
	}

	[Test]
	[Description("A failed tool update returns only trusted clio processes as an immutable confirmation snapshot.")]
	public async Task UpdateAsync_ShouldReturnTrustedProcesses_WhenToolIsLocked() {
		// Arrange
		ClioToolUpdateCheck update = NewUpdate();
		var process = new ClioToolProcess(20736, 1234, TargetPath, "clio mcp-server",
			"Started by Claude Code - claude.exe (PID 115220)");
		_runner.RunAsync(Arg.Any<string>(), Arg.Is<IReadOnlyList<string>>(arguments => IsUpdateCommand(arguments)), Arg.Any<TimeSpan>(),
			Arg.Any<CancellationToken>()).Returns(new ClioToolProcessRunResult(1, string.Empty, "locked"));
		_inspector.FindLockingTrustedProcesses(TargetPath).Returns(new[] { process });

		// Act
		ClioToolUpdateResult result = await _sut.UpdateAsync(update);

		// Assert
		result.Outcome.Should().Be(ClioToolUpdateOutcome.Blocked,
			because: "the trusted Release shim is held by a running clio process");
		result.Processes.Should().ContainSingle().Which.Should().Be(process,
			because: "the user must confirm the exact identity snapshot before termination");
		await _runner.DidNotReceive().RunAsync(Arg.Any<string>(),
			Arg.Is<IReadOnlyList<string>>(arguments => IsUpdateCommand(arguments)),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Kill and retry revalidates each confirmed process, terminates no parent, and performs exactly one retry.")]
	public async Task TerminateAndRetryAsync_ShouldRevalidateThenRetryOnce() {
		// Arrange
		ClioToolUpdateCheck update = NewUpdate();
		var process = new ClioToolProcess(20736, 1234, TargetPath, "clio mcp-server",
			"Started by Codex - codex.exe (PID 55104)");
		_inspector.TerminateRevalidatedAsync(process, Arg.Any<CancellationToken>()).Returns(true);
		_runner.RunAsync(Arg.Any<string>(), Arg.Is<IReadOnlyList<string>>(arguments => IsUpdateCommand(arguments)), Arg.Any<TimeSpan>(),
			Arg.Any<CancellationToken>()).Returns(_ => {
				_installedVersion = update.AvailableVersion;
				return new ClioToolProcessRunResult(0, "updated", string.Empty);
			});

		// Act
		ClioToolUpdateResult result = await _sut.TerminateAndRetryAsync(update, new[] { process });

		// Assert
		result.Outcome.Should().Be(ClioToolUpdateOutcome.Success,
			because: "the confirmed process still matched and the one retry succeeded");
		await _inspector.Received(1).TerminateRevalidatedAsync(process, Arg.Any<CancellationToken>());
		await _runner.Received(1).RunAsync(Arg.Any<string>(), Arg.Is<IReadOnlyList<string>>(arguments => IsUpdateCommand(arguments)),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A stale PID or changed executable aborts the operation before the update retry.")]
	public async Task TerminateAndRetryAsync_ShouldAbort_WhenProcessIdentityChanged() {
		// Arrange
		ClioToolUpdateCheck update = NewUpdate();
		var process = new ClioToolProcess(20736, 1234, TargetPath, "clio mcp-server",
			"Started by Claude Code - claude.exe (PID 115220)");
		_inspector.TerminateRevalidatedAsync(process, Arg.Any<CancellationToken>()).Returns(false);

		// Act
		ClioToolUpdateResult result = await _sut.TerminateAndRetryAsync(update, new[] { process });

		// Assert
		result.Outcome.Should().Be(ClioToolUpdateOutcome.Failed,
			because: "Ring must fail closed when PID identity no longer matches");
		await _runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, default, default);
	}

	[TestCase("clio.exe mcp-server", "clio mcp-server")]
	[TestCase("clio.exe reg-web-app --password secret", "clio process")]
	[Description("Process command presentation classifies MCP servers without exposing arbitrary secret-bearing arguments.")]
	public void ClassifyCommand_ShouldReturnSecretFreeSummary(string commandLine, string expected) {
		// Arrange

		// Act
		string result = ClioToolProcessInspector.ClassifyCommand(commandLine);

		// Assert
		result.Should().Be(expected, because: "the blocker UI must never echo arbitrary clio arguments");
	}

	private ClioToolUpdateCheck NewUpdate() => new(_installedVersion, "8.1.0.86", TargetPath);

	private static bool IsInventoryCommand(IReadOnlyList<string>? arguments) =>
		arguments is not null && string.Join(" ", arguments) == "tool list --global --format json";

	private static bool IsUpdateCommand(IReadOnlyList<string>? arguments) =>
		arguments is not null && arguments.Count >= 8
		&& string.Join(" ", arguments.Take(6)) == "tool update --global clio --version 8.1.0.86";

	private static ClioToolProcessRunResult InventoryResult(string version) => new(0,
		$"{{\"version\":1,\"data\":[{{\"packageId\":\"clio\",\"version\":\"{version}\",\"commands\":[\"clio\"]}}]}}",
		string.Empty);

	private const string RegistrationJson = """
	{
	  "items": [{
	    "items": [
	      { "catalogEntry": { "version": "8.1.0.84", "listed": true } },
	      { "catalogEntry": { "version": "8.1.0.85-beta", "listed": true } },
	      { "catalogEntry": { "version": "8.1.0.99", "listed": false } },
	      { "catalogEntry": { "version": "8.1.0.86", "listed": true } }
	    ]
	  }]
	}
	""";

	private sealed class StaticJsonHandler(string json) : HttpMessageHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken) {
			var response = new HttpResponseMessage(HttpStatusCode.OK) {
				Content = new StringContent(json)
			};
			return Task.FromResult(response);
		}
	}
}
