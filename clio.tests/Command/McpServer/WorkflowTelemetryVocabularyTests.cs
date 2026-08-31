using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer;
using Clio.Common.Telemetry;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Covers the flow-agnostic stage vocabulary: one set of stage names for every Creatio workflow, with the
/// flow carried in the <c>workflow</c> field rather than baked into the event name.
/// </summary>
/// <remarks>
/// Two defects are pinned here. First, telemetry used to accept only the app-creation names, whose
/// emission points hang off Gate P/R — which the migration, mobile-conversion and branding skills are
/// exempt from, so those flows had no event they were even allowed to send. Second, the obvious fix of a
/// name per flow per stage (<c>migration_plan_approved</c>, <c>branding_approved</c>, ...) encodes a
/// dimension into the enum: names multiply by flows, every new skill needs a clio release, and comparing
/// one funnel step across flows becomes a UNION over a hand-maintained list.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class WorkflowTelemetryVocabularyTests
{
	private static readonly string[] StageEvents = [
		"workflow_started", "clarification_requested", "user_input_received", "plan_presented",
		"plan_skipped", "plan_blocked", "plan_changes_requested", "plan_approved", "build_started",
		"work_item_completed", "workflow_completed", "workflow_failed", "changes_requested",
		"changes_applied"
	];

	private static readonly string[] LegacyAppCreationEvents = [
		"session_started", "business_plan_generated", "business_plan_approved",
		"implementation_started", "implementation_completed", "implementation_failed"
	];

	private string _telemetryHome;

	[SetUp]
	public void SetUp()
	{
		_telemetryHome = Path.Combine(Path.GetTempPath(), "clio-workflow-tests", Guid.NewGuid().ToString("N"));
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_telemetryHome)) {
			Directory.Delete(_telemetryHome, recursive: true);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts every flow-agnostic stage, so any workflow has events it is allowed to send.")]
	public void TelemetryService_Should_Accept_Every_Stage_Event()
	{
		// Arrange
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act / Assert
		foreach (string eventName in StageEvents) {
			TelemetryEventResult result = service.Send(
				CreateRequest(eventName) with { Workflow = "classic-to-freedom-migration" });
			result.Success.Should().BeTrue(because: $"'{eventName}' is part of the canonical stage vocabulary");
			result.Status.Should().Be("recorded", because: $"'{eventName}' must be stored, not silently dropped");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps accepting the deprecated app-creation names so an older installed toolkit is not silenced.")]
	public void TelemetryService_Should_Still_Accept_Legacy_App_Creation_Events()
	{
		// Arrange
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act / Assert — clio and the toolkit release independently, so a new clio must not reject the
		// events an already-installed toolkit still emits.
		foreach (string eventName in LegacyAppCreationEvents) {
			service.Send(CreateRequest(eventName)).Success.Should().BeTrue(
				because: $"'{eventName}' ships in installed toolkit versions and must keep working");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a per-flow event name, keeping the flow dimension in the field instead of the enum.")]
	public void TelemetryService_Should_Reject_Per_Flow_Event_Names()
	{
		// Arrange
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act / Assert — these read plausibly, which is exactly why the allow-list has to refuse them:
		// accepting them would let the name-per-flow explosion back in through the side door.
		foreach (string eventName in new[] { "migration_plan_approved", "branding_approved", "mobile_conversion_completed" }) {
			TelemetryEventResult result = service.Send(CreateRequest(eventName));
			result.Success.Should().BeFalse(
				because: $"'{eventName}' duplicates a stage that already exists; the flow belongs in the workflow field");
			result.Error!.Code.Should().Be("unknown-event-name");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Stores the flow and its optional qualifier as attributes, so one stage is comparable across flows.")]
	public void TelemetryService_Should_Persist_Workflow_And_Variant()
	{
		// Arrange
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act
		TelemetryEventResult recorded = service.Send(CreateRequest("workflow_started") with {
			Workflow = "classic-to-freedom-migration",
			Variant = "single-section"
		});

		// Assert — by id, not by newest name: on the real clock this write and GrantConsent's can land
		// in the same millisecond, and the file name then tie-breaks on a random GUID.
		JsonElement stored = ReadStoredEvent(recorded);
		ReadStringAttribute(stored, "workflow").Should().Be("classic-to-freedom-migration",
			because: "the flow dimension is what lets a generic stage name stay generic");
		ReadStringAttribute(stored, "variant").Should().Be("single-section",
			because: "a bounded per-stage qualifier replaces inventing a distinct event name per scope");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects free text in the flow fields, keeping customer data out of telemetry.")]
	public void TelemetryService_Should_Reject_Free_Text_Workflow()
	{
		// Arrange
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act
		TelemetryEventResult result = service.Send(CreateRequest("workflow_started") with {
			Workflow = "Migrate ACME Corp contact page"
		});

		// Assert
		result.Success.Should().BeFalse(
			because: "a free-text field would become the place a customer name eventually lands");
		result.Error!.Code.Should().Be("invalid-token");
	}

	[Test]
	[Category("Unit")]
	[TestCase("")]
	[TestCase(" ")]
	[Description("A blank flow field means absent, not malformed, so a hook that resolves one to nothing still reports its stage.")]
	public void TelemetryService_Should_Treat_A_Blank_Workflow_As_Absent(string blank)
	{
		// Arrange — the shape guard admitted any non-null value into a predicate that rejects blanks,
		// so "" cost the WHOLE event while null was accepted and became `unattributed`. That is the
		// value a hook emits by accident (`workflow: env.WORKFLOW ?? ""`, a template that resolved to
		// nothing), and the floor event it kills is the one tier the design guarantees.
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act
		TelemetryEventResult result = service.Send(CreateRequest("workflow_started") with {
			Workflow = blank
		});

		// Assert
		result.Success.Should().BeTrue(
			because: "an absent flow is a known state with a reserved name, not a malformed payload");
		ReadStringAttribute(ReadStoredEvent(result), "workflow").Should().Be("unattributed",
			because: "the contract says an omitted or blank value is recorded as the reserved name");
	}

	[Test]
	[Category("Unit")]
	[TestCase("")]
	[TestCase(" ")]
	[Description("A blank variant or model is omitted rather than rejected, matching every other optional field.")]
	public void TelemetryService_Should_Omit_A_Blank_Variant_And_Model(string blank)
	{
		// Arrange
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act
		TelemetryEventResult result = service.Send(CreateRequest("plan_presented") with {
			Workflow = "branding", Variant = blank, Model = blank
		});

		// Assert — coding_agent and plugin_version already behave this way; these three were the
		// outlier, and an event lost to a blank qualifier loses the stage as well.
		result.Success.Should().BeTrue(
			because: "a blank qualifier is an absent qualifier, as it is for every other optional field");
		JsonElement stored = ReadStoredEvent(result);
		ReadStringAttribute(stored, "variant").Should().BeNull(
			because: "an empty attribute reads as a real value where an absent one says nothing was sent");
		ReadStringAttribute(stored, "model").Should().BeNull(
			because: "an empty attribute reads as a real value where an absent one says nothing was sent");
	}

	[Test]
	[Category("Unit")]
	[Description("Measures each funnel stage with one flow-agnostic mapping, so every workflow is timed identically.")]
	public void TelemetryService_Should_Infer_Stage_Durations_For_Any_Flow()
	{
		// Arrange
		MutableTimeProvider time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		TelemetryService service = CreateService(time);
		service.Send(CreateRequest("workflow_started") with {
			Workflow = "branding", TelemetryConsent = "granted"
		});

		// Act — 20s to produce the plan, 10s for the developer to approve, 45s to apply.
		time.Advance(TimeSpan.FromSeconds(20));
		service.Send(CreateRequest("plan_presented") with { Workflow = "branding" });
		long planDuration = ReadIntAttribute(ReadNewestStoredEvent(), "duration_ms")!.Value;
		time.Advance(TimeSpan.FromSeconds(10));
		service.Send(CreateRequest("plan_approved") with { Workflow = "branding" });
		long approvalDuration = ReadIntAttribute(ReadNewestStoredEvent(), "duration_ms")!.Value;
		time.Advance(TimeSpan.FromSeconds(45));
		service.Send(CreateRequest("workflow_completed") with { Workflow = "branding" });
		JsonElement completed = ReadNewestStoredEvent();

		// Assert
		planDuration.Should().Be(20_000, because: "time-to-plan is measured from the session start");
		approvalDuration.Should().Be(10_000, because: "time-to-approval is measured from the presented plan");
		ReadIntAttribute(completed, "duration_ms").Should().Be(45_000,
			because: "the terminal event reports the narrowest span — here, time since approval");
		ReadIntAttribute(completed, "duration_since_session_start_ms").Should().Be(75_000,
			because: "total elapsed is still carried separately, so narrowing duration_ms loses nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("Anchors elapsed time on the canonical session-start event for a non-app-creation flow.")]
	public void TelemetryService_Should_Anchor_Elapsed_Time_On_Workflow_Started()
	{
		// Arrange
		MutableTimeProvider time = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		TelemetryService service = CreateService(time);
		service.Send(CreateRequest("workflow_started") with {
			Workflow = "mobile-page-conversion", TelemetryConsent = "granted"
		});

		// Act
		time.Advance(TimeSpan.FromSeconds(30));
		service.Send(CreateRequest("work_item_completed") with { Workflow = "mobile-page-conversion" });

		// Assert
		ReadIntAttribute(ReadNewestStoredEvent(), "duration_since_session_start_ms").Should().Be(30_000,
			because: "a conversion never emits session_started, so anchoring only on it would leave the flow unmeasured");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises the stage-plus-workflow model, so a host with no skill files is not misled.")]
	public void McpServerInstructions_Should_Advertise_Stage_And_Workflow_Model()
	{
		// Arrange
		string instructions = McpServerInstructions.Text;

		// Act / Assert
		instructions.Should().Contain("EVERY Creatio workflow",
			because: "treating telemetry as app-creation-only is what left the other flows silent");
		instructions.Should().Contain("`workflow` field",
			because: "an agent must know the flow goes in a field, not in the event name");
		// Deliberately NOT asserting individual stage names. They used to be listed here and in the
		// instructions, where nothing kept either copy in step with AllowedEventNames; the file's own
		// rule is that it is a pointer, not a manual. What must hold is that it points at the
		// authoritative list rather than restating a stale half of it.
		instructions.Should().Contain("get-tool-contract for the authoritative event_name list",
			because: "the one enumeration of stage names lives in the contract, derived from the enforcer");
		foreach (string stage in StageEvents) {
			instructions.Should().NotContain(stage,
				because: $"a hand-copied '{stage}' in the pointer text is a second source of truth with no drift oracle");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Never tells a skill-less agent to skip telemetry, which would negate the routing in the same breath.")]
	public void TelemetryGuidance_Should_Not_Gate_Itself_On_A_Loaded_Skill()
	{
		// Arrange — the surfaces an agent reads when NO skill file is loaded.
		string[] surfaces = [
			McpServerInstructions.Text,
			ReadToolDescription(typeof(Clio.Command.McpServer.Tools.SendTelemetryTool),
				nameof(Clio.Command.McpServer.Tools.SendTelemetryTool.SendTelemetry)),
			ReadToolDescription(typeof(Clio.Command.McpServer.Tools.GetTelemetryConsentTool),
				nameof(Clio.Command.McpServer.Tools.GetTelemetryConsentTool.GetTelemetryConsent)),
			// The two surfaces that actually reach the agent. Neither telemetry tool is in
			// McpCoreToolProfile, so both are non-resident and the CURATED contract is the WHOLE
			// description they receive — editing the attributes above alone ships nothing
			// (docs/knowledge/McpServer/curated-tool-contract-wins-over-the-description-attribute.md).
			SerializeContract(Clio.Command.McpServer.Tools.SendTelemetryTool.ToolName),
			SerializeContract(Clio.Command.McpServer.Tools.GetTelemetryConsentTool.ToolName)
		];

		// Act / Assert — this exact wording shipped and silently disabled the no-skill
		// case: the text routed telemetry per workflow and then told an agent with no
		// skill loaded not to call or prompt at all, so it correctly did nothing.
		foreach (string surface in surfaces) {
			// Case-insensitive throughout: these surfaces disagree about capitalisation, and the gate
			// that shipped read "if no such skill is active" where this assertion said "If no such ...".
			string lowered = surface.ToLowerInvariant();
			lowered.Should().NotContain("no such skill",
				because: "a skill-less agent doing Creatio work is in scope; every phrasing of the gate is the gate");
			lowered.Should().NotContain("only when a consuming skill/contract drives it",
				because: "the same gate, in the heading, negates every routing line under it");
			lowered.Should().Contain("no skill file is loaded",
				because: "the in-scope case must be stated positively, not left to inference");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Separates the two unconsented outcomes: an undecided install is asked, a denied one is silent.")]
	public void TelemetryService_Should_Distinguish_The_Two_Unconsented_Outcomes()
	{
		// Arrange — a fresh telemetry home, so consent reads unknown.
		TelemetryService service = CreateService();

		// Act — no telemetry_consent field while the decision is still unmade.
		TelemetryEventResult undecided = service.Send(CreateRequest("workflow_started") with {
			Workflow = "branding"
		});

		// Assert — rejected with an ACTIONABLE code, not dropped. An agent cannot ask the developer
		// for a decision it was never told is missing, and a silent drop is indistinguishable from a
		// successful send, so the whole first run would report nothing and nobody would know why.
		undecided.Success.Should().BeFalse();
		undecided.Status.Should().Be("rejected");
		undecided.Error!.Code.Should().Be("telemetry-consent-required");
		StoredEventCount().Should().Be(0, because: "an unconsented event must never reach the outbox");

		// Act — the developer declines, and the decision is persisted by the same call.
		TelemetryEventResult declined = service.Send(CreateRequest("workflow_started") with {
			Workflow = "branding", TelemetryConsent = "denied"
		});
		TelemetryEventResult afterDenial = service.Send(CreateRequest("plan_approved") with {
			Workflow = "branding"
		});

		// Assert — once denied, every later send reports SUCCESS and stores nothing. Success is the
		// right answer here: a decision the developer already made is not an error the agent should
		// retry, surface, or treat as a failed step.
		declined.Success.Should().BeTrue();
		declined.Status.Should().Be("consent-denied");
		afterDenial.Success.Should().BeTrue();
		afterDenial.Status.Should().Be("consent-denied");
		StoredEventCount().Should().Be(0,
			because: "a denied installation stores nothing at all, not even the stage that carried the decision");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps the unconsented behaviour described accurately, so an agent knows an undecided send is answerable.")]
	public void TelemetryGuidance_Should_Not_Claim_Unconsented_Events_Are_Silently_Dropped()
	{
		// Arrange
		string[] descriptions = [
			ReadToolDescription(typeof(Clio.Command.McpServer.Tools.SendTelemetryTool),
				nameof(Clio.Command.McpServer.Tools.SendTelemetryTool.SendTelemetry)),
			ReadToolDescription(typeof(Clio.Command.McpServer.Tools.GetTelemetryConsentTool),
				nameof(Clio.Command.McpServer.Tools.GetTelemetryConsentTool.GetTelemetryConsent)),
			// The two surfaces that actually reach the agent. Neither telemetry tool is in
			// McpCoreToolProfile, so both are non-resident and the CURATED contract is the WHOLE
			// description they receive — editing the attributes above alone ships nothing
			// (docs/knowledge/McpServer/curated-tool-contract-wins-over-the-description-attribute.md).
			SerializeContract(Clio.Command.McpServer.Tools.SendTelemetryTool.ToolName),
			SerializeContract(Clio.Command.McpServer.Tools.GetTelemetryConsentTool.ToolName)
		];

		// Assert — the shipped wording promised a silent drop while the code answers with
		// telemetry-consent-required. An agent that believes the drop is silent has no reason to ask
		// the developer, which is exactly how a first run ends up reporting nothing.
		foreach (string description in descriptions) {
			description.Should().NotContain("silently dropped",
				because: "an undecided send is rejected with an actionable code, so the drop is not silent");
			description.Should().Contain("telemetry-consent-required",
				because: "the agent has to recognise the code it will actually receive");
		}
	}

	private int StoredEventCount() {
		string events = Path.Combine(_telemetryHome, "events");
		return Directory.Exists(events) ? Directory.GetFiles(events).Length : 0;
	}

	[Test]
	[Category("Unit")]
	[Description("Stores the model driving the run, so a change in funnel behaviour can be attributed to the model that produced it.")]
	public void TelemetryService_Should_Persist_The_Model()
	{
		// Arrange
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act
		TelemetryEventResult accepted = service.Send(CreateRequest("plan_presented") with {
			Workflow = "branding",
			Model = "claude-opus-5",
			InputTokens = 12_345,
			OutputTokens = 678,
			CachedInputTokens = 90_123
		});
		TelemetryEventResult badModel = service.Send(CreateRequest("plan_presented") with {
			Workflow = "branding", Model = "Claude Opus 5 (preview)"
		});
		TelemetryEventResult negativeCount = service.Send(CreateRequest("plan_presented") with {
			Workflow = "branding", OutputTokens = -1
		});

		// Assert
		accepted.Success.Should().BeTrue();
		// By id: as in TelemetryService_Should_Persist_Workflow_And_Variant, the real clock can put this
		// event and the consent event in the same millisecond.
		JsonElement stored = ReadStoredEvent(accepted);
		ReadStringAttribute(stored, "model").Should().Be("claude-opus-5",
			because: "the model id has to survive to the stored event, or it cannot be grouped by");
		foreach ((string name, long expected) in new[] {
			("input_tokens", 12_345L), ("output_tokens", 678L), ("cached_input_tokens", 90_123L)
		}) {
			ReadIntAttribute(stored, name).Should().Be(expected,
				because: "a counter that does not reach the stored event cannot be summed or maxed");
		}
		// A display name is the shape that arrives when someone types it from memory, and the same
		// looseness is what would let prompt text ride along in this field.
		badModel.Success.Should().BeFalse();
		badModel.Error!.Code.Should().Be("invalid-token");
		// The counters are a running total, so they only grow; a negative one is a client bug that
		// would poison any sum or max taken over the session.
		negativeCount.Success.Should().BeFalse();
		negativeCount.Error!.Code.Should().Be("invalid-token-count");
	}


	[Test]
	[Category("Unit")]
	[Description("Keeps the advertised tool contract in step with the fields the service accepts, so the authoritative schema cannot forbid a supported field.")]
	public void SendTelemetryContract_Should_Advertise_The_Flow_Fields_It_Accepts()
	{
		// Arrange — the contract an agent is told to read BEFORE its first call, and which the
		// telemetry guidance names as authoritative over any prose.
		string contract = SerializeContract(Clio.Command.McpServer.Tools.SendTelemetryTool.ToolName);

		// Assert — measured failure: the service accepted workflow and variant while the contract
		// declared neither and asserted that any undocumented field is rejected. An agent that
		// believed it dropped the flow dimension entirely, which is the one field the whole
		// stage-plus-field design depends on.
		contract.Should().Contain("\"workflow\"",
			because: "the field carrying the flow dimension has to appear in the schema an agent reads first");
		contract.Should().Contain("\"variant\"",
			because: "a bounded qualifier an agent cannot see is a qualifier it will never send");
		foreach (string stage in StageEvents) {
			contract.Should().Contain(stage,
				because: "the advertised allow-list must match the one the service enforces");
		}
		contract.Should().NotContain("If no such skill is active",
			because: "the contract is a third surface that carried the skill gate after the tool descriptions dropped it");
		contract.Should().NotContain("silently dropped",
			because: "an undecided send is rejected with telemetry-consent-required, not dropped");
	}

	// Serialized through the same catalog entry point the MCP tool calls, so the assertions read the
	// bytes an agent actually receives rather than a hand-picked projection of the definition.
	private static string SerializeContract(string toolName) =>
		JsonSerializer.Serialize(Clio.Command.McpServer.Tools.ToolContractCatalog.GetContracts([toolName]));

	private static string ReadToolDescription(Type toolType, string methodName) =>
		toolType.GetMethod(methodName)!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single()
			.Description;

	[Test]
	[Category("Unit")]
	[Description("Keeps the announced allow-list free of per-flow duplicates of a stage that already exists.")]
	public void AllowedEventNames_Should_Not_Multiply_Stages_By_Flow()
	{
		// Arrange
		IReadOnlyList<string> allowed = TelemetryService.AllowedEventNames;

		// Act
		string[] perFlowPrefixed = allowed
			.Where(name => name.StartsWith("migration_", StringComparison.Ordinal)
				|| name.StartsWith("branding_", StringComparison.Ordinal)
				|| name.StartsWith("mobile_", StringComparison.Ordinal)
				|| name.StartsWith("maintenance_", StringComparison.Ordinal))
			.ToArray();

		// Assert
		perFlowPrefixed.Should().BeEmpty(
			because: "the flow belongs in the workflow field; a flow-prefixed name is the regression this guards");
		StageEvents.Should().OnlyContain(stage => allowed.Contains(stage),
			because: "a documented stage that clio rejects would silently zero out every flow's telemetry");
	}

	[Test]
	[Category("Unit")]
	[Description("A flow with no start of its own reports no elapsed time instead of borrowing another flow's anchor.")]
	public void TelemetryService_Should_Not_Borrow_Another_Flows_Start_Anchor()
	{
		// Arrange: the exact shape a measured run produced. A deterministic session-start floor emitted
		// under `unattributed`, then the agent — told not to emit a second workflow_started — reported
		// its build directly under its own flow, with no start of its own.
		MutableTimeProvider time = new(DateTimeOffset.Parse("2026-08-13T08:00:00Z"));
		TelemetryService service = CreateService(time);
		GrantConsent(service);

		service.Send(CreateRequest("workflow_started") with { Workflow = "unattributed" });
		time.Advance(TimeSpan.FromMinutes(2));

		// Act
		service.Send(CreateRequest("build_started") with { Workflow = "app-maintenance" });

		// Assert: no elapsed time, rather than 120 s measured from a run this stage was never part of.
		// A missing measure is honest; one borrowed from a foreign flow silently reports time the run
		// never spent, and there is nothing in the payload to tell the two apart downstream.
		ReadIntAttribute(ReadNewestStoredEvent(), "duration_since_session_start_ms").Should().BeNull(
			because: "elapsed time is only meaningful within one (session_id, workflow) pair");
	}

	[Test]
	[Category("Unit")]
	[Description("A second workflow_started under a different workflow does not overwrite the first flow's anchor.")]
	public void TelemetryService_Should_Not_Let_A_Second_Flow_Overwrite_The_First_Flows_Anchor()
	{
		// Arrange
		MutableTimeProvider time = new(DateTimeOffset.Parse("2026-08-13T08:00:00Z"));
		TelemetryService service = CreateService(time);
		GrantConsent(service);

		service.Send(CreateRequest("workflow_started") with { Workflow = "branding" });
		time.Advance(TimeSpan.FromMinutes(1));
		service.Send(CreateRequest("workflow_started") with { Workflow = "app-maintenance" });
		time.Advance(TimeSpan.FromMinutes(1));

		// Act: the branding run reports a terminal stage after the other flow also started.
		service.Send(CreateRequest("workflow_failed") with { Workflow = "branding" });

		// Assert: still measured from branding's own start two minutes back. This is what makes the
		// "never emit workflow_started twice in a session" instruction unnecessary — and that
		// instruction is why a real run was recorded with a build and no start at all.
		ReadIntAttribute(ReadNewestStoredEvent(), "duration_since_session_start_ms").Should().Be(120_000,
			because: "another flow starting in the same session must not shift this flow's elapsed-time measures");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts the session consumption measurement without letting it act as a funnel stage.")]
	public void TelemetryService_Should_Accept_SessionUsage_As_A_Measurement_Not_A_Stage()
	{
		// Arrange
		MutableTimeProvider time = new(DateTimeOffset.Parse("2026-08-14T08:00:00Z"));
		TelemetryService service = CreateService(time);
		GrantConsent(service);

		// Act: the shape a host session produces — the deterministic floor start under `unattributed`,
		// then the end-of-session totals in that same session-scoped pair.
		service.Send(CreateRequest("workflow_started") with { Workflow = "unattributed" });
		time.Advance(TimeSpan.FromMinutes(5));
		TelemetryEventResult result = service.Send(CreateRequest(TelemetryService.SessionUsageEvent) with {
			Workflow = "unattributed", InputTokens = 470, OutputTokens = 187_785, CachedInputTokens = 42_285_146
		});

		// Assert: recorded, with the counters intact and elapsed measured from the session's start.
		result.Status.Should().Be("recorded",
			because: "an agent cannot see its own running totals, so the host-side measurement is the only source of them");
		JsonElement stored = ReadNewestStoredEvent();
		ReadIntAttribute(stored, "output_tokens").Should().Be(187_785);
		ReadIntAttribute(stored, "duration_since_session_start_ms").Should().Be(300_000);

		// ...and it does not become the anchor: a stage after it is still measured from the session's
		// real start, not from the moment the totals happened to be taken.
		time.Advance(TimeSpan.FromMinutes(1));
		service.Send(CreateRequest("work_item_completed") with { Workflow = "unattributed" });
		ReadIntAttribute(ReadNewestStoredEvent(), "duration_since_session_start_ms").Should().Be(360_000,
			because: "session_usage is a measurement, so it must not anchor a session it merely reports on");
	}

	[Test]
	[Category("Unit")]
	[Description("A second run of the same flow in one session does not inherit the first run's timings.")]
	public void TelemetryService_Should_Not_Carry_A_Finished_Runs_Timings_Into_The_Next_Run()
	{
		// Arrange: the developer asks for one edit, then another. Both are app-maintenance, so both
		// land in the same (session_id, workflow) pair.
		MutableTimeProvider time = new(DateTimeOffset.Parse("2026-08-13T17:22:00Z"));
		TelemetryService service = CreateService(time);
		GrantConsent(service);

		service.Send(CreateRequest("workflow_started") with { Workflow = "app-maintenance" });
		service.Send(CreateRequest("build_started") with { Workflow = "app-maintenance" });
		time.Advance(TimeSpan.FromSeconds(40));
		service.Send(CreateRequest("workflow_completed") with { Workflow = "app-maintenance" });

		// The second run, eighteen minutes later.
		time.Advance(TimeSpan.FromMinutes(18));
		service.Send(CreateRequest("workflow_started") with { Workflow = "app-maintenance" });
		time.Advance(TimeSpan.FromSeconds(4));

		// Act
		service.Send(CreateRequest("workflow_completed") with { Workflow = "app-maintenance" });

		// Assert: four seconds, the length of the second run. Anchored on the finished run's
		// `build_started` it reported eighteen minutes — a stale span reads as a genuinely slow run,
		// which is worse than no measurement at all.
		ReadIntAttribute(ReadNewestStoredEvent(), "duration_ms").Should().Be(4_000,
			because: "a finished run's stage timestamps must not measure the next run");
	}

	[Test]
	[Category("Unit")]
	[Description("A repeated session-start inside an OPEN run keeps the stages already recorded.")]
	public void TelemetryService_Should_Keep_History_When_A_Run_Has_Not_Ended()
	{
		// Arrange: a stray second start — a hook handing over a session id mid-run, for instance —
		// while the run is still open. Clearing here would erase stages that really happened.
		MutableTimeProvider time = new(DateTimeOffset.Parse("2026-08-13T17:22:00Z"));
		TelemetryService service = CreateService(time);
		GrantConsent(service);

		service.Send(CreateRequest("workflow_started") with { Workflow = "app-maintenance" });
		time.Advance(TimeSpan.FromSeconds(10));
		service.Send(CreateRequest("build_started") with { Workflow = "app-maintenance" });
		time.Advance(TimeSpan.FromSeconds(5));
		service.Send(CreateRequest("workflow_started") with { Workflow = "app-maintenance" });
		time.Advance(TimeSpan.FromSeconds(5));

		// Act
		service.Send(CreateRequest("workflow_completed") with { Workflow = "app-maintenance" });

		// Assert: measured from `build_started`, which is still the narrowest known span — 10 s after
		// it. If the repeated start had cleared state, this would fall back to the new start (5 s).
		ReadIntAttribute(ReadNewestStoredEvent(), "duration_ms").Should().Be(10_000,
			because: "only a run that already reported a terminal stage is finished");
	}

	[Test]
	[Category("Unit")]
	[Description("Records one host as one cohort, whatever casing and separators the agent typed.")]
	[TestCase("Claude Code", "claude-code")]
	[TestCase("claude-code", "claude-code")]
	[TestCase("  Claude   Code  ", "claude-code")]
	[TestCase("GitHub Copilot CLI", "github-copilot-cli")]
	public void TelemetryService_Should_Canonicalize_CodingAgent(string sent, string expected)
	{
		// Arrange
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act
		TelemetryEventResult result =
			service.Send(CreateRequest("workflow_started") with { Workflow = "branding", CodingAgent = sent });

		// Assert: one host must not split into several cohorts because two runs spelled it differently.
		ReadStringAttribute(ReadStoredEvent(result), "coding_agent").Should().Be(expected,
			because: "adoption per host is only readable if the host name is one value, not a free-text spelling");
	}

	[Test]
	[Category("Unit")]
	[Description("Leaves a genuinely different agent name distinct rather than guessing which host it is.")]
	public void TelemetryService_Should_Not_Guess_A_Host_From_A_Different_Word()
	{
		// Arrange
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act: a truncated name a run really did send. It could be Claude Code or Claude Desktop.
		TelemetryEventResult result =
			service.Send(CreateRequest("workflow_started") with { Workflow = "branding", CodingAgent = "claude" });

		// Assert: kept as sent. Folding it into a canonical host would record a guess as measurement,
		// which is the same failure as an invented plugin_version — worse than a value left odd.
		ReadStringAttribute(ReadStoredEvent(result), "coding_agent").Should().Be("claude",
			because: "normalization may merge spellings of one name, never decide which name was meant");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts an event that omits coding_agent and plugin_version, which is what agents are told to do.")]
	public void TelemetryService_Should_Accept_An_Event_Without_The_Identity_Fields()
	{
		// Arrange: the guidance article and the toolkit hook both say to OMIT these rather than send a
		// guessed version or the placeholder `unknown`. Requiring them rejected every event from an agent
		// that obeyed — worst of all the skill-less run with no toolkit context, the case this work exists
		// for.
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act
		TelemetryEventResult result = service.Send(new TelemetryEventRequest(
			SessionId: "018f6e4a-0000-7000-9000-00000000000a",
			EventName: "workflow_started") with { Workflow = "app-maintenance" });

		// Assert
		result.Success.Should().BeTrue(because: "omitting an unknown version is the documented behaviour");
		result.Status.Should().Be("recorded");
		JsonElement stored = ReadStoredEvent(result);
		ReadStringAttribute(stored, "coding_agent").Should().BeNull(
			because: "an absent attribute says 'not supplied'; an empty one reads as a real value");
		ReadStringAttribute(stored, "plugin_version").Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("Records an event with no workflow as unattributed rather than in an empty bucket.")]
	public void TelemetryService_Should_Attribute_A_Missing_Workflow_To_Unattributed()
	{
		// Arrange
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act — `workflow` is now the state key, so an empty one would give the event its own anchor
		// bucket and drop it out of every GROUP BY workflow.
		TelemetryEventResult result = service.Send(CreateRequest("workflow_started"));

		// Assert
		ReadStringAttribute(ReadStoredEvent(result), "workflow").Should().Be("unattributed",
			because: "an unknown flow is a reserved value, not an empty string");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps a coding_agent that has no ASCII alphanumerics instead of erasing it.")]
	public void TelemetryService_Should_Not_Erase_A_Non_Ascii_Coding_Agent()
	{
		// Arrange
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act: a host name with nothing to slug. The split this canonicalization fixes is recoverable
		// afterwards; an erased value is not.
		TelemetryEventResult result = service.Send(CreateRequest("workflow_started") with {
			Workflow = "branding", CodingAgent = "Редактор"
		});

		// Assert
		ReadStringAttribute(ReadStoredEvent(result), "coding_agent").Should().Be("редактор",
			because: "an unslugabble name is still a name; storing empty loses it for good");
	}

	[Test]
	[Category("Unit")]
	[Description("Treats plan_blocked as the end of a run, matching the guidance article.")]
	public void TelemetryService_Should_Treat_Plan_Blocked_As_A_Finished_Run()
	{
		// Arrange — a run blocked on its first check ends at plan_blocked per the guidance; if clio does
		// not agree, the next run in that pair inherits this one's timestamps.
		MutableTimeProvider time = new(DateTimeOffset.Parse("2026-08-17T10:00:00Z"));
		TelemetryService service = CreateService(time);
		GrantConsent(service);

		service.Send(CreateRequest("workflow_started") with { Workflow = "app-maintenance" });
		time.Advance(TimeSpan.FromSeconds(30));
		service.Send(CreateRequest("plan_blocked") with { Workflow = "app-maintenance" });

		// Act — a new run, an hour later.
		time.Advance(TimeSpan.FromHours(1));
		service.Send(CreateRequest("workflow_started") with { Workflow = "app-maintenance" });
		time.Advance(TimeSpan.FromSeconds(5));
		TelemetryEventResult result =
			service.Send(CreateRequest("workflow_completed") with { Workflow = "app-maintenance" });

		// Assert
		ReadIntAttribute(ReadStoredEvent(result), "duration_since_session_start_ms").Should().Be(5_000,
			because: "the blocked run ended, so its start must not anchor the run that follows");
	}

	[Test]
	[Category("Unit")]
	[Description("Strips Unicode control and format characters from a host name that cannot be slugged, so schema v2's slug promise holds for every stored value.")]
	public void TelemetryService_Should_Not_Store_Control_Characters_In_A_Coding_Agent()
	{
		// Arrange — the slug path drops these, but it never runs for a value with no ASCII
		// alphanumerics: that falls back to the caller's string, so a name made only of control or
		// format characters reached the store verbatim while schema v2 advertises a canonical slug.
		// U+202E is the one that matters: it reverses the rendering of everything after it in whatever
		// dashboard cell or log line the value lands in. They are written as escapes, not as the
		// literal characters: a source file that carries a real U+202E renders differently from how
		// it compiles, which is the Trojan-Source shape text:S6389 fails the build over. The runtime
		// value the test feeds the service is byte-identical either way.
		TelemetryService service = CreateService();
		GrantConsent(service);

		// Act
		TelemetryEventResult result = service.Send(CreateRequest("workflow_started") with {
			Workflow = "branding", CodingAgent = "\u202E\u0007 \u200B"
		});

		// Assert
		result.Success.Should().BeTrue(
			because: "an unslugabble host name is still not a malformed payload");
		string stored = ReadStringAttribute(ReadStoredEvent(result), "coding_agent");
		foreach (string forbidden in new[] { "\u202E", "\u0007", "\u200B" }) {
			(stored ?? string.Empty).Should().NotContain(forbidden,
				because: "a value that renders as arbitrary bytes downstream is not the slug v2 promises");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("A run that starts on the legacy name and continues in stage vocabulary is still timed, since the two release independently.")]
	public void TelemetryService_Should_Anchor_Stage_Durations_On_Either_Start_Name()
	{
		// Arrange — clio ships before the toolkit does, so an installed toolkit emitting the legacy
		// `session_started` alongside an updated clio that speaks stages is a state on the release path,
		// not a mistake. Total elapsed already accepted either anchor; duration_ms did not, so exactly
		// these runs reported one measurement and silently lost the other.
		MutableTimeProvider time = new(DateTimeOffset.Parse("2026-08-20T09:00:00Z"));
		TelemetryService service = CreateService(time);
		GrantConsent(service);

		// Act
		service.Send(CreateRequest("session_started") with { Workflow = "app-creation" });
		time.Advance(TimeSpan.FromSeconds(45));
		TelemetryEventResult presented = service.Send(CreateRequest("plan_presented") with {
			Workflow = "app-creation"
		});
		time.Advance(TimeSpan.FromSeconds(15));
		TelemetryEventResult completed = service.Send(CreateRequest("workflow_completed") with {
			Workflow = "app-creation"
		});

		// Assert
		ReadIntAttribute(ReadStoredEvent(presented), "duration_ms").Should().Be(45_000,
			because: "the legacy start name anchors a canonical stage, as it already does for total elapsed");
		ReadIntAttribute(ReadStoredEvent(completed), "duration_ms").Should().Be(60_000,
			because: "a terminal stage falls back to the legacy start when no narrower anchor was reported");
	}

	[Test]
	[Category("Unit")]
	[Description("A repeating stage carries no inferred duration_ms, only total elapsed, so a batched report cannot pass itself off as fast work.")]
	public void TelemetryService_Should_Not_Infer_A_Duration_For_A_Repeating_Work_Item()
	{
		// Arrange
		// Anchoring this stage would measure the gap since the previously REPORTED unit rather than the
		// cost of this one: an agent that finishes several units and reports them together would
		// produce spans of milliseconds each, indistinguishable from genuinely fast work and worse
		// than an absent field. Per-unit cost stays derivable from consecutive event timestamps.
		// Pinned because the behaviour follows from an unmapped switch arm, which the next person to
		// add a mapping would change without noticing.
		MutableTimeProvider time = new(DateTimeOffset.Parse("2026-08-20T09:00:00Z"));
		TelemetryService service = CreateService(time);
		GrantConsent(service);

		// Act
		service.Send(CreateRequest("workflow_started") with { Workflow = "app-creation" });
		time.Advance(TimeSpan.FromSeconds(10));
		service.Send(CreateRequest("build_started") with { Workflow = "app-creation" });
		time.Advance(TimeSpan.FromSeconds(20));
		service.Send(CreateRequest("work_item_completed") with {
			Workflow = "app-creation", Variant = "entity-column"
		});
		time.Advance(TimeSpan.FromSeconds(5));
		TelemetryEventResult second = service.Send(CreateRequest("work_item_completed") with {
			Workflow = "app-creation", Variant = "page"
		});

		// Assert
		JsonElement stored = ReadStoredEvent(second);
		ReadIntAttribute(stored, "duration_ms").Should().BeNull(
			because: "a stage that repeats has no single prior stage whose span means anything");
		ReadIntAttribute(stored, "duration_since_session_start_ms").Should().Be(35_000,
			because: "total elapsed still places the unit within its run");
	}

	[Test]
	[Category("Unit")]
	[Description("Spooled files carry no UTF-8 BOM, so a consumer that is not .NET can read the spool at all.")]
	public void TelemetryService_Should_Write_Spooled_Files_Without_A_Byte_Order_Mark()
	{
		// Arrange
		// The spool used to be read only by clio, and .NET strips a BOM on read, so nothing here ever
		// noticed one was being written. It is now a second consumer's input — the CAADT hook reads
		// consent.json, and QA read the event files straight off disk, where a plain `json.load` answers
		// "Unexpected UTF-8 BOM" instead of an event. System.Text.Json is strict about it in exactly the
		// same way, so parsing the raw bytes is the consumer's experience rather than an approximation.
		TelemetryService service = CreateService();
		GrantConsent(service);
		TelemetryEventResult recorded = service.Send(CreateRequest("workflow_started") with {
			Workflow = "branding"
		});

		// Assert
		string eventPath = Directory
			.GetFiles(TelemetryStoragePaths.EventsDirectory(_telemetryHome), $"*_{recorded.EventId}.json")
			.Single();
		// installation-id.txt is the OTHER writer the no-BOM change touched, and the only one whose
		// content is consumed verbatim as an identifier. It is not JSON, so it cannot join the parse
		// loop below; without this line, reverting that call site alone keeps the suite green.
		string installationIdPath = Path.Combine(_telemetryHome, "installation-id.txt");
		File.ReadAllBytes(installationIdPath).Take(3).Should().NotEqual([(byte)0xEF, (byte)0xBB, (byte)0xBF],
			because: "a BOM in front of the id corrupts the value for any reader that does not strip one");
		File.ReadAllText(installationIdPath).Trim().Should()
			.Be(ReadStringAttribute(ReadStoredEvent(recorded), "installation_id"),
				because: "the id on disk is the id the event carries, byte for byte");
		foreach (string path in new[] { eventPath, Path.Combine(_telemetryHome, "consent.json") }) {
			string name = Path.GetFileName(path);
			File.ReadAllBytes(path).Take(3).Should().NotEqual([(byte)0xEF, (byte)0xBB, (byte)0xBF],
				because: $"{name} must not open with a byte-order mark");
			Action parseTheBytesAsAnyOtherReaderWould = () => JsonDocument.Parse(File.ReadAllBytes(path));
			parseTheBytesAsAnyOtherReaderWould.Should().NotThrow(
				because: $"{name} is JSON to whoever opens it, not only to a BOM-stripping .NET reader");
		}
	}

	private TelemetryService CreateService() => new(new System.IO.Abstractions.FileSystem(), _telemetryHome);

	private TelemetryService CreateService(TimeProvider timeProvider) =>
		new(new System.IO.Abstractions.FileSystem(), _telemetryHome, timeProvider);

	private static void GrantConsent(TelemetryService service) =>
		service.Send(CreateRequest("workflow_started") with {
			Workflow = "app-creation", TelemetryConsent = "granted"
		});

	private static TelemetryEventRequest CreateRequest(string eventName) =>
		new(
			SessionId: "018f6e4a-0000-7000-9000-000000000001",
			EventName: eventName,
			CodingAgent: "Claude Code",
			PluginVersion: "1.6.0");

	private JsonElement ReadNewestStoredEvent()
	{
		string eventsDirectory = TelemetryStoragePaths.EventsDirectory(_telemetryHome);
		string newest = Directory.GetFiles(eventsDirectory, "*.json")
			.OrderBy(path => path, StringComparer.Ordinal)
			.Last();
		return JsonDocument.Parse(File.ReadAllText(newest)).RootElement;
	}

	/// <summary>
	/// Reads the event a <see cref="TelemetryService.Send"/> call actually wrote, by its returned id.
	/// </summary>
	/// <remarks>
	/// File names are <c>yyyyMMddTHHmmssfffZ_&lt;guid&gt;</c>, so two events written in the same
	/// millisecond tie-break on a random GUID and "the newest by name" can be the wrong one. Tests on the
	/// real clock (the canonicalization cases) can collide with the consent event that way.
	/// </remarks>
	private JsonElement ReadStoredEvent(TelemetryEventResult result)
	{
		result.EventId.Should().NotBeNullOrWhiteSpace(because: "a recorded event always reports its id");
		string eventsDirectory = TelemetryStoragePaths.EventsDirectory(_telemetryHome);
		string path = Directory.GetFiles(eventsDirectory, $"*_{result.EventId}.json").Single();
		return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
	}

	private static string ReadStringAttribute(JsonElement storedEvent, string key) =>
		Attributes(storedEvent, key)
			.Select(attribute => attribute.GetProperty("value").GetProperty("string_value").GetString())
			.SingleOrDefault();

	private static long? ReadIntAttribute(JsonElement storedEvent, string key) =>
		Attributes(storedEvent, key)
			.Select(attribute => (long?)attribute.GetProperty("value").GetProperty("int_value").GetInt64())
			.SingleOrDefault();

	private static IEnumerable<JsonElement> Attributes(JsonElement storedEvent, string key) =>
		storedEvent.GetProperty("attributes")
			.EnumerateArray()
			.Where(attribute => attribute.GetProperty("key").GetString() == key);

	private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset _utcNow = start;

		public void Advance(TimeSpan delta) => _utcNow += delta;

		public override DateTimeOffset GetUtcNow() => _utcNow;
	}
}
