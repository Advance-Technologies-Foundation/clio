using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClioRing.Ipc;
using ClioRing.Models;
using ClioRing.Services;
using ClioRing.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace ClioRing.Tests;

[TestFixture]
[Category("Unit")]
public sealed class ClioToolUpdateViewModelTests {
	private IClioToolUpdateService _updateService = null!;
	private IClioIpcClient _ipcClient = null!;
	private RingViewModel _sut = null!;
	private ClioToolUpdateCheck _update = null!;
	private const string TargetPath = "C:\\Users\\test\\.dotnet\\tools\\clio.exe";

	[SetUp]
	public void SetUp() {
		_updateService = Substitute.For<IClioToolUpdateService>();
		_ipcClient = Substitute.For<IClioIpcClient>();
		_ipcClient.IsConnected.Returns(true);
		_update = new ClioToolUpdateCheck("8.1.0.84", "8.1.0.86", TargetPath);
		_updateService.CheckAsync(Arg.Any<CancellationToken>()).Returns(_update);
		_sut = ResolveViewModel(new ResolvedClioRuntime(ClioRuntimeMode.Release, ClioIpcSettings.Default));
	}

	[TearDown]
	public void TearDown() {
		_sut.StopWatching();
	}

	[Test]
	[Description("A Release update stops Ring's owned MCP child before invoking the global-tool update.")]
	public async Task RequestClioUpdateCommand_ShouldStopOwnedReleaseChildBeforeUpdating() {
		// Arrange
		_updateService.UpdateAsync(_update, Arg.Any<CancellationToken>()).Returns(
			new ClioToolUpdateResult(ClioToolUpdateOutcome.Success, "installed",
				Array.Empty<ClioToolProcess>()));
		await _sut.CheckForClioUpdateAsync();

		// Act
		await _sut.RequestClioUpdateCommand.ExecuteAsync(null);

		// Assert
		Received.InOrder(() => {
			_ipcClient.PauseForUpdateAsync(Arg.Any<CancellationToken>());
			_updateService.UpdateAsync(_update, Arg.Any<CancellationToken>());
		});
		_sut.IsClioUpdateAvailable.Should().BeFalse(because: "the verified version is now installed");
		_sut.ClioUpdateMessage.Should().Be("installed", because: "the user sees the terminal result");
	}

	[Test]
	[Description("A lock failure renders the exact trusted process snapshot without terminating anything.")]
	public async Task RequestClioUpdateCommand_ShouldShowBlockersWithoutTerminatingThem() {
		// Arrange
		var process = new ClioToolProcess(20736, 1234, TargetPath, "clio mcp-server",
			"Started by Claude Code - claude.exe (PID 115220)");
		_updateService.UpdateAsync(_update, Arg.Any<CancellationToken>()).Returns(
			new ClioToolUpdateResult(ClioToolUpdateOutcome.Blocked, "blocked", new[] { process }));
		await _sut.CheckForClioUpdateAsync();

		// Act
		await _sut.RequestClioUpdateCommand.ExecuteAsync(null);

		// Assert
		_sut.HasClioUpdateBlockers.Should().BeTrue(because: "explicit confirmation is required before termination");
		_sut.ClioUpdateProcesses.Should().ContainSingle().Which.ProcessId.Should().Be(20736,
			because: "the confirmation binds to the process the service inspected");
		await _updateService.DidNotReceiveWithAnyArgs().TerminateAndRetryAsync(default!, default!, default);
	}

	[Test]
	[Description("The explicit kill-and-retry command passes only the displayed immutable snapshot to the service.")]
	public async Task KillClioUpdateBlockersAndRetryCommand_ShouldUseDisplayedSnapshot() {
		// Arrange
		var process = new ClioToolProcess(20736, 1234, TargetPath, "clio mcp-server",
			"Started by Codex - codex.exe (PID 55104)");
		_updateService.UpdateAsync(_update, Arg.Any<CancellationToken>()).Returns(
			new ClioToolUpdateResult(ClioToolUpdateOutcome.Blocked, "blocked", new[] { process }));
		_updateService.TerminateAndRetryAsync(_update, Arg.Any<IReadOnlyList<ClioToolProcess>>(),
			Arg.Any<CancellationToken>()).Returns(new ClioToolUpdateResult(ClioToolUpdateOutcome.Success,
			"installed", Array.Empty<ClioToolProcess>()));
		await _sut.CheckForClioUpdateAsync();
		await _sut.RequestClioUpdateCommand.ExecuteAsync(null);

		// Act
		await _sut.KillClioUpdateBlockersAndRetryCommand.ExecuteAsync(null);

		// Assert
		await _updateService.Received(1).TerminateAndRetryAsync(_update,
			Arg.Is<IReadOnlyList<ClioToolProcess>>(items =>
				items != null && items.Count == 1 && items[0] == process),
			Arg.Any<CancellationToken>());
		_sut.HasClioUpdateBlockers.Should().BeFalse(because: "the verified retry completed");
	}

