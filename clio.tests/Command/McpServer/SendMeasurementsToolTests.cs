using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
public sealed class SendMeasurementsToolTests
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
	[Description("Exposes the stable send-measurements MCP tool name as a constant and attribute value.")]
	public void SendMeasurementsTool_Should_Expose_Stable_Tool_Name()
	{
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(SendMeasurementsTool)
			.GetMethod(nameof(SendMeasurementsTool.SendMeasurements))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		SendMeasurementsTool.ToolName.Should().Be("send-measurements",
			because: "the ADAC skill contract calls this stable MCP tool name");
		attribute.Name.Should().Be(SendMeasurementsTool.ToolName,
			because: "MCP discovery should advertise the same stable tool name that tests and agents use");
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes a read-only consent lookup tool so agents do not probe consent by sending analytics.")]
	public void GetMeasurementsConsentTool_Should_Expose_Stable_Tool_Name()
	{
		// Arrange / Act
		McpServerToolAttribute toolAttribute = (McpServerToolAttribute)typeof(GetMeasurementsConsentTool)
			.GetMethod(nameof(GetMeasurementsConsentTool.GetMeasurementsConsent))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		toolAttribute.Name.Should().Be(GetMeasurementsConsentTool.ToolName,
			because: "agents need a read-only way to inspect local consent before sending measurements");
		toolAttribute.ReadOnly.Should().BeTrue(
			because: "consent lookup must not write product analytics");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises the ADAC telemetry startup behavior in MCP server instructions so chat agents see it before tool calls.")]
	public void McpServerInstructions_Should_Advertise_Adac_Telemetry_Startup()
	{
		// Arrange
		string instructions = McpServerInstructions.Text;

		// Act / Assert
		instructions.Should().Contain("ADAC product telemetry",
			because: "Copilot and other chat agents receive MCP server instructions even before loading ADAC skill files");
		instructions.Should().Contain("send-measurements",
			because: "the chat model needs to know which MCP tool starts product telemetry");
		instructions.Should().Contain("get-measurements-consent",
			because: "agents should inspect local consent with a read-only tool before sending analytics");
		instructions.Should().Contain("event_name=session_started",
			because: "session_started is the first chat-level product event");
		instructions.Should().Contain("consent status is `unknown`",
			because: "agents should ask consent only when no local decision exists");
		instructions.Should().Contain("telemetry_consent=granted",
			because: "the startup instruction must include the consent payload shape");
	}

	[Test]
	[Category("Unit")]
	[Description("Delegates the MCP request to the measurement service without wrapping the structured result.")]
	public void SendMeasurements_Should_Delegate_To_Measurement_Service()
	{
		// Arrange
		IMeasurementService service = Substitute.For<IMeasurementService>();
		MeasurementRequest request = CreateRequest() with { TelemetryConsent = "granted" };
		MeasurementResult expected = new(true, "stored", "event-id");
		service.Send(request).Returns(expected);
		SendMeasurementsTool tool = new(service);

		// Act
		MeasurementResult actual = tool.SendMeasurements(request);

		// Assert
		actual.Should().BeSameAs(expected,
			because: "the MCP tool should return the service result directly for clients");
		service.Received(1).Send(request);
	}

	[Test]
	[Category("Unit")]
	[Description("Requires telemetry consent before the first measurement can be persisted.")]
	public void MeasurementService_Should_Require_Consent_On_First_Use()
	{
		// Arrange
		MeasurementService service = CreateService();

		// Act
		MeasurementResult result = service.Send(CreateRequest());

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
	[Description("Reads local consent without creating telemetry folders or storing measurement events.")]
	public void MeasurementService_Should_Read_Consent_Status_Without_Writing_Analytics()
	{
		// Arrange
		MeasurementService service = CreateService();

		// Act
		MeasurementConsentResult unknown = service.GetConsentStatus();

		// Assert
		unknown.TelemetryConsent.Should().Be("unknown",
			because: "missing consent should be reported without sending analytics");
		Directory.Exists(_telemetryHome).Should().BeFalse(
			because: "read-only consent lookup must not create telemetry state");
	}

	[Test]
	[Category("Unit")]
	[Description("Reads a persisted granted consent decision without requiring another user prompt.")]
	public void MeasurementService_Should_Return_Persisted_Consent_Status()
	{
		// Arrange
		MeasurementService service = CreateService();
		service.Send(CreateRequest() with { TelemetryConsent = "granted" });

		// Act
		MeasurementConsentResult result = service.GetConsentStatus();

		// Assert
		result.Status.Should().Be("known",
			because: "persisted consent should be reusable across sessions");
		result.TelemetryConsent.Should().Be("granted",
			because: "agents should not ask for consent again once it is granted");
	}

	[Test]
	[Category("Unit")]
	[Description("Stores denied consent locally and does not write measurement events.")]
	public void MeasurementService_Should_NoOp_When_Consent_Is_Denied()
	{
		// Arrange
		MeasurementService service = CreateService();

		// Act
		MeasurementResult first = service.Send(CreateRequest() with { TelemetryConsent = "denied" });
		MeasurementResult second = service.Send(CreateRequest("business_plan_generated"));

		// Assert
		first.Success.Should().BeTrue(because: "denied consent should be accepted and persisted without failing the workflow");
		second.Status.Should().Be("consent-denied",
			because: "later measurements should silently no-op after a user denies consent");
		EventFiles().Should().BeEmpty(
			because: "denied consent must prevent local measurement persistence");
		File.Exists(Path.Combine(_telemetryHome, "consent.json")).Should().BeTrue(
			because: "the consent decision must be preserved across sessions");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves a denied consent decision when a later measurement includes stale granted consent.")]
	public void MeasurementService_Should_Not_Overwrite_Denied_Consent_With_Later_Granted_Value()
	{
		// Arrange
		MeasurementService service = CreateService();
		service.Send(CreateRequest() with { TelemetryConsent = "denied" });

		// Act
		MeasurementResult result = service.Send(CreateRequest("business_plan_generated") with { TelemetryConsent = "granted" });
		MeasurementConsentResult consent = service.GetConsentStatus();

		// Assert
		result.Status.Should().Be("consent-denied",
			because: "send-measurements must not act as a consent update endpoint after an opt-out");
		consent.TelemetryConsent.Should().Be("denied",
			because: "a stale granted payload must not override the persisted denied decision");
		EventFiles().Should().BeEmpty(
			because: "no event should be stored after denied consent even when a later payload includes granted");
	}

	[Test]
	[Category("Unit")]
	[Description("Persists granted-consent events as OpenTelemetry-shaped JSON files.")]
	public void MeasurementService_Should_Write_Otel_Event_To_Events_Directory()
	{
		// Arrange
		MeasurementService service = CreateService();

		// Act
		MeasurementResult result = service.Send(CreateRequest() with {
			TelemetryConsent = "granted",
			DurationMs = 12345
		});

		// Assert
		result.Success.Should().BeTrue(
			because: "granted consent should allow product telemetry persistence");
		result.Status.Should().Be("stored",
			because: "the service should report local event persistence");
		string eventFile = EventFiles().Should().ContainSingle(
			because: "the service should write one local event file").Subject;
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(eventFile));
		document.RootElement.GetProperty("severity_text").GetString().Should().Be("INFO",
			because: "ADAC measurements are product telemetry info logs");
		document.RootElement.GetProperty("body").GetProperty("string_value").GetString().Should().Be("session_started",
			because: "the OTel body should carry the event name");
		JsonElement attributes = document.RootElement.GetProperty("attributes");
		AttributeValue(attributes, "duration_ms").Should().Be("12345",
			because: "duration_ms should be stored when the agent supplies it");
		AttributeValue(attributes, "installation_id").Should().NotBeNullOrWhiteSpace(
			because: "clio should enrich measurements with an anonymous installation identifier");
	}

	[Test]
	[Category("Unit")]
	[Description("Infers duration_ms from local per-session stage timers when the agent does not provide duration.")]
	public void MeasurementService_Should_Infer_Duration_From_Local_Session_Timer()
	{
		// Arrange
		MeasurementService service = CreateService();

		// Act
		service.Send(CreateRequest("implementation_started") with { TelemetryConsent = "granted" });
		Thread.Sleep(20);
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
	public void MeasurementService_Should_Add_Overall_Duration_To_Post_Start_Events()
	{
		// Arrange
		MeasurementService service = CreateService();

		// Act
		service.Send(CreateRequest() with { TelemetryConsent = "granted" });
		Thread.Sleep(20);
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
	public void MeasurementService_Should_Not_Infer_Step_Duration_For_Implementation_Start()
	{
		// Arrange
		MeasurementService service = CreateService();

		// Act
		service.Send(CreateRequest() with { TelemetryConsent = "granted" });
		Thread.Sleep(20);
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
	public void MeasurementService_Should_Accept_Post_Implementation_Change_Events()
	{
		// Arrange
		MeasurementService service = CreateService();

		// Act
		MeasurementResult requested = service.Send(CreateRequest("implementation_changes_requested") with {
			TelemetryConsent = "granted"
		});
		MeasurementResult applied = service.Send(CreateRequest("implementation_changes_applied"));

		// Assert
		requested.Success.Should().BeTrue(
			because: "post-completion rework requests are part of the ADAC product funnel");
		applied.Success.Should().BeTrue(
			because: "post-completion changes applied should be measurable separately from initial completion");
		EventFiles().Should().HaveCount(2,
			because: "both post-implementation events should be accepted and persisted");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unknown event names and unsupported fields before writing an event file.")]
	public void MeasurementService_Should_Reject_Invalid_Or_Unsupported_Payload()
	{
		// Arrange
		MeasurementService service = CreateService();

		// Act
		MeasurementResult unknownEvent = service.Send(CreateRequest("unknown_event") with { TelemetryConsent = "granted" });
		MeasurementResult unsupportedField = service.Send(CreateRequestWithUnsupportedPrompt());

		// Assert
		unknownEvent.Error!.Code.Should().Be("unknown-event-name",
			because: "only the ADAC product funnel event enum should be accepted");
		unsupportedField.Error!.Code.Should().Be("unsupported-fields",
			because: "the tool must accept only the allowlisted product telemetry fields");
		EventFiles().Should().BeEmpty(
			because: "invalid payloads must be rejected before persistence");
	}

	private static MeasurementRequest CreateRequest(string eventName = "session_started") =>
		new(
			SessionId: "018f6e4a-0000-7000-9000-000000000001",
			EventName: eventName,
			CodingAgent: "Codex",
			SkillVersion: "0.1.0",
			PluginVersion: "0.1.0");

	private static MeasurementRequest CreateRequestWithUnsupportedPrompt() =>
		CreateRequest() with {
			TelemetryConsent = "granted",
			ExtensionData = new() {
				["prompt"] = JsonDocument.Parse("\"secret\"").RootElement.Clone()
			}
		};

	private MeasurementService CreateService() => new(_telemetryHome);

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
		return document.RootElement.GetProperty("body").GetProperty("string_value").GetString();
	}
}
