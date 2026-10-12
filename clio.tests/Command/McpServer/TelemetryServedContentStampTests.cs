using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Clio.Common.Telemetry;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-100157: the served-content counters clio stamps on the events it records.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class TelemetryServedContentStampTests {
	private static readonly string[] ServedContentKeys = [
		"guidance_reads", "guidance_rereads", "guidance_bytes", "contract_reads", "contract_bytes",
		"guidance_library_version"
	];

	private string _telemetryHome;

	[SetUp]
	public void SetUp() {
		_telemetryHome = Path.Combine(Path.GetTempPath(), "clio-telemetry-tests", Guid.NewGuid().ToString("N"));
	}

	[TearDown]
	public void TearDown() {
		if (Directory.Exists(_telemetryHome)) {
			Directory.Delete(_telemetryHome, recursive: true);
		}
	}

	[Test]
	[Description("Stamps what the process served the agent (reads, re-reads, bytes, contracts and the library version) on the event it records, as schema 3.")]
	public void Send_ShouldStampServedContent_WhenTheProcessServedContent() {
		// Arrange
		ServedContentMeter meter = new();
		meter.RecordGuidance("process-modeling", "1.16.2", 1_000);
		meter.RecordGuidance("process-modeling", "1.16.2", 1_000);
		meter.RecordGuidance(null, null, 300);
		meter.RecordContract(5_000);
		TelemetryService service = CreateService(meter);

		// Act
		TelemetryEventResult result = service.Send(CreateRequest("workflow_started"));

		// Assert
		result.Status.Should().Be("recorded",
			because: "a granted-consent stage event is stored");
		JsonElement attributes = ReadSingleEventAttributes();
		IntValue(attributes, "guidance_reads").Should().Be(2,
			because: "two articles were served, the repeated one included");
		IntValue(attributes, "guidance_rereads").Should().Be(1,
			because: "one article was served again after the session had already received it");
		IntValue(attributes, "guidance_bytes").Should().Be(2_300,
			because: "every get-guidance response the agent received counts, the refusal included");
		IntValue(attributes, "contract_reads").Should().Be(1,
			because: "one contract response was served");
		IntValue(attributes, "contract_bytes").Should().Be(5_000,
			because: "the contract bytes are stamped as served");
		StringValue(attributes, "guidance_library_version").Should().Be("1.16.2",
			because: "the guidance generation is what tells a before from an after once the library changes, which clio_version cannot");
		StringValue(attributes, "schema_version").Should().Be("3",
			because: "the served-content attributes are a new payload shape that a consumer routes on");
	}

	[Test]
	[Description("Stamps a running total, so a later stage of the same process reports everything served up to it.")]
	public void Send_ShouldStampARunningTotal_WhenMoreIsServedBetweenStages() {
		// Arrange
		ServedContentMeter meter = new();
		TelemetryService service = CreateService(meter);
		meter.RecordGuidance("routing", "1.16.2", 100);
		service.Send(CreateRequest("workflow_started"));

		// Act
		meter.RecordGuidance("process-modeling", "1.16.2", 900);
		service.Send(CreateRequest("plan_presented"));

		// Assert
		JsonElement started = ReadEventAttributes("workflow_started");
		JsonElement presented = ReadEventAttributes("plan_presented");
		IntValue(started, "guidance_bytes").Should().Be(100,
			because: "the first stage reports what had been served by then");
		IntValue(presented, "guidance_bytes").Should().Be(1_000,
			because: "the counters are cumulative, so the difference between two stages is what was served between them");
	}

	[Test]
	[Description("Stamps nothing when the process served nothing, so the hook's own short-lived processes do not report sessions that cost nothing.")]
	public void Send_ShouldNotStampServedContent_WhenTheProcessServedNothing() {
		// Arrange
		TelemetryService service = CreateService(new ServedContentMeter());

		// Act
		service.Send(CreateRequest("workflow_started"));

		// Assert
		JsonElement attributes = ReadSingleEventAttributes();
		KeysOf(attributes).Should().NotContain(ServedContentKeys,
			because: "an absent counter says 'not measured here', where a zero would claim a session that cost nothing");
	}

	[Test]
	[Description("Stamps nothing when no meter is supplied, keeping the enrichment fail-soft.")]
	public void Send_ShouldNotStampServedContent_WhenNoMeterIsSupplied() {
		// Arrange
		TelemetryService service = new(new System.IO.Abstractions.FileSystem(), _telemetryHome);

		// Act
		TelemetryEventResult result = service.Send(CreateRequest("workflow_started"));

		// Assert
		result.Status.Should().Be("recorded",
			because: "the event itself does not depend on the meter");
		KeysOf(ReadSingleEventAttributes()).Should().NotContain(ServedContentKeys,
			because: "without a meter there is nothing to stamp");
	}

	[Test]
	[Description("Keeps the counters but drops a library version that is not plainly numeric, since a suffix is free text its publisher chose.")]
	public void Send_ShouldDropTheLibraryVersion_WhenItIsNotPlainlyNumeric() {
		// Arrange
		ServedContentMeter meter = new();
		meter.RecordGuidance("routing", "2.0.0-acme-bank", 100);
		TelemetryService service = CreateService(meter);

		// Act
		service.Send(CreateRequest("workflow_started"));

		// Assert
		JsonElement attributes = ReadSingleEventAttributes();
		IntValue(attributes, "guidance_reads").Should().Be(1,
			because: "the counters are clio's own numbers and stay valid");
		KeysOf(attributes).Should().NotContain("guidance_library_version",
			because: "a pre-release suffix can name a customer, so anything but a plain numeric version is dropped rather than stored");
	}

	[TestCase("1.16.2", true)]
	[TestCase("1.15.97.0", true)]
	[TestCase("10.1", true)]
	[TestCase("1", false)]
	[TestCase("1.16.2-beta", false)]
	[TestCase("2.0.0-acme-bank", false)]
	[TestCase("v1.16.2", false)]
	[TestCase("1..2", false)]
	[TestCase("1.2.3.4.5", false)]
	[TestCase("1234567890.1", false)]
	[TestCase(" 1.16.2", false)]
	[TestCase("", false)]
	[TestCase(null, false)]
	[Description("Accepts only two to four dot-separated groups of one to nine digits as a library version.")]
	public void IsAllowedLibraryVersion_ShouldAcceptOnlyPlainNumericVersions_WhenGivenACandidate(string version, bool expected) {
		// Arrange

		// Act
		bool allowed = TelemetryService.IsAllowedLibraryVersion(version);

		// Assert
		allowed.Should().Be(expected,
			because: "only a plain published version is safe to store; anything else may carry its publisher's free text");
	}

	[Test]
	[Description("Refuses a served-content field sent by the caller, so the agent can never forge what clio served.")]
	public void Send_ShouldRejectTheEvent_WhenTheCallerSendsAServedContentField() {
		// Arrange
		ServedContentMeter meter = new();
		meter.RecordGuidance("routing", "1.16.2", 100);
		TelemetryService service = CreateService(meter);
		TelemetryEventRequest forged = CreateRequest("workflow_started") with {
			ExtensionData = new() {
				["guidance_bytes"] = JsonDocument.Parse("1").RootElement.Clone()
			}
		};

		// Act
		TelemetryEventResult result = service.Send(forged);

		// Assert
		result.Error!.Code.Should().Be("unsupported-fields",
			because: "the served-content counters are measured by clio, never accepted from the caller");
		EventFiles().Should().BeEmpty(
			because: "a rejected event is never stored");
	}

	private TelemetryService CreateService(IServedContentMeter meter) =>
		new(new System.IO.Abstractions.FileSystem(), _telemetryHome, servedContentMeter: meter);

	private static TelemetryEventRequest CreateRequest(string eventName) =>
		new(
			SessionId: "018f6e4a-0000-7000-9000-000000000157",
			EventName: eventName,
			TelemetryConsent: "granted",
			Workflow: "app-creation");

	private string[] EventFiles() {
		string eventsDirectory = Path.Combine(_telemetryHome, "events");
		return Directory.Exists(eventsDirectory)
			? Directory.GetFiles(eventsDirectory, "*.json")
			: [];
	}

	private JsonElement ReadSingleEventAttributes() {
		string eventFile = EventFiles().Should().ContainSingle(
			because: "exactly one event was sent").Subject;
		return ReadAttributes(eventFile);
	}

	private JsonElement ReadEventAttributes(string eventName) {
		string eventFile = EventFiles().Single(path => {
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			return document.RootElement.GetProperty("event_name").GetString() == eventName;
		});
		return ReadAttributes(eventFile);
	}

	private static JsonElement ReadAttributes(string eventFile) {
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(eventFile));
		return document.RootElement.GetProperty("attributes").Clone();
	}

	private static string[] KeysOf(JsonElement attributes) =>
		attributes.EnumerateArray().Select(attribute => attribute.GetProperty("key").GetString()).ToArray();

	private static long? IntValue(JsonElement attributes, string key) =>
		attributes.EnumerateArray()
			.Where(attribute => attribute.GetProperty("key").GetString() == key)
			.Select(attribute => (long?)attribute.GetProperty("value").GetProperty("int_value").GetInt64())
			.SingleOrDefault();

	private static string StringValue(JsonElement attributes, string key) =>
		attributes.EnumerateArray()
			.Where(attribute => attribute.GetProperty("key").GetString() == key)
			.Select(attribute => attribute.GetProperty("value").GetProperty("string_value").GetString())
			.SingleOrDefault();
}