	[Test]
	[Description("A periodic refresh is suppressed while a destructive blocker snapshot awaits confirmation.")]
	public async Task CheckForClioUpdateAsync_ShouldPreserveBlockedSnapshotUntilUserDecides() {
		// Arrange
		var process = new ClioToolProcess(20736, 1234, TargetPath, "clio mcp-server",
			"Started by Claude Code - claude.exe (PID 115220)");
		_updateService.UpdateAsync(_update, Arg.Any<CancellationToken>()).Returns(
			new ClioToolUpdateResult(ClioToolUpdateOutcome.Blocked, "blocked", new[] { process }));
		await _sut.CheckForClioUpdateAsync();
		await _sut.RequestClioUpdateCommand.ExecuteAsync(null);
		_updateService.CheckAsync(Arg.Any<CancellationToken>()).Returns(
			new ClioToolUpdateCheck("8.1.0.86", "8.1.0.87", TargetPath));

		// Act
		await _sut.CheckForClioUpdateAsync();

		// Assert
		_sut.AvailableClioVersion.Should().Be("8.1.0.86",
			because: "the displayed process confirmation remains bound to its original update snapshot");
		await _updateService.Received(1).CheckAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Development mode leaves its unrelated MCP child running while updating the Release tool.")]
	public async Task RequestClioUpdateCommand_ShouldNotStopDevelopmentChild() {
		// Arrange
		_sut.StopWatching();
		_sut = ResolveViewModel(new ResolvedClioRuntime(ClioRuntimeMode.Development,
			new ClioIpcSettings { Command = "dotnet", Args = new[] { "dev-clio.dll", "mcp-server" } }));
		_updateService.UpdateAsync(_update, Arg.Any<CancellationToken>()).Returns(
			new ClioToolUpdateResult(ClioToolUpdateOutcome.Success, "installed",
				Array.Empty<ClioToolProcess>()));
		await _sut.CheckForClioUpdateAsync();

		// Act
		await _sut.RequestClioUpdateCommand.ExecuteAsync(null);

		// Assert
		await _ipcClient.DidNotReceiveWithAnyArgs().PauseForUpdateAsync(default);
		await _updateService.Received(1).UpdateAsync(_update, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A disconnected Release IPC client is still paused so its retained transport lease cannot block the update.")]
	public async Task RequestClioUpdateCommand_ShouldPauseDisconnectedReleaseClient() {
		// Arrange
		_ipcClient.IsConnected.Returns(false);
		_updateService.UpdateAsync(_update, Arg.Any<CancellationToken>()).Returns(
			new ClioToolUpdateResult(ClioToolUpdateOutcome.Success, "installed",
				Array.Empty<ClioToolProcess>()));
		await _sut.CheckForClioUpdateAsync();

		// Act
		await _sut.RequestClioUpdateCommand.ExecuteAsync(null);

		// Assert
		await _ipcClient.Received(1).PauseForUpdateAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A failed forced refresh is presented as a warning rather than a successful update result.")]
	public async Task CheckForClioUpdateAsync_ShouldShowWarning_WhenForcedRefreshIsUnavailable() {
		// Arrange
		_updateService.CheckAsync(Arg.Any<CancellationToken>(), true)
			.Returns((ClioToolUpdateCheck?)null);

		// Act
		await _sut.CheckForClioUpdateAsync(force: true);

		// Assert
		_sut.ShowClioUpdateWarningResult.Should().BeTrue(
			because: "an unavailable refresh must not use the green success treatment");
		_sut.ShowClioUpdateSuccessResult.Should().BeFalse(
			because: "no update check completed successfully");
	}

	[Test]
	[Description("A successful periodic check clears a stale forced-refresh warning before showing an available update.")]
	public async Task CheckForClioUpdateAsync_ShouldClearForcedRefreshWarning_WhenPeriodicCheckSucceeds() {
		// Arrange
		_updateService.CheckAsync(Arg.Any<CancellationToken>(), true)
			.Returns((ClioToolUpdateCheck?)null);
		await _sut.CheckForClioUpdateAsync(force: true);
		_updateService.CheckAsync(Arg.Any<CancellationToken>(), false).Returns(_update);

		// Act
		await _sut.CheckForClioUpdateAsync();

		// Assert
		_sut.IsClioUpdateAvailable.Should().BeTrue(
			because: "the later successful check found a newer Release clio");
		_sut.ClioUpdateMessage.Should().BeEmpty(
			because: "the unavailable-check warning is stale after a successful check");
		_sut.ShowClioUpdateWarningResult.Should().BeFalse(
			because: "the periodic check recovered successfully");
	}

	[Test]
	[Description("An unavailable later check removes a stale update snapshot that can no longer be executed.")]
	public async Task CheckForClioUpdateAsync_ShouldClearAvailableUpdate_WhenLaterCheckIsUnavailable() {
		// Arrange
		await _sut.CheckForClioUpdateAsync();
		_updateService.CheckAsync(Arg.Any<CancellationToken>(), false)
			.Returns((ClioToolUpdateCheck?)null);

		// Act
		await _sut.CheckForClioUpdateAsync();

		// Assert
		_sut.IsClioUpdateAvailable.Should().BeFalse(
			because: "the visible action must not outlive its verified update snapshot");
		_sut.AvailableClioVersion.Should().BeEmpty(
			because: "the tray and banner must not present a stale target version");
		_sut.RequestClioUpdateCommand.CanExecute(null).Should().BeFalse(
			because: "an unavailable check leaves no safe update action to execute");
	}

	[Test]
	[Description("A later unavailable check cannot restyle an updater failure as a successful terminal result.")]
	public async Task CheckForClioUpdateAsync_ShouldPreserveFailureSeverity_WhenLaterCheckIsUnavailable() {
		// Arrange
		_updateService.UpdateAsync(_update, Arg.Any<CancellationToken>()).Returns(
			new ClioToolUpdateResult(ClioToolUpdateOutcome.Failed, "The clio update failed.",
				Array.Empty<ClioToolProcess>()));
		await _sut.CheckForClioUpdateAsync();
		await _sut.RequestClioUpdateCommand.ExecuteAsync(null);
		_updateService.CheckAsync(Arg.Any<CancellationToken>(), false)
			.Returns((ClioToolUpdateCheck?)null);

		// Act
		await _sut.CheckForClioUpdateAsync();

		// Assert
		_sut.ShowClioUpdateWarningResult.Should().BeTrue(
			because: "the updater failure remains a warning after availability is invalidated");
		_sut.ShowClioUpdateSuccessResult.Should().BeFalse(
			because: "a failed update must never receive the green success treatment");
	}

	[Test]
	[Description("Canceling a blocker confirmation remains neutral after a later unavailable update check.")]
	public async Task CheckForClioUpdateAsync_ShouldNotShowSuccess_AfterBlockerCancelAndUnavailableCheck() {
		// Arrange
		var process = new ClioToolProcess(20736, 1234, TargetPath, "clio mcp-server",
			"Started by Codex - codex.exe (PID 55104)");
		_updateService.UpdateAsync(_update, Arg.Any<CancellationToken>()).Returns(
			new ClioToolUpdateResult(ClioToolUpdateOutcome.Blocked, "blocked", new[] { process }));
		await _sut.CheckForClioUpdateAsync();
		await _sut.RequestClioUpdateCommand.ExecuteAsync(null);
		_sut.CancelClioUpdateBlockersCommand.Execute(null);
		_updateService.CheckAsync(Arg.Any<CancellationToken>(), false)
			.Returns((ClioToolUpdateCheck?)null);

		// Act
		await _sut.CheckForClioUpdateAsync();

		// Assert
		_sut.ShowClioUpdateWarningResult.Should().BeTrue(
			because: "canceling an update is not a successful installation");
		_sut.ShowClioUpdateSuccessResult.Should().BeFalse(
			because: "the later unavailable check must not restyle cancellation as success");
	}

	private RingViewModel ResolveViewModel(ResolvedClioRuntime runtime) {
		var services = new ServiceCollection();
		services.AddSingleton(Substitute.For<IClioAdapter>());
		services.AddSingleton<IActionCatalogLoader, ActionCatalogLoader>();
		services.AddSingleton<IEnvStateStore, InMemoryEnvStateStore>();
		services.AddSingleton<IActionCatalogWatcher, NullActionCatalogWatcher>();
		services.AddSingleton<IEnvironmentSettingsWatcher, NullEnvironmentSettingsWatcher>();
		services.AddSingleton(Substitute.For<IClioSettingsStore>());
		services.AddSingleton(_ipcClient);
		services.AddSingleton(_updateService);
		services.AddSingleton(runtime);
		services.AddTransient<RingViewModel>();
		return services.BuildServiceProvider().GetRequiredService<RingViewModel>();
	}
}
