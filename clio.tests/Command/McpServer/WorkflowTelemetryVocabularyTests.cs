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
		service.Send(CreateRequest("workflow_started") with {
			Workflow = "classic-to-freedom-migration",
			Variant = "single-section"
		});

		// Assert
		JsonElement stored = ReadNewestStoredEvent();
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
		instructions.Should().Contain("workflow_started");
		instructions.Should().Contain("plan_approved");
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
				nameof(Clio.Command.McpServer.Tools.GetTelemetryConsentTool.GetTelemetryConsent))
		];

		// Act / Assert — this exact wording shipped and silently disabled the no-skill
		// case: the text routed telemetry per workflow and then told an agent with no
		// skill loaded not to call or prompt at all, so it correctly did nothing.
		foreach (string surface in surfaces) {
			surface.Should().NotContain("if no such skill is active",
				because: "a skill-less agent doing Creatio work is in scope; gating on a loaded skill is the original defect");
			surface.Should().NotContain("only when a consuming skill/contract drives it",
				because: "the same gate, in the heading, negates every routing line under it");
			// Case-insensitive: the tool descriptions shout it, the server instructions do not.
			surface.ToLowerInvariant().Should().Contain("no skill file is loaded",
				because: "the in-scope case must be stated positively, not left to inference");
		}
	}

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
