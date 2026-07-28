using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClioRing.Ipc;
using ClioRing.Services;
using ClioRing.ViewModels;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>
/// Unit tests for the guided Creatio Uninstall flow (story 9): the local-environment picker (local-filtered
/// from the same source the ring uses), the simple "Are you sure? Yes/No" confirm (NO exact-name typing),
/// and the safety invariants — a real uninstall fires ONLY on the Yes click, exactly once, with the pipeline
/// sink; No cancels with no clio call; nothing auto-fires on open; and the live gate stays off by default so
/// a Yes only renders the pipeline preview. Both collaborators are substituted, so no clio process runs and
/// no real uninstall ever fires. The pipeline's honest config-read-failure rendering (AC-ERR) is asserted
/// via the design seam.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class UninstallFlowViewModelTests {
	private const string LocalEnvName = "creatio-demo";

	private IClioAdapter _clio = null!;
	private IClioIpcClient _client = null!;
	private ClioToolCallResult _uninstallResult = new() { RawText = "{\"ok\":true}", Json = "{\"ok\":true}", IsError = false };

	[SetUp]
	public void SetUp() {
		_uninstallResult = new ClioToolCallResult { RawText = "{\"ok\":true}", Json = "{\"ok\":true}", IsError = false };

		_clio = Substitute.For<IClioAdapter>();
		_clio.ListEnvironmentsAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(DefaultEnvironments()));

		_client = Substitute.For<IClioIpcClient>();
		_client.CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(_uninstallResult));
	}

	[TearDown]
	public void TearDown() {
		_clio.ClearReceivedCalls();
		_client.ClearReceivedCalls();
	}

	// Two local environments (localhost + *.tscrm.com) plus one cloud environment that must be filtered out.
	private static IReadOnlyList<ClioEnvironment> DefaultEnvironments() => new[] {
		new ClioEnvironment(LocalEnvName, "http://localhost:40001", IsNetCore: true),
		new ClioEnvironment("qa-local", "https://qa.tscrm.com", IsNetCore: false),
		new ClioEnvironment("prod-cloud", "https://prod.creatio.com", IsNetCore: true)
	};

	private UninstallFormViewModel NewSut(bool liveUninstallEnabled = false) => new(_clio, _client, liveUninstallEnabled);

	private static async Task<UninstallFormViewModel> LoadedSutAsync(UninstallFormViewModel sut) {
		await sut.InitializeAsync();
		return sut;
	}

	[Test]
	[Description("Opening the flow lists only the LOCAL registered environments and pre-selects the first one.")]
	public async Task InitializeAsync_ShouldListOnlyLocalEnvironments_WhenDiscoverySucceeds() {
		// Arrange — a fresh flow over the substituted environment source (two local + one cloud env).
		UninstallFormViewModel sut = NewSut();

		// Act — run the open-time environment listing.
		await sut.InitializeAsync();

		// Assert — only the two local environments are listed; the cloud one is filtered out.
		sut.LocalEnvironments.Select(e => e.Name).Should().BeEquivalentTo(new[] { LocalEnvName, "qa-local" },
			because: "the picker lists only local registered environments (the cloud env is filtered out)");
		sut.SelectedEnvironment!.Name.Should().Be(LocalEnvName, because: "the first local environment is pre-selected on open");
	}

	[Test]
	[Description("Merely opening + listing never starts a real uninstall — nothing auto-fires it (AC-06/FR-21).")]
	public async Task InitializeAsync_ShouldNotFireUninstall_WhenNoYesClickOccurred() {
		// Arrange — a flow with the live gate ON, so only the missing Yes click can prevent an uninstall.
		UninstallFormViewModel sut = NewSut(liveUninstallEnabled: true);

		// Act — open + list only; no Uninstall/Yes click.
		await sut.InitializeAsync();

		// Assert — the uninstall tool was never invoked.
		await _client.DidNotReceive().CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Clicking Uninstall with no environment selected is blocked with a human notice and opens no confirm.")]
	public async Task RequestUninstall_ShouldBlockWithoutOpeningConfirm_WhenNoEnvironmentSelected() {
		// Arrange — a flow with no environments at all, so nothing can be selected.
		_clio.ListEnvironmentsAsync(Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult<IReadOnlyList<ClioEnvironment>>(Array.Empty<ClioEnvironment>()));
		UninstallFormViewModel sut = await LoadedSutAsync(NewSut(liveUninstallEnabled: true));
		sut.SelectedEnvironment.Should().BeNull(because: "the arrangement leaves no environment to select");

		// Act — click Uninstall with nothing selected.
		sut.RequestUninstallCommand.Execute(null);

		// Assert — no confirm opens, a human notice explains why, and no uninstall fires.
		sut.IsConfirmVisible.Should().BeFalse(because: "there is no target environment to confirm");
		sut.HasValidationSummary.Should().BeTrue(because: "the block surfaces a human notice telling the user what to do");
		await _client.DidNotReceive().CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("The Uninstall confirm names the target environment and spells out the irreversible consequence (no exact-name typing).")]
	public async Task RequestUninstall_ShouldOpenSimpleYesNoConfirmNamingEnvAndConsequence_WhenEnvironmentSelected() {
		// Arrange — a loaded flow with the first local environment pre-selected.
		UninstallFormViewModel sut = await LoadedSutAsync(NewSut());

		// Act — click Uninstall.
		sut.RequestUninstallCommand.Execute(null);

		// Assert — a simple Yes/No confirm opens, names the env, and spells out the consequence.
		sut.IsConfirmVisible.Should().BeTrue(because: "clicking Uninstall opens the Yes/No confirm");
		sut.ConfirmMessage.Should().Contain(LocalEnvName, because: "the confirm names the concrete target environment");
		sut.ConfirmConsequence.Should().ContainAll(new[] { "database", "undo" },
			because: "the consequence line states the database is dropped and there is no undo");
		sut.ConfirmMessage.Should().NotContain("uninstall-creatio", because: "no raw tool names may surface to the user");
		sut.ConfirmMessage.Should().NotContain("clio-run", because: "no raw tool names may surface to the user");
	}

	[Test]
	[Description("Clicking No cancels with no changes and makes no clio call (AC-06).")]
	public async Task CancelUninstall_ShouldMakeNoClioCall_WhenUserClicksNo() {
		// Arrange — a loaded flow (live gate ON) with the confirm open.
		UninstallFormViewModel sut = await LoadedSutAsync(NewSut(liveUninstallEnabled: true));
		sut.RequestUninstallCommand.Execute(null);
		sut.IsConfirmVisible.Should().BeTrue(because: "the confirm is open before the user decides");

		// Act — click No.
		sut.CancelUninstallCommand.Execute(null);

		// Assert — the confirm closes and no uninstall was ever invoked.
		sut.IsConfirmVisible.Should().BeFalse(because: "clicking No dismisses the confirm");
		await _client.DidNotReceive().CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Clicking Yes with the live gate ON fires the uninstall exactly once, with the pipeline sink and the target env in the request (AC-06).")]
	public async Task ConfirmUninstall_ShouldFireUninstallOnceWithSink_WhenYesAndLiveEnabled() {
		// Arrange — a loaded flow with the live gate enabled and the confirm open.
		UninstallFormViewModel sut = await LoadedSutAsync(NewSut(liveUninstallEnabled: true));
		sut.RequestUninstallCommand.Execute(null);

		string? capturedRequest = null;
		IProgress<ClioStageEvent>? capturedSink = null;
		_client.CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>())
			.Returns(ci => {
				capturedRequest = ci.ArgAt<string>(1);
				capturedSink = ci.ArgAt<IProgress<ClioStageEvent>>(2);
				return Task.FromResult(_uninstallResult);
			});

		// Act — click Yes.
		await sut.ConfirmUninstallCommand.ExecuteAsync(null);

		// Assert — the uninstall stream overload was invoked exactly once, with the pipeline's per-run sink.
		await _client.Received(1).CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>());
		capturedSink.Should().BeAssignableTo<PipelineRunSink>(because: "the uninstall is driven by the pipeline's per-run sink");

		// Assert — the request dispatches uninstall-creatio against the selected environment.
		capturedRequest.Should().Contain("uninstall-creatio", because: "the Yes click uninstalls Creatio");
		capturedRequest.Should().Contain("environment-name", because: "the uninstall targets one environment by name");
		capturedRequest.Should().Contain(LocalEnvName, because: "the selected environment is the uninstall target");
	}

	[Test]
	[Description("A non-error tool response without a streamed terminal is reported as unconfirmed, never as a successful removal.")]
	public async Task ConfirmUninstall_ShouldNotClaimRemoval_WhenResultHasNoTerminalEvent() {
		// Arrange
		UninstallFormViewModel sut = await LoadedSutAsync(NewSut(liveUninstallEnabled: true));
		sut.RequestUninstallCommand.Execute(null);

		// Act
		await sut.ConfirmUninstallCommand.ExecuteAsync(null);

		// Assert
		sut.Pipeline.HasTerminalOutcome.Should().BeFalse(because: "the substituted client emitted no run-completed event");
		sut.Output.Should().Contain("can't confirm", because: "a non-error response is not proof that destructive cleanup completed");
		sut.Output.Should().NotContain("was removed", because: "the Ring must never fabricate uninstall success");
	}

	[Test]
	[Description("An explicit tool error without a streamed terminal is reported as an uninstall failure.")]
	public async Task ConfirmUninstall_ShouldReportFailure_WhenToolReturnsErrorWithoutTerminalEvent() {
		// Arrange
		_uninstallResult = new ClioToolCallResult { RawText = "failed", Json = "{}", IsError = true };
		UninstallFormViewModel sut = await LoadedSutAsync(NewSut(liveUninstallEnabled: true));
		sut.RequestUninstallCommand.Execute(null);

		// Act
		await sut.ConfirmUninstallCommand.ExecuteAsync(null);

		// Assert
		sut.Output.Should().Contain("did not complete", because: "the tool explicitly returned an error");
		sut.Output.Should().NotContain("was removed", because: "an error response cannot be presented as success");
	}

	[Test]
	[Description("The Yes click uninstalls the immutable target named by the open confirmation even if selection changes afterward.")]
	public async Task ConfirmUninstall_ShouldUseConfirmedEnvironment_WhenSelectionChangesAfterConfirmOpens() {
		// Arrange
		UninstallFormViewModel sut = await LoadedSutAsync(NewSut(liveUninstallEnabled: true));
		sut.RequestUninstallCommand.Execute(null);
		sut.SelectedEnvironment = sut.LocalEnvironments[1];
		string? capturedRequest = null;
		_client.CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>())
			.Returns(call => {
				capturedRequest = call.ArgAt<string>(1);
				return Task.FromResult(_uninstallResult);
			});

		// Act
		await sut.ConfirmUninstallCommand.ExecuteAsync(null);

		// Assert
		capturedRequest.Should().Contain(LocalEnvName,
			because: "the user confirmed the original environment and that exact target must be executed");
		capturedRequest.Should().NotContain("qa-local",
			because: "a later picker change must not redirect an already-confirmed destructive action");
	}

	[Test]
	[Description("The pipeline is driven SOLELY by clio's single authoritative stream — the ring authors no manifest, so clio's runId owns the uninstall pipeline (one-authoritative-runId contract).")]
	public async Task ConfirmUninstall_ShouldRenderOnlyClioStreamUnderClioRunId_WhenYesAndLiveEnabled() {
		// Arrange — a loaded flow with the live gate on and the confirm open; the uninstall mock drives the
		// captured sink with clio's OWN manifest + terminal under clio's runId (not the ring's).
		UninstallFormViewModel sut = await LoadedSutAsync(NewSut(liveUninstallEnabled: true));
		sut.RequestUninstallCommand.Execute(null);

		Guid clioRunId = Guid.NewGuid();
		_client.CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>())
			.Returns(ci => {
				var sink = ci.ArgAt<IProgress<ClioStageEvent>>(2);
				sink.Report(new ClioStageEvent(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.Manifest,
					clioRunId, 0, ClioStageEventContract.Operations.Uninstall,
					Stages: new[] { new ClioStageManifestEntry("stop-iis", "Stop IIS site", 0, 1, false) }));
				sink.Report(new ClioStageEvent(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.RunCompleted,
					clioRunId, 1, ClioStageEventContract.Operations.Uninstall,
					RunCompleted: new ClioRunCompleted(ClioStageEventContract.RunOutcomes.Success, "Creatio was uninstalled.")));
				return Task.FromResult(_uninstallResult);
			});

		// Act — click Yes.
		await sut.ConfirmUninstallCommand.ExecuteAsync(null);

		// Assert — the pipeline rendered ONLY clio's manifest (no ring-authored preview stages) and reached
		// clio's terminal, proving clio's single authoritative stream owns the pipeline with no competing reset.
		sut.Pipeline.Steps.Should().ContainSingle(because: "only clio's authoritative manifest builds the pipeline");
		sut.Pipeline.Steps[0].Name.Should().Be("Stop IIS site", because: "the pipeline renders clio's stages, not a ring-authored preview manifest");
		sut.Pipeline.Title.Should().Be("Uninstall Creatio", because: "the pipeline renders an uninstall operation from clio's manifest");
		sut.Pipeline.IsSucceeded.Should().BeTrue(because: "clio's run-completed drove the terminal outcome");
	}

	[Test]
	[Description("With the live gate OFF (shipped default), Yes shows a preview notice and starts NO real uninstall; the pipeline stays idle (no ring-authored preview run).")]
	public async Task ConfirmUninstall_ShouldNotFireButShowPreviewNotice_WhenLiveGateOff() {
		// Arrange — a loaded flow at the shipped default (live gate off) with the confirm open.
		UninstallFormViewModel sut = await LoadedSutAsync(NewSut(liveUninstallEnabled: false));
		sut.RequestUninstallCommand.Execute(null);

		// Act — click Yes.
		await sut.ConfirmUninstallCommand.ExecuteAsync(null);

		// Assert — a human notice explains why, no real uninstall fired, and the pipeline stays idle until a
		// real clio run drives it (the ring authors no competing preview run).
		sut.Pipeline.HasSteps.Should().BeFalse(because: "the pipeline stays idle until clio's authoritative stream drives it");
		sut.Pipeline.RunState.Should().Be(PipelineRunState.Idle, because: "no ring-authored preview run is created when the gate is off");
		sut.HasPreviewNotice.Should().BeTrue(because: "the live uninstall is disabled in this preview build");
		sut.PreviewNotice.Should().NotContain("clio-run", because: "the notice is human-readable with no raw tool names");
		sut.PreviewNotice.Should().NotContain("uninstall-creatio", because: "the notice is human-readable with no raw tool names");
		await _client.DidNotReceive().CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A config-read failure renders the Read configuration step Failed and leaves the environment NOT unregistered (AC-ERR).")]
	public void DesignPopulate_ShouldRenderReadConfigFailedAndNotUnregistered_WhenConfigReadFails() {
		// Arrange — a flow rendering the honest config-read-failure case through the shared pipeline.
		UninstallFormViewModel sut = NewSut();

		// Act — drive the AC-ERR pipeline (config-read fails).
		sut.DesignPopulate(succeed: false);

		// Assert — the Read configuration step is Failed and the run terminates as a failure.
		PipelineStepViewModel readConfig = sut.Pipeline.Steps.First(s => s.Name == "Read configuration");
		readConfig.IsFailed.Should().BeTrue(because: "clio's honest reporting surfaces the config-read failure as Failed");
		sut.Pipeline.Steps.Select(step => step.StageId).Should().StartWith(["read-config", "stop-iis"],
			because: "the Ring simulation must mirror clio's safety-critical manifest order");
		PipelineStepViewModel stopIis = sut.Pipeline.Steps.First(s => s.StageId == "stop-iis");
		stopIis.IsDone.Should().BeFalse(because: "a configuration failure must leave the IIS site running");
		sut.Pipeline.IsFailed.Should().BeTrue(because: "the run terminates as a failure when configuration cannot be read");

		// Assert — the environment is NOT shown as unregistered (that stage never completed).
		PipelineStepViewModel unregister = sut.Pipeline.Steps.First(s => s.Name == "Unregister environment");
		unregister.IsDone.Should().BeFalse(because: "the environment must not be reported unregistered after a failed uninstall");
	}
}
