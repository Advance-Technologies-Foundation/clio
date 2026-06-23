using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common.Telemetry;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class SendTelemetryToolTests
{
	private string _telemetryHome;

	[SetUp]
	public void SetUp()
	{
		_telemetryHome = Path.Combine(Path.GetTempPath(), "clio-telemetry-tests", Guid.NewGuid().ToString("N"));
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
	[Description("Exposes the stable send-telemetry MCP tool name as a constant and attribute value.")]
	public void SendTelemetryTool_Should_Expose_Stable_Tool_Name()
	{
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(SendTelemetryTool)
			.GetMethod(nameof(SendTelemetryTool.SendTelemetry))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		SendTelemetryTool.ToolName.Should().Be("send-telemetry",
			because: "the product telemetry skill contract calls this stable MCP tool name");
		attribute.Name.Should().Be(SendTelemetryTool.ToolName,
			because: "MCP discovery should advertise the same stable tool name that tests and agents use");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes a read-only consent lookup tool so agents do not probe consent by sending analytics.")]
	public void GetTelemetryConsentTool_Should_Expose_Stable_Tool_Name()
	{
		// Arrange / Act
		McpServerToolAttribute toolAttribute = (McpServerToolAttribute)typeof(GetTelemetryConsentTool)
			.GetMethod(nameof(GetTelemetryConsentTool.GetTelemetryConsent))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		toolAttribute.Name.Should().Be(GetTelemetryConsentTool.ToolName,
			because: "agents need a read-only way to inspect local consent before sending telemetry events");
		toolAttribute.ReadOnly.Should().BeTrue(
			because: "consent lookup must not write product analytics");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises the product telemetry tools in MCP server instructions while deferring consent orchestration to the skill/contract.")]
	public void McpServerInstructions_Should_Advertise_Product_Telemetry_Tools()
	{
		// Arrange
		string instructions = McpServerInstructions.Text;

		// Act / Assert
		instructions.Should().Contain("Product telemetry",
			because: "Copilot and other chat agents receive MCP server instructions even before loading skill files");
		instructions.Should().Contain("send-telemetry",
			because: "the chat model needs to know which MCP tool stores product telemetry");
		instructions.Should().Contain("get-telemetry-consent",
			because: "agents should inspect local consent with a read-only tool before sending analytics");
		instructions.Should().Contain("withdraw-telemetry-consent",
			because: "agents need a tool to honor a developer's request to stop or withdraw telemetry");
		instructions.Should().Contain("get-tool-contract",
			because: "the authoritative payload shape and emission order live in the tool contract, not the server instructions");
		instructions.Should().NotContain("event_name=session_started",
			because: "the per-step consent prompt and event sequence are owned by the skill/contract to avoid two sources of truth");
	}

	[Test]
	[Category("Unit")]
	[Description("Delegates the MCP request to the telemetry service without wrapping the structured result.")]
	public void SendTelemetry_Should_Delegate_To_Telemetry_Service()
	{
		// Arrange
		ITelemetryService service = Substitute.For<ITelemetryService>();
		ITelemetryFlushScheduler scheduler = Substitute.For<ITelemetryFlushScheduler>();
		TelemetryEventRequest request = CreateRequest() with { TelemetryConsent = "granted" };
		TelemetryEventResult expected = new(true, "recorded", "event-id");
		service.Send(request).Returns(expected);
		SendTelemetryTool tool = new(service, scheduler);

		// Act
		TelemetryEventResult actual = tool.SendTelemetry(request);

		// Assert
		actual.Should().BeSameAs(expected,
			because: "the MCP tool should return the service result directly for clients");
		service.Received(1).Send(request);
	}

	[Test]
	[Category("Unit")]
	[Description("Schedules a background telemetry flush after an event is stored locally.")]
	public void SendTelemetry_Should_Schedule_Flush_When_Event_Stored()
	{
		// Arrange
		ITelemetryService service = Substitute.For<ITelemetryService>();
		ITelemetryFlushScheduler scheduler = Substitute.For<ITelemetryFlushScheduler>();
		TelemetryEventRequest request = CreateRequest();
		service.Send(request).Returns(new TelemetryEventResult(true, "recorded", "event-id"));
		SendTelemetryTool tool = new(service, scheduler);

		// Act
		tool.SendTelemetry(request);

		// Assert
		scheduler.Received(1).TryScheduleFlush();
	}

	[Test]
	[Category("Unit")]
	[Description("Does not schedule a background flush when the telemetry is rejected or consent is denied.")]
	public void SendTelemetry_Should_Not_Schedule_Flush_When_Event_Not_Stored()
	{
		// Arrange
		ITelemetryService service = Substitute.For<ITelemetryService>();
		ITelemetryFlushScheduler scheduler = Substitute.For<ITelemetryFlushScheduler>();
		TelemetryEventRequest request = CreateRequest();
		service.Send(request).Returns(
			new TelemetryEventResult(false, "rejected", Error: new TelemetryError("unknown-event-name", "bad")),
			new TelemetryEventResult(true, "consent-denied"));
		SendTelemetryTool tool = new(service, scheduler);

		// Act
		tool.SendTelemetry(request);
		tool.SendTelemetry(request);

		// Assert
		scheduler.DidNotReceive().TryScheduleFlush();
	}

	[Test]
	[Category("Unit")]
	[Description("Requires telemetry consent before the first telemetry can be persisted.")]
	public void TelemetryService_Should_Require_Consent_On_First_Use()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		TelemetryEventResult result = service.Send(CreateRequest());

		// Assert
		result.Success.Should().BeFalse(
			because: "first-run telemetry must not persist product events before the user grants or denies consent");
		result.Error!.Code.Should().Be("telemetry-consent-required",
			because: "agents need a deterministic signal to ask the user for telemetry consent");
		EventFiles().Should().BeEmpty(
			because: "no local event should be written before consent is granted");
	}

	[Test]
	[Category("Unit")]
	[Description("Reads local consent without creating telemetry folders or storing telemetry events.")]
	public void TelemetryService_Should_Read_Consent_Status_Without_Writing_Analytics()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		TelemetryConsentResult unknown = service.GetConsentStatus();

		// Assert
		unknown.TelemetryConsent.Should().Be("unknown",
			because: "missing consent should be reported without sending analytics");
		Directory.Exists(_telemetryHome).Should().BeFalse(
			because: "read-only consent lookup must not create telemetry state");
	}

	[Test]
	[Category("Unit")]
	[Description("Reads a persisted granted consent decision without requiring another user prompt.")]
	public void TelemetryService_Should_Return_Persisted_Consent_Status()
	{
		// Arrange
		TelemetryService service = CreateService();
		service.Send(CreateRequest() with { TelemetryConsent = "granted" });

		// Act
		TelemetryConsentResult result = service.GetConsentStatus();

		// Assert
		result.Status.Should().Be("known",
			because: "persisted consent should be reusable across sessions");
		result.TelemetryConsent.Should().Be("granted",
			because: "agents should not ask for consent again once it is granted");
	}

	[Test]
	[Category("Unit")]
	[Description("Stores denied consent locally and does not write telemetry events.")]
	public void TelemetryService_Should_NoOp_When_Consent_Is_Denied()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		TelemetryEventResult first = service.Send(CreateRequest() with { TelemetryConsent = "denied" });
		TelemetryEventResult second = service.Send(CreateRequest("business_plan_generated"));

		// Assert
		first.Success.Should().BeTrue(because: "denied consent should be accepted and persisted without failing the workflow");
		second.Status.Should().Be("consent-denied",
			because: "later telemetry events should silently no-op after a user denies consent");
		EventFiles().Should().BeEmpty(
			because: "denied consent must prevent local telemetry persistence");
		File.Exists(Path.Combine(_telemetryHome, "consent.json")).Should().BeTrue(
			because: "the consent decision must be preserved across sessions");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves a denied consent decision when a later telemetry includes stale granted consent.")]
	public void TelemetryService_Should_Not_Overwrite_Denied_Consent_With_Later_Granted_Value()
	{
		// Arrange
		TelemetryService service = CreateService();
		service.Send(CreateRequest() with { TelemetryConsent = "denied" });

		// Act
		TelemetryEventResult result = service.Send(CreateRequest("business_plan_generated") with { TelemetryConsent = "granted" });
		TelemetryConsentResult consent = service.GetConsentStatus();

		// Assert
		result.Status.Should().Be("consent-denied",
			because: "send-telemetry must not act as a consent update endpoint after an opt-out");
		consent.TelemetryConsent.Should().Be("denied",
			because: "a stale granted payload must not override the persisted denied decision");
		EventFiles().Should().BeEmpty(
			because: "no event should be stored after denied consent even when a later payload includes granted");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes the stable withdraw-telemetry-consent MCP tool name and marks it a mutating tool.")]
	public void WithdrawTelemetryConsentTool_Should_Expose_Stable_Tool_Name()
	{
		// Arrange / Act
		McpServerToolAttribute toolAttribute = (McpServerToolAttribute)typeof(WithdrawTelemetryConsentTool)
			.GetMethod(nameof(WithdrawTelemetryConsentTool.WithdrawTelemetryConsent))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		WithdrawTelemetryConsentTool.ToolName.Should().Be("withdraw-telemetry-consent",
			because: "the product telemetry contract calls this stable MCP tool name to withdraw consent");
		toolAttribute.Name.Should().Be(WithdrawTelemetryConsentTool.ToolName,
			because: "MCP discovery should advertise the same stable tool name agents use to opt out");
		toolAttribute.ReadOnly.Should().BeFalse(
			because: "withdrawal mutates the stored consent decision and purges local events");
	}

	[Test]
	[Category("Unit")]
	[Description("Delegates the withdraw MCP request to the telemetry service without wrapping the result.")]
	public void WithdrawTelemetryConsent_Should_Delegate_To_Telemetry_Service()
	{
		// Arrange
		ITelemetryService service = Substitute.For<ITelemetryService>();
		TelemetryConsentWithdrawalResult expected = new(true, "withdrawn", "denied", 3);
		service.WithdrawConsent().Returns(expected);
		WithdrawTelemetryConsentTool tool = new(service);

		// Act
		TelemetryConsentWithdrawalResult actual = tool.WithdrawTelemetryConsent();

		// Assert
		actual.Should().BeSameAs(expected,
			because: "the MCP tool should return the service result directly for clients");
		service.Received(1).WithdrawConsent();
	}

	[Test]
	[Category("Unit")]
	[Description("Withdraws granted consent: persists denied, and purges the not-yet-uploaded local outbox.")]
	public void TelemetryService_Should_Withdraw_Consent_And_Purge_Spooled_Events()
	{
		// Arrange
		TelemetryService service = CreateService();
		service.Send(CreateRequest() with { TelemetryConsent = "granted" });
		service.Send(CreateRequest("business_plan_generated"));
		EventFiles().Should().HaveCount(2,
			because: "two granted-consent events are spooled before withdrawal");

		// Act
		TelemetryConsentWithdrawalResult result = service.WithdrawConsent();

		// Assert
		result.Success.Should().BeTrue(
			because: "withdrawal should succeed without blocking the workflow");
		result.Status.Should().Be("withdrawn",
			because: "a successful withdrawal reports the withdrawn status");
		result.TelemetryConsent.Should().Be("denied",
			because: "withdrawal sets the stored decision to denied");
		result.EventsPurged.Should().Be(2,
			because: "both not-yet-uploaded local events should be purged on withdrawal");
		service.GetConsentStatus().TelemetryConsent.Should().Be("denied",
			because: "the withdrawn decision must be persisted and read back as denied");
		EventFiles().Should().BeEmpty(
			because: "the local outbox is cleared so opted-out events never upload");
		File.Exists(Path.Combine(_telemetryHome, "installation-id.txt")).Should().BeTrue(
			because: "the anonymous installation id is preserved across a withdrawal");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops persisting telemetry events after consent is withdrawn.")]
	public void TelemetryService_Should_Not_Collect_After_Withdrawal()
	{
		// Arrange
		TelemetryService service = CreateService();
		service.Send(CreateRequest() with { TelemetryConsent = "granted" });
		service.WithdrawConsent();

		// Act
		TelemetryEventResult afterWithdrawal = service.Send(CreateRequest("business_plan_generated"));

		// Assert
		afterWithdrawal.Status.Should().Be("consent-denied",
			because: "telemetry events must no-op after the user withdraws consent");
		EventFiles().Should().BeEmpty(
			because: "no event may be stored after withdrawal");
	}

	[Test]
	[Category("Unit")]
	[Description("Withdraws from an unknown (never-decided) state and is idempotent on repeat calls.")]
	public void TelemetryService_Should_Withdraw_From_Unknown_State_And_Be_Idempotent()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		TelemetryConsentWithdrawalResult first = service.WithdrawConsent();
		TelemetryConsentWithdrawalResult second = service.WithdrawConsent();

		// Assert
		first.Success.Should().BeTrue(
			because: "opting out before ever granting consent is valid");
		first.TelemetryConsent.Should().Be("denied",
			because: "withdrawal always lands on denied");
		first.EventsPurged.Should().Be(0,
			because: "there is nothing spooled to purge from an unknown state");
		second.Success.Should().BeTrue(
			because: "withdrawal is idempotent");
		second.TelemetryConsent.Should().Be("denied",
			because: "a repeat withdrawal keeps the denied decision");
		service.GetConsentStatus().TelemetryConsent.Should().Be("denied",
			because: "the denied decision persists across calls");
	}

	[Test]
	[Category("Unit")]
	[Description("get-tool-contract resolves the withdraw-telemetry-consent contract with its consent and purge-count outputs.")]
	public void GetToolContract_Should_Announce_Withdraw_Telemetry_Consent()
	{
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse response = tool.GetToolContracts(new ToolContractGetArgs([WithdrawTelemetryConsentTool.ToolName]));

		// Assert
		response.Success.Should().BeTrue(
			because: "the withdraw-telemetry-consent contract must resolve");
		ToolContractDefinition contract = response.Tools!.Single();
		contract.Name.Should().Be(WithdrawTelemetryConsentTool.ToolName);
		contract.OutputContract.Fields.Select(field => field.Name)
			.Should().Contain(["telemetry_consent", "events_purged"],
				because: "the contract must announce the consent result and the purged-event count");
	}

	[Test]
	[Category("Unit")]
	[Description("Persists granted-consent events as OpenTelemetry-shaped JSON files.")]
	public void TelemetryService_Should_Write_Otel_Event_To_Events_Directory()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		TelemetryEventResult result = service.Send(CreateRequest() with {
			TelemetryConsent = "granted",
			DurationMs = 12345
		});

		// Assert
		result.Success.Should().BeTrue(
			because: "granted consent should allow product telemetry persistence");
		result.Status.Should().Be("recorded",
			because: "the service should report that the event was recorded");
		string eventFile = EventFiles().Should().ContainSingle(
			because: "the service should write one local event file").Subject;
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(eventFile));
		document.RootElement.GetProperty("severity_text").GetString().Should().Be("INFO",
			because: "product telemetry events are telemetry info logs");
		document.RootElement.GetProperty("event_name").GetString().Should().Be("session_started",
			because: "the event name is stored once, in the dedicated OTel event_name field");
		document.RootElement.TryGetProperty("body", out _).Should().BeFalse(
			because: "single-source events carry no body; the event name lives only in event_name");
		JsonElement attributes = document.RootElement.GetProperty("attributes");
		AttributeValue(attributes, "schema_version").Should().Be("1",
			because: "every event must carry a schema_version so consumers can parse evolving payloads");
		AttributeValue(attributes, "duration_ms").Should().Be("12345",
			because: "duration_ms should be stored when the agent supplies it");
		AttributeValue(attributes, "installation_id").Should().NotBeNullOrWhiteSpace(
			because: "clio should enrich telemetry events with an anonymous installation identifier");
		// Pin the full ENG-89424 AC3 required-field set. The enrichment fields below are added by
		// BuildLogEvent and are NOT echoed from the request, so a regression that drops one (e.g.
		// GetPlatform()/GetClioVersion() returning empty, or an attribute being removed) would
		// otherwise pass the telemetry suite undetected.
		AttributeValue(attributes, "session_id").Should().NotBeNullOrWhiteSpace(
			because: "AC3 requires every event to carry the session_id");
		AttributeValue(attributes, "coding_agent").Should().NotBeNullOrWhiteSpace(
			because: "AC3 requires every event to carry the coding_agent");
		AttributeValue(attributes, "plugin_version").Should().NotBeNullOrWhiteSpace(
			because: "AC3 requires every event to carry the plugin_version");
		AttributeValue(attributes, "platform").Should().NotBeNullOrWhiteSpace(
			because: "AC3 requires every event to carry the platform; clio derives it locally");
		AttributeValue(attributes, "clio_version").Should().NotBeNullOrWhiteSpace(
			because: "AC3 requires every event to carry the clio_version; clio derives it locally");
		AttributeValue(attributes, "event_timestamp").Should().NotBeNullOrWhiteSpace(
			because: "AC3 requires every event to carry the event_timestamp; clio derives it locally");
	}

	[Test]
	[Category("Unit")]
	[Description("Infers duration_ms from local per-session stage timers when the agent does not provide duration.")]
	public void TelemetryService_Should_Infer_Duration_From_Local_Session_Timer()
	{
		// Arrange
		MutableTimeProvider time = new(DateTimeOffset.UnixEpoch);
		TelemetryService service = CreateService(time);

		// Act
		service.Send(CreateRequest("implementation_started") with { TelemetryConsent = "granted" });
		time.Advance(TimeSpan.FromMilliseconds(20));
		service.Send(CreateRequest("implementation_completed"));

		// Assert
		string completedFile = EventFiles().Single(path => EventName(path) == "implementation_completed");
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(completedFile));
		long.Parse(AttributeValue(document.RootElement.GetProperty("attributes"), "duration_ms")!).Should().BeGreaterThan(0,
			because: "implementation completion duration should be inferred from implementation_started in the same session");
	}

	[Test]
	[Category("Unit")]
	[Description("Adds overall duration since session start to later events without treating every event as a step duration.")]
	public void TelemetryService_Should_Add_Overall_Duration_To_Post_Start_Events()
	{
		// Arrange
		MutableTimeProvider time = new(DateTimeOffset.UnixEpoch);
		TelemetryService service = CreateService(time);

		// Act
		service.Send(CreateRequest() with { TelemetryConsent = "granted" });
		time.Advance(TimeSpan.FromMilliseconds(20));
		service.Send(CreateRequest("business_plan_feedback_received"));

		// Assert
		string feedbackFile = EventFiles().Single(path => EventName(path) == "business_plan_feedback_received");
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(feedbackFile));
		JsonElement attributes = document.RootElement.GetProperty("attributes");
		AttributeValue(attributes, "duration_ms").Should().BeNull(
			because: "feedback events are point-in-time events, not meaningful step-completion transitions");
		long.Parse(AttributeValue(attributes, "duration_since_session_start_ms")!).Should().BeGreaterThan(0,
			because: "every event after session_started should carry overall session age when the start is known");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not add duration_ms to implementation_started while still recording total session age.")]
	public void TelemetryService_Should_Not_Infer_Step_Duration_For_Implementation_Start()
	{
		// Arrange
		MutableTimeProvider time = new(DateTimeOffset.UnixEpoch);
		TelemetryService service = CreateService(time);

		// Act
		service.Send(CreateRequest() with { TelemetryConsent = "granted" });
		time.Advance(TimeSpan.FromMilliseconds(20));
		service.Send(CreateRequest("implementation_started"));

		// Assert
		string startedFile = EventFiles().Single(path => EventName(path) == "implementation_started");
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(startedFile));
		JsonElement attributes = document.RootElement.GetProperty("attributes");
		AttributeValue(attributes, "duration_ms").Should().BeNull(
			because: "implementation_started is not one of the approved step-duration transitions");
		long.Parse(AttributeValue(attributes, "duration_since_session_start_ms")!).Should().BeGreaterThan(0,
			because: "implementation_started should still expose overall duration since session start");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts post-implementation change request and change applied events.")]
	public void TelemetryService_Should_Accept_Post_Implementation_Change_Events()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		TelemetryEventResult requested = service.Send(CreateRequest("implementation_changes_requested") with {
			TelemetryConsent = "granted"
		});
		TelemetryEventResult applied = service.Send(CreateRequest("implementation_changes_applied"));

		// Assert
		requested.Success.Should().BeTrue(
			because: "post-completion rework requests are part of the product funnel");
		applied.Success.Should().BeTrue(
			because: "post-completion changes applied should be measurable separately from initial completion");
		EventFiles().Should().HaveCount(2,
			because: "both post-implementation events should be accepted and persisted");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unknown event names and unsupported fields before writing an event file.")]
	public void TelemetryService_Should_Reject_Invalid_Or_Unsupported_Payload()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		TelemetryEventResult unknownEvent = service.Send(CreateRequest("unknown_event") with { TelemetryConsent = "granted" });
		TelemetryEventResult unsupportedField = service.Send(CreateRequestWithUnsupportedPrompt());

		// Assert
		unknownEvent.Error!.Code.Should().Be("unknown-event-name",
			because: "only the product funnel event enum should be accepted");
		unsupportedField.Error!.Code.Should().Be("unsupported-fields",
			because: "the tool must accept only the allowlisted product telemetry fields");
		EventFiles().Should().BeEmpty(
			because: "invalid payloads must be rejected before persistence");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a soft non-throwing result when the local filesystem rejects a telemetry write.")]
	public void TelemetryService_Should_Return_Soft_Result_When_Storage_Write_Fails()
	{
		// Arrange
		System.IO.Abstractions.IFileSystem fileSystem = Substitute.For<System.IO.Abstractions.IFileSystem>();
		fileSystem.File.Exists(Arg.Any<string>()).Returns(false);
		fileSystem.File.When(file => file.WriteAllText(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Encoding>()))
			.Do(_ => throw new UnauthorizedAccessException("telemetry directory is read-only"));
		TelemetryService service = new(fileSystem, _telemetryHome);

		// Act
		TelemetryEventResult result = service.Send(CreateRequest() with { TelemetryConsent = "granted" });

		// Assert
		result.Success.Should().BeFalse(
			because: "a local I/O failure must be reported, not silently treated as recorded");
		result.Status.Should().Be("record-failed",
			because: "telemetry persistence errors degrade to a soft status instead of throwing into the MCP tool");
		result.Error!.Code.Should().Be("record-unavailable",
			because: "the caller receives a structured, non-throwing record error");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an over-long or malformed session_id before persisting any event.")]
	public void TelemetryService_Should_Reject_Invalid_Session_Id()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		TelemetryEventResult tooLong = service.Send(
			CreateRequest() with { SessionId = new string('a', 200), TelemetryConsent = "granted" });
		TelemetryEventResult badChars = service.Send(
			CreateRequest() with { SessionId = "has space/slash", TelemetryConsent = "granted" });

		// Assert
		tooLong.Error!.Code.Should().Be("invalid-session-id",
			because: "an over-long session_id is bounded to keep the local spool and wire payload small");
		badChars.Error!.Code.Should().Be("invalid-session-id",
			because: "session_id must be a safe identifier so it cannot smuggle content or unsafe path characters");
		EventFiles().Should().BeEmpty(
			because: "invalid identifiers must be rejected before any event is written");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an over-long scalar metadata field before persisting any event.")]
	public void TelemetryService_Should_Reject_Over_Long_Scalar_Field()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		TelemetryEventResult result = service.Send(
			CreateRequest() with { CodingAgent = new string('x', 100), TelemetryConsent = "granted" });

		// Assert
		result.Error!.Code.Should().Be("field-too-long",
			because: "agent-supplied free strings are length-bounded as defense in depth against oversized or PII-shaped values");
		EventFiles().Should().BeEmpty(
			because: "an oversized field must be rejected before persistence");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a negative client-supplied duration_ms before persisting any event.")]
	public void TelemetryService_Should_Reject_Negative_Duration()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		TelemetryEventResult result = service.Send(
			CreateRequest() with { DurationMs = -1, TelemetryConsent = "granted" });

		// Assert
		result.Error!.Code.Should().Be("invalid-duration",
			because: "a client-supplied duration_ms must be non-negative, matching the inferred path's Math.Max(0, ...) clamp");
		EventFiles().Should().BeEmpty(
			because: "an invalid duration must be rejected before persistence");
	}

	[Test]
	[Category("Unit")]
	[Description("Reuses the same anonymous installation_id across every event stored in one session.")]
	public void TelemetryService_Should_Reuse_InstallationId_Across_Events()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		service.Send(CreateRequest() with { TelemetryConsent = "granted" });
		service.Send(CreateRequest("business_plan_generated"));

		// Assert
		string[] eventFiles = EventFiles();
		eventFiles.Should().HaveCount(2,
			because: "both events in the session should be persisted");
		string[] installationIds = eventFiles
			.Select(path => InstallationId(path))
			.ToArray();
		installationIds.Should().OnlyContain(id => !string.IsNullOrWhiteSpace(id),
			because: "every event must carry the anonymous installation identifier");
		installationIds.Distinct().Should().ContainSingle(
			because: "the anonymous installation id is created once and reused for every later event");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a blank required field before persisting any event.")]
	public void TelemetryService_Should_Reject_Blank_Required_Field()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		TelemetryEventResult result = service.Send(
			CreateRequest() with { SessionId = "", TelemetryConsent = "granted" });

		// Assert
		result.Error!.Code.Should().Be("missing-required-field",
			because: "a blank required field must be rejected with a deterministic missing-required-field signal");
		EventFiles().Should().BeEmpty(
			because: "a payload with a blank required field must be rejected before persistence");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an unsupported telemetry_consent value before persisting any event.")]
	public void TelemetryService_Should_Reject_Unknown_Consent_Value()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		TelemetryEventResult result = service.Send(
			CreateRequest() with { TelemetryConsent = "maybe" });

		// Assert
		result.Error!.Code.Should().Be("unknown-consent",
			because: "telemetry_consent must be one of the documented granted/denied values");
		EventFiles().Should().BeEmpty(
			because: "a payload with an unsupported consent value must be rejected before persistence");
	}

	[Test]
	[Category("Unit")]
	[Description("Stores distinct session-state files for session ids a lossy sanitizer would have collapsed together.")]
	public void TelemetryService_Should_Not_Collide_Session_State_For_Similar_Session_Ids()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		service.Send(CreateRequest() with { SessionId = "sess.1", TelemetryConsent = "granted" });
		service.Send(CreateRequest() with { SessionId = "sess_1" });

		// Assert
		SessionFiles().Should().HaveCount(2,
			because: "session ids differing only in punctuation must not share duration-inference state");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not infer a step duration for a completion event when its start event is absent from the session.")]
	public void TelemetryService_Should_Not_Infer_Duration_Without_Start_Event()
	{
		// Arrange
		TelemetryService service = CreateService();

		// Act
		service.Send(CreateRequest("implementation_completed") with { TelemetryConsent = "granted" });

		// Assert
		string completedFile = EventFiles().Single();
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(completedFile));
		AttributeValue(document.RootElement.GetProperty("attributes"), "duration_ms").Should().BeNull(
			because: "implementation_completed without a prior implementation_started has no measurable step duration");
	}

	[Test]
	[Category("Unit")]
	[Description("get-tool-contract announces exactly the event names clio enforces (single source of truth).")]
	public void GetToolContract_Should_Announce_All_Enforced_Event_Names()
	{
		// Arrange
		ToolContractGetTool tool = new();

		// Act
		ToolContractGetResponse response = tool.GetToolContracts(new ToolContractGetArgs([SendTelemetryTool.ToolName]));

		// Assert
		response.Success.Should().BeTrue(
			because: "the send-telemetry contract must resolve");
		ToolContractField eventField = response.Tools!.Single().InputSchema.Properties
			.Single(field => field.Name == "event_name");
		foreach (string eventName in TelemetryService.AllowedEventNames) {
			eventField.Description.Should().Contain(eventName,
				because: "the announced event_name allow-list must not drift from the enforced allow-list");
		}
	}

	private string[] SessionFiles()
	{
		string sessionsDirectory = Path.Combine(_telemetryHome, "sessions");
		return Directory.Exists(sessionsDirectory)
			? Directory.GetFiles(sessionsDirectory, "*.json")
			: [];
	}

	private static TelemetryEventRequest CreateRequest(string eventName = "session_started") =>
		new(
			SessionId: "018f6e4a-0000-7000-9000-000000000001",
			EventName: eventName,
			CodingAgent: "Codex",
			PluginVersion: "0.1.0");

	private static TelemetryEventRequest CreateRequestWithUnsupportedPrompt() =>
		CreateRequest() with {
			TelemetryConsent = "granted",
			ExtensionData = new() {
				["prompt"] = JsonDocument.Parse("\"secret\"").RootElement.Clone()
			}
		};

	private TelemetryService CreateService() => new(new System.IO.Abstractions.FileSystem(), _telemetryHome);

	private TelemetryService CreateService(TimeProvider timeProvider) =>
		new(new System.IO.Abstractions.FileSystem(), _telemetryHome, timeProvider);

	private sealed class MutableTimeProvider : TimeProvider
	{
		private DateTimeOffset _utcNow;

		public MutableTimeProvider(DateTimeOffset start) => _utcNow = start;

		public void Advance(TimeSpan delta) => _utcNow += delta;

		public override DateTimeOffset GetUtcNow() => _utcNow;
	}

	private string[] EventFiles()
	{
		string eventsDirectory = Path.Combine(_telemetryHome, "events");
		return Directory.Exists(eventsDirectory)
			? Directory.GetFiles(eventsDirectory, "*.json")
			: [];
	}

	private static string AttributeValue(JsonElement attributes, string key)
	{
		foreach (JsonElement attribute in attributes.EnumerateArray()) {
			if (attribute.GetProperty("key").GetString() != key) {
				continue;
			}
			JsonElement value = attribute.GetProperty("value");
			if (value.TryGetProperty("string_value", out JsonElement stringValue)
				&& stringValue.ValueKind != JsonValueKind.Null) {
				return stringValue.GetString();
			}
			if (value.TryGetProperty("int_value", out JsonElement intValue)) {
				return intValue.GetInt64().ToString();
			}
		}
		return null;
	}

	private static string EventName(string eventPath)
	{
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(eventPath));
		return document.RootElement.GetProperty("event_name").GetString();
	}

	private static string InstallationId(string eventPath)
	{
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(eventPath));
		return AttributeValue(document.RootElement.GetProperty("attributes"), "installation_id");
	}
}
