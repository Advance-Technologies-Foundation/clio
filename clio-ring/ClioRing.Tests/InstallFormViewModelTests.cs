using System;
using System.Threading;
using System.Threading.Tasks;
using ClioRing.Ipc;
using ClioRing.ViewModels;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>
/// Unit tests for the guided Creatio Install form (story 8): the auto-discovery open path, the form's
/// required fields (no dry-run control exists), the Local-vs-Rancher request shape, the preflight-as-first
/// pipeline-step behaviour (one human message + one corrective action on a problem, no install started),
/// and the safety invariants (a real deploy fires ONLY on the Install click, exactly once, with the
/// pipeline sink; nothing auto-fires; the live gate stays off by default). The IPC client is substituted
/// and returns canned JSON per inner command, so no clio process runs and no real deploy ever fires.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class InstallFormViewModelTests {
	private const string BuildsJson =
		"{\"status\":\"ok\",\"products-folder\":\"F:\\\\CreatioBuilds\",\"products-folder-exists\":true," +
		"\"message\":\"\",\"builds\":[" +
		"{\"file-name\":\"8.1.5.2176_Studio.zip\",\"full-path\":\"F:\\\\CreatioBuilds\\\\8.1.5.2176_Studio.zip\",\"size-bytes\":2147483648,\"modified-on-utc\":\"2026-06-20T10:00:00Z\"}]}";

	private const string InfraJson =
		"{\"status\":\"available\",\"local\":{" +
		"\"databases\":[{\"dbServerName\":\"rec-db\",\"engine\":\"postgres\",\"host\":\"localhost\",\"port\":5433}]," +
		"\"redisServers\":[{\"redisServerName\":\"rec-redis\",\"host\":\"localhost\",\"port\":6379}]}," +
		"\"recommendedDeployment\":{\"dbServerName\":\"rec-db\",\"redisServerName\":\"rec-redis\"}}";

	private const string PortJson = "{\"status\":\"ok\",\"firstAvailablePort\":40001}";

	// A passing preflight for BOTH sources (local+filesystem+k8 all pass) so a single canned assert works.
	private const string AssertPassJson =
		"{\"status\":\"pass\",\"sections\":{\"k8\":{\"status\":\"pass\"},\"local\":{\"status\":\"pass\"},\"filesystem\":{\"status\":\"pass\"}}}";

	// A failing Rancher preflight: the required k8 section is absent (unknown -> not pass).
	private const string AssertFailJson = "{\"status\":\"partial\",\"sections\":{\"local\":{\"status\":\"pass\"}}}";

	private IClioIpcClient _client = null!;
	private string _assertJson = AssertPassJson;
	private ClioToolCallResult _deployResult = new() { RawText = "{\"ok\":true}", Json = "{\"ok\":true}", IsError = false };

	[SetUp]
	public void SetUp() {
		_assertJson = AssertPassJson;
		_deployResult = new ClioToolCallResult { RawText = "{\"ok\":true}", Json = "{\"ok\":true}", IsError = false };

		_client = Substitute.For<IClioIpcClient>();
		_client.ConnectAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(new ClioServerHandshake {
			ServerName = "clio", ServerVersion = "8.1.0.77", Capabilities = new[] { "tools" }
		}));

		// Discovery + preflight dispatch a long-tail tool named in the arguments JSON — route by name.
		_client.CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(ci => Task.FromResult(CannedFor(ci.ArgAt<string>(1))));

		// The real deploy uses the typed stage-stream overload — return the canned deploy result.
		_client.CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(_deployResult));
	}

	[TearDown]
	public void TearDown() {
		_client.ClearReceivedCalls();
	}

	private InstallFormViewModel NewSut(bool liveDeployEnabled = false) => new(_client, liveDeployEnabled);

	[Test]
	[Description("Auto-init discovers builds and local infrastructure and pre-selects a free port that stays editable.")]
	public async Task InitializeAsync_ShouldPreselectDefaultsAndEditableFreePort_WhenDiscoverySucceeds() {
		// Arrange — a fresh form over the substituted discovery client.
		InstallFormViewModel sut = NewSut();

		// Act — run the open-time discovery entry point.
		await sut.InitializeAsync();

		// Assert — builds, local db/redis and the pre-selected free port are populated from discovery.
		sut.Builds.Should().ContainSingle(because: "list-creatio-builds returned one build");
		sut.SelectedBuild.Should().NotBeNull(because: "the newest build is pre-selected on open");
		sut.SelectedDatabase!.DbServerName.Should().Be("rec-db", because: "the recommended database is pre-selected");
		sut.SelectedRedis!.RedisServerName.Should().Be("rec-redis", because: "the recommended Redis is pre-selected");
		sut.Port.Should().Be("40001", because: "find-empty-iis-port reported 40001 as the first available port");

		// Assert — the pre-selected port is editable (it is a settable text field).
		sut.Port = "41234";
		sut.Port.Should().Be("41234", because: "the pre-selected free port must remain editable by the user");
	}

	[Test]
	[Description("On a machine where Kubernetes is not ready but local db+Redis are, discovery defaults the source to Local so the guided Install works out of the box instead of blocking on Rancher.")]
	public async Task InitializeAsync_ShouldDefaultSourceToLocal_WhenKubernetesNotReadyButLocalReady() {
		// Arrange — assert-infrastructure reports k8 fail but local + filesystem pass (the real dev-box state).
		_assertJson =
			"{\"status\":\"partial\",\"sections\":{\"k8\":{\"status\":\"fail\"},\"local\":{\"status\":\"pass\"},\"filesystem\":{\"status\":\"pass\"}}}";
		InstallFormViewModel sut = NewSut();

		// Act — run open-time discovery.
		await sut.InitializeAsync();

		// Assert — the guided default lands on Local (the only source that can run here), not Rancher.
		sut.Local.Should().BeTrue(because: "Rancher can't run without Kubernetes, but local db+Redis are ready, so Local is the working default");
	}

	[Test]
	[Description("When Kubernetes IS ready, discovery keeps the Rancher default (does not force Local) so a k8s-capable machine still gets the platform default.")]
	public async Task InitializeAsync_ShouldKeepRancherDefault_WhenKubernetesReady() {
		// Arrange — the default fixture reports all sections passing (k8 ready).
		InstallFormViewModel sut = NewSut();

		// Act — run open-time discovery.
		await sut.InitializeAsync();

		// Assert — Rancher (Local=false) is preserved when Kubernetes is available.
		sut.Local.Should().BeFalse(because: "Kubernetes is ready, so the platform default (Rancher) is kept");
	}

	[Test]
	[Description("Merely opening + discovering never starts a real install — nothing auto-fires the deploy (AC-06/FR-21).")]
	public async Task InitializeAsync_ShouldNotFireDeploy_WhenNoInstallClickOccurred() {
		// Arrange — a form with the live gate ON, so only the missing Install click can prevent a deploy.
		InstallFormViewModel sut = NewSut(liveDeployEnabled: true);

		// Act — open + discover only; no Install click.
		await sut.InitializeAsync();

		// Assert — the typed deploy stream overload was never invoked.
		await _client.DidNotReceive().CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A missing instance name blocks the install with one human message + corrective action, and no deploy fires (AC-ERR).")]
	public async Task InstallAsync_ShouldBlockWithOneFriendlyMessage_WhenInstanceNameMissing() {
		// Arrange — defaults loaded, a Rancher plan, but the instance name left empty.
		InstallFormViewModel sut = NewSut(liveDeployEnabled: true);
		await sut.InitializeAsync();
		sut.Local = false;
		sut.InstanceName = string.Empty;

		// Act — click Install.
		await sut.InstallCommand.ExecuteAsync(null);

		// Assert — preflight is a PRE-CALL gate: it never authors a pipeline run, so the clio-owned pipeline
		// stays idle/empty (no competing ring run to collide with clio's authoritative runId).
		sut.Pipeline.HasSteps.Should().BeFalse(because: "preflight is a pre-call gate and authors no pipeline run");
		sut.Pipeline.RunState.Should().Be(PipelineRunState.Idle, because: "the pipeline stays idle until a real clio run drives it");

		// Assert — exactly one human blocker message + one corrective action, with no raw tool/flag concepts.
		sut.HasValidationSummary.Should().BeTrue(because: "the block surfaces one human-readable blocker");
		sut.ValidationSummary.Should().Contain("name", because: "the message names the concrete problem in human terms");
		sut.ValidationSummary.Should().NotContain("clio-run", because: "no raw tool names may surface to the user");
		sut.ValidationSummary.Should().NotContain("deploy-creatio", because: "no raw tool names may surface to the user");
		sut.ValidationSummary.Should().NotContain("_meta", because: "no raw protocol concepts may surface to the user");

		// Assert — no real deploy was fired.
		await _client.DidNotReceive().CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A Local install with no database/Redis chosen blocks with one friendly message + corrective action, no deploy.")]
	public async Task InstallAsync_ShouldBlock_WhenLocalSourceHasNoDatabaseOrRedis() {
		// Arrange — Local source but the db/redis selections cleared.
		InstallFormViewModel sut = NewSut(liveDeployEnabled: true);
		await sut.InitializeAsync();
		sut.Local = true;
		sut.InstanceName = "creatio-demo";
		sut.SelectedDatabase = null;
		sut.SelectedRedis = null;

		// Act — click Install.
		await sut.InstallCommand.ExecuteAsync(null);

		// Assert — blocked at the pre-call gate with a human message; the pipeline stays idle (no ring run).
		sut.Pipeline.HasSteps.Should().BeFalse(because: "a Local source without db/redis is blocked before any pipeline run");
		sut.ValidationSummary.Should().Contain("database", because: "the message names what the Local source needs");
		await _client.DidNotReceive().CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A required infrastructure check that fails blocks the install with one human message + corrective action, no deploy (AC-03).")]
	public async Task InstallAsync_ShouldBlockWithCorrectiveAction_WhenRequiredInfraCheckFails() {
		// Arrange — a valid Rancher plan, but the required Kubernetes check does not pass.
		_assertJson = AssertFailJson;
		InstallFormViewModel sut = NewSut(liveDeployEnabled: true);
		await sut.InitializeAsync();
		sut.Local = false;
		sut.InstanceName = "creatio-demo";

		// Act — click Install.
		await sut.InstallCommand.ExecuteAsync(null);

		// Assert — blocked at the pre-call gate (no pipeline run authored) with one human blocker + corrective action.
		sut.Pipeline.HasSteps.Should().BeFalse(because: "a failed required check blocks before any pipeline run is authored");
		sut.Pipeline.RunState.Should().Be(PipelineRunState.Idle, because: "the pipeline stays idle until a real clio run drives it");
		sut.HasValidationSummary.Should().BeTrue(because: "the block surfaces one human message plus one corrective action");
		sut.ValidationSummary.Should().NotContain("assert-infrastructure", because: "the corrective action is human-readable with no raw tool names");
		sut.ValidationSummary.Should().NotContain("clio-run", because: "the corrective action is human-readable with no raw tool names");
		await _client.DidNotReceive().CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A valid Rancher install fires the deploy exactly once with the pipeline sink and omits db/redis from the request (AC-04).")]
	public async Task InstallAsync_ShouldFireDeployOnceWithSinkAndOmitDbRedis_WhenValidRancherAndLiveEnabled() {
		// Arrange — a valid Rancher plan with the live gate enabled.
		InstallFormViewModel sut = NewSut(liveDeployEnabled: true);
		await sut.InitializeAsync();
		sut.Local = false;
		sut.InstanceName = "creatio-demo";

		string? capturedRequest = null;
		IProgress<ClioStageEvent>? capturedSink = null;
		_client.CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>())
			.Returns(ci => {
				capturedRequest = ci.ArgAt<string>(1);
				capturedSink = ci.ArgAt<IProgress<ClioStageEvent>>(2);
				return Task.FromResult(_deployResult);
			});

		// Act — click Install.
		await sut.InstallCommand.ExecuteAsync(null);

		// Assert — the deploy stream overload was invoked exactly once, with the pipeline's run sink.
		await _client.Received(1).CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>());
		capturedSink.Should().BeAssignableTo<PipelineRunSink>(because: "the deploy is driven by the pipeline's per-run sink");

		// Assert — the request targets deploy-creatio and OMITS db/redis for the Rancher source.
		capturedRequest.Should().Contain("deploy-creatio", because: "the Install click deploys Creatio");
		capturedRequest.Should().NotContain("dbServerName", because: "the Rancher source uses the default Kubernetes database and omits it");
		capturedRequest.Should().NotContain("redisServerName", because: "the Rancher source uses the default Kubernetes Redis and omits it");
	}

	[Test]
	[Description("The pipeline is driven SOLELY by clio's single authoritative stream — the ring authors no manifest, so clio's runId owns the pipeline (one-authoritative-runId contract).")]
	public async Task InstallAsync_ShouldRenderOnlyClioStreamUnderClioRunId_WhenLiveEnabled() {
		// Arrange — a valid Rancher plan with the live gate on; the deploy mock drives the captured sink with
		// clio's OWN manifest + terminal under clio's runId (not the ring's).
		InstallFormViewModel sut = NewSut(liveDeployEnabled: true);
		await sut.InitializeAsync();
		sut.Local = false;
		sut.InstanceName = "creatio-demo";

		Guid clioRunId = Guid.NewGuid();
		_client.CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>())
			.Returns(ci => {
				var sink = ci.ArgAt<IProgress<ClioStageEvent>>(2);
				sink.Report(new ClioStageEvent(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.Manifest,
					clioRunId, 0, ClioStageEventContract.Operations.Deploy,
					Stages: new[] { new ClioStageManifestEntry("unzip", "Unzip distribution", 0, 1, false) }));
				sink.Report(new ClioStageEvent(ClioStageEventContract.SchemaVersion, ClioStageEventContract.EventTypes.RunCompleted,
					clioRunId, 1, ClioStageEventContract.Operations.Deploy,
					RunCompleted: new ClioRunCompleted(ClioStageEventContract.RunOutcomes.Success, "Deployment completed")));
				return Task.FromResult(_deployResult);
			});

		// Act — click Install.
		await sut.InstallCommand.ExecuteAsync(null);

		// Assert — the pipeline rendered ONLY clio's manifest (no ring "Check requirements" step) and reached
		// clio's terminal, proving clio's single authoritative stream owns the pipeline with no competing reset.
		sut.Pipeline.Steps.Should().ContainSingle(because: "only clio's authoritative manifest builds the pipeline");
		sut.Pipeline.Steps[0].Name.Should().Be("Unzip distribution", because: "the pipeline renders clio's stages, not a ring-authored preflight step");
		sut.Pipeline.IsSucceeded.Should().BeTrue(because: "clio's run-completed drove the terminal outcome");
	}

	[Test]
	[Description("A valid Local install includes the chosen database and Redis server names in the deploy request.")]
	public async Task InstallAsync_ShouldIncludeDbAndRedis_WhenValidLocalAndLiveEnabled() {
		// Arrange — a valid Local plan with the live gate enabled.
		InstallFormViewModel sut = NewSut(liveDeployEnabled: true);
		await sut.InitializeAsync();
		sut.Local = true;
		sut.InstanceName = "creatio-demo";

		string? capturedRequest = null;
		_client.CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>())
			.Returns(ci => { capturedRequest = ci.ArgAt<string>(1); return Task.FromResult(_deployResult); });

		// Act — click Install.
		await sut.InstallCommand.ExecuteAsync(null);

		// Assert — the request includes the selected local database and Redis server names.
		capturedRequest.Should().Contain("dbServerName", because: "a Local install provides the database server name");
		capturedRequest.Should().Contain("rec-db", because: "the selected database name is sent");
		capturedRequest.Should().Contain("redisServerName", because: "a Local install provides the Redis server name");
		capturedRequest.Should().Contain("rec-redis", because: "the selected Redis name is sent");
	}

	[Test]
	[Description("With the live gate OFF (shipped default), a clean preflight validates but does NOT start a real install (safety gate).")]
	public async Task InstallAsync_ShouldNotFireDeployButShowPreviewNotice_WhenLiveGateOff() {
		// Arrange — a valid Rancher plan but the live deploy gate at its shipped default (off).
		InstallFormViewModel sut = NewSut(liveDeployEnabled: false);
		await sut.InitializeAsync();
		sut.Local = false;
		sut.InstanceName = "creatio-demo";

		// Act — click Install.
		await sut.InstallCommand.ExecuteAsync(null);

		// Assert — preflight passed but no real deploy fired; the pipeline stays idle and a human notice explains why.
		sut.Pipeline.HasSteps.Should().BeFalse(because: "the pipeline stays idle until a real clio run drives it (no ring-authored preview)");
		sut.HasPreviewNotice.Should().BeTrue(because: "the live install is disabled in this preview build");
		sut.PreviewNotice.Should().NotContain("clio-run", because: "the notice is human-readable with no raw tool names");
		sut.PreviewNotice.Should().NotContain("deploy-creatio", because: "the notice is human-readable with no raw tool names");
		await _client.DidNotReceive().CallToolAsync("clio-run", Arg.Any<string>(), Arg.Any<IProgress<ClioStageEvent>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A no-terminal result whose payload carries clio's {success:false,error} envelope (no MCP IsError flag) is reported as a failure with the real reason — NEVER as a fabricated success.")]
	public async Task InstallAsync_ShouldReportFailureNotSuccess_WhenClioReturnsFailureEnvelopeWithoutTerminal() {
		// Arrange — a valid Rancher plan, live gate on, but clio returns its long-tail failure envelope
		// {"success":false,"error":…} WITHOUT setting IsError and WITHOUT streaming any stage event.
		InstallFormViewModel sut = NewSut(liveDeployEnabled: true);
		await sut.InitializeAsync();
		sut.Local = false;
		sut.InstanceName = "creatio-demo";
		const string envelope = "{\"success\":false,\"error\":\"Kubernetes is not available\"}";
		_deployResult = new ClioToolCallResult { RawText = envelope, Json = envelope, IsError = false };

		// Act — click Install.
		await sut.InstallCommand.ExecuteAsync(null);

		// Assert — the human message surfaces the real failure reason and never claims the install succeeded.
		sut.Output.Should().Contain("did not complete", because: "clio signalled failure via success:false, so the ring must report a failure");
		sut.Output.Should().Contain("Kubernetes is not available", because: "the real clio error is surfaced to the human");
		sut.Output.Should().NotContain("was installed", because: "an unconfirmed/failed outcome must never be reported as a successful install");
	}

	[Test]
	[Description("When clio streams no terminal AND gives no failure signal, the outcome is unknown — the ring says it cannot confirm the install, never fabricating success.")]
	public async Task InstallAsync_ShouldNotClaimSuccess_WhenNoTerminalAndNoFailureSignal() {
		// Arrange — a valid Rancher plan, live gate on; clio returns a benign non-error payload with no stage
		// events (e.g. an old clio lacking typed progress). The outcome is genuinely unconfirmed.
		InstallFormViewModel sut = NewSut(liveDeployEnabled: true);
		await sut.InitializeAsync();
		sut.Local = false;
		sut.InstanceName = "creatio-demo";
		_deployResult = new ClioToolCallResult { RawText = "{\"ok\":true}", Json = "{\"ok\":true}", IsError = false };

		// Act — click Install.
		await sut.InstallCommand.ExecuteAsync(null);

		// Assert — the ring is honest that it cannot confirm the install rather than fabricating success.
		sut.Output.Should().Contain("can't confirm", because: "no success terminal was observed, so success must not be asserted");
		sut.Output.Should().NotContain("was installed", because: "an unconfirmed outcome must never be reported as a successful install");
	}

	[Test]
	[Description("A no-terminal result carrying clio's command envelope {exit-code:1, execution-log-messages} (no MCP IsError flag) is reported as a failure with the real reason, not as 'can't confirm'.")]
	public async Task InstallAsync_ShouldSurfaceExitCodeFailureReason_WhenClioReturnsNonZeroExitEnvelope() {
		// Arrange — a valid Local plan, live gate on; clio returns its deploy command envelope: a non-zero
		// exit-code with the real reason in execution-log-messages, WITHOUT setting IsError and WITHOUT
		// streaming any stage event (the exact shape a fast deploy failure returns).
		InstallFormViewModel sut = NewSut(liveDeployEnabled: true);
		await sut.InitializeAsync();
		sut.Local = true;
		sut.InstanceName = "creatio-demo";
		const string envelope =
			"{\"exit-code\":1,\"execution-log-messages\":[{\"message-type\":\"Info\",\"value\":\"Could not find zip file: C:\\\\builds\\\\x.zip\"}]}";
		_deployResult = new ClioToolCallResult { RawText = envelope, Json = envelope, IsError = false };

		// Act — click Install.
		await sut.InstallCommand.ExecuteAsync(null);

		// Assert — the real clio reason is surfaced and success is never claimed.
		sut.Output.Should().Contain("did not complete", because: "a non-zero exit-code is a failure the ring must report");
		sut.Output.Should().Contain("Could not find zip file", because: "the real reason from execution-log-messages is surfaced to the human");
		sut.Output.Should().NotContain("can't confirm", because: "an identified failure must not be mislabelled as an unconfirmed outcome");
	}

	private ClioToolCallResult CannedFor(string argumentsJson) {
		string raw =
			argumentsJson.Contains("list-creatio-builds") ? BuildsJson :
			argumentsJson.Contains("show-passing-infrastructure") ? InfraJson :
			argumentsJson.Contains("find-empty-iis-port") ? PortJson :
			argumentsJson.Contains("assert-infrastructure") ? _assertJson :
			"{}";
		return new ClioToolCallResult { RawText = raw, Json = raw };
	}
}
