using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class TelemetryFlushServiceTests
{
	private const string DefaultEndpoint = "https://telemetry.example.com/v1/logs";
	private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private string _telemetryHome;

	[SetUp]
	public void SetUp()
	{
		_telemetryHome = Path.Combine(Path.GetTempPath(), "clio-telemetry-flush-tests", Guid.NewGuid().ToString("N"));
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
	[Description("Keeps spooled events local and sends nothing when no telemetry endpoint is configured.")]
	public async Task FlushAsync_Should_Not_Post_When_Endpoint_Not_Configured()
	{
		// Arrange
		WriteEventFile(BaseTime);
		FakeHttpHandler handler = new();
		TelemetryFlushService service = CreateService(handler, endpoint: null);

		// Act
		await service.FlushAsync();

		// Assert
		handler.Requests.Should().BeEmpty(
			because: "uploading is disabled by default until an endpoint is configured");
		EventFiles().Should().ContainSingle(
			because: "spooled events must stay local while uploading is disabled");
	}

	[Test]
	[Category("Unit")]
	[Description("Sends nothing when the locally stored telemetry consent is not granted.")]
	public async Task FlushAsync_Should_Not_Post_When_Consent_Not_Granted()
	{
		// Arrange
		WriteEventFile(BaseTime);
		FakeHttpHandler handler = new();
		TelemetryFlushService service = CreateService(handler, consent: "denied");

		// Act
		await service.FlushAsync();

		// Assert
		handler.Requests.Should().BeEmpty(
			because: "events must never leave the machine unless consent is granted");
		EventFiles().Should().ContainSingle(
			because: "the flusher must not destroy events when it skips sending for consent reasons");
	}

	[Test]
	[Category("Unit")]
	[Description("Posts a spec-conformant OTLP/HTTP JSON envelope and deletes the sent files on success.")]
	public async Task FlushAsync_Should_Post_Spec_Conformant_Otlp_Envelope_When_Server_Accepts()
	{
		// Arrange
		WriteEventFile(BaseTime, "session_started");
		FakeHttpHandler handler = new();
		TelemetryFlushService service = CreateService(handler);

		// Act
		await service.FlushAsync();

		// Assert
		CapturedRequest request = handler.Requests.Should().ContainSingle(
			because: "one spooled event fits in a single OTLP batch").Subject;
		request.Uri!.AbsoluteUri.Should().Be(DefaultEndpoint,
			because: "the flusher must post to the configured OTLP logs endpoint");
		request.IngestKey.Should().BeNull(
			because: "no ingest-key header should be sent when none is configured");
		using JsonDocument document = JsonDocument.Parse(request.Body!);
		JsonElement resourceLogs = document.RootElement.GetProperty("resourceLogs");
		JsonElement resourceAttributes = resourceLogs[0].GetProperty("resource").GetProperty("attributes");
		resourceAttributes[0].GetProperty("key").GetString().Should().Be("service.name",
			because: "OTLP consumers group telemetry by the service.name resource attribute");
		resourceAttributes[0].GetProperty("value").GetProperty("stringValue").GetString().Should().Be("clio",
			because: "clio is the emitting service");
		JsonElement logRecord = resourceLogs[0].GetProperty("scopeLogs")[0].GetProperty("logRecords")[0];
		logRecord.GetProperty("timeUnixNano").ValueKind.Should().Be(JsonValueKind.String,
			because: "the OTLP JSON encoding maps int64 fields to JSON strings");
		logRecord.GetProperty("severityNumber").GetInt32().Should().Be(9,
			because: "severityNumber (9 = INFO) must accompany severityText so backends can classify severity numerically");
		logRecord.TryGetProperty("body", out _).Should().BeFalse(
			because: "the single-source design emits no body; the event name rides the dedicated eventName field only");
		logRecord.GetProperty("eventName").GetString().Should().Be("session_started",
			because: "the OTLP LogRecord event_name field is the single source of the event name, and the ClickHouse EventName column is populated from it");
		JsonElement durationAttribute = logRecord.GetProperty("attributes").EnumerateArray()
			.Single(attribute => attribute.GetProperty("key").GetString() == "duration_ms");
		durationAttribute.GetProperty("value").GetProperty("intValue").ValueKind.Should().Be(JsonValueKind.String,
			because: "OTLP intValue is an int64 and therefore a JSON string");
		EventFiles().Should().BeEmpty(
			because: "delivered events must be removed from the local spool");
	}

	[Test]
	[Category("Unit")]
	[Description("Uploads a large spool as ordered batches, oldest events first.")]
	public async Task FlushAsync_Should_Send_Batches_Oldest_First_When_Spool_Exceeds_Batch_Size()
	{
		// Arrange
		for (int index = 0; index < 120; index++) {
			WriteEventFile(BaseTime.AddSeconds(index), $"evt_{index:D3}");
		}
		FakeHttpHandler handler = new();
		TelemetryFlushService service = CreateService(
			handler, timeProvider: new MutableTimeProvider(BaseTime.AddHours(1)));

		// Act
		await service.FlushAsync();

		// Assert
		handler.Requests.Should().HaveCount(3,
			because: "120 events should upload as batches of 50, 50, and 20");
		handler.Requests.Select(request => LogRecordEventNames(request.Body!).Count)
			.Should().Equal([50, 50, 20], because: "each batch is capped at the maximum batch size");
		LogRecordEventNames(handler.Requests[0].Body!)[0].Should().Be("evt_000",
			because: "the flusher must upload the oldest spooled events first");
		LogRecordEventNames(handler.Requests[2].Body!)[^1].Should().Be("evt_119",
			because: "the newest event should be in the final batch");
		EventFiles().Should().BeEmpty(
			because: "all delivered batches must be removed from the spool");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops the multi-batch flush early when the cancellation token is signalled, leaving undelivered files spooled.")]
	public async Task FlushAsync_Should_Stop_Early_When_Cancellation_Requested()
	{
		// Arrange
		for (int index = 0; index < TelemetryFlushService.MaxBatchSize + 10; index++) {
			WriteEventFile(BaseTime.AddSeconds(index), $"evt_{index:D3}");
		}
		using CancellationTokenSource cts = new();
		FakeHttpHandler handler = new() { CancelAfterFirstRequest = cts };
		TelemetryFlushService service = CreateService(
			handler, timeProvider: new MutableTimeProvider(BaseTime.AddHours(1)));

		// Act
		await service.FlushAsync(cts.Token);

		// Assert
		handler.Requests.Should().ContainSingle(
			because: "cancellation after the first request must stop the run before the next batch is attempted");
		EventFiles().Length.Should().BeGreaterThanOrEqualTo(10,
			because: "the un-attempted second batch must stay spooled for the next trigger once the run is cancelled");
	}

	[Test]
	[Category("Unit")]
	[Description("Retries transient server failures, then stops the run and keeps the spool intact.")]
	public async Task FlushAsync_Should_Stop_And_Keep_Files_When_Transient_Failures_Exhaust_Attempts()
	{
		// Arrange
		for (int index = 0; index < 60; index++) {
			WriteEventFile(BaseTime.AddSeconds(index));
		}
		FakeHttpHandler handler = new();
		handler.Enqueue(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable,
			HttpStatusCode.ServiceUnavailable);
		TelemetryFlushService service = CreateService(
			handler, timeProvider: new MutableTimeProvider(BaseTime.AddHours(1)));
		// Skip the real backoff sleep so the retry path runs deterministically in milliseconds.
		service.DelayFactory = (_, _) => Task.CompletedTask;

		// Act
		await service.FlushAsync();

		// Assert
		handler.Requests.Should().HaveCount(TelemetryFlushService.PostAttempts,
			because: "a transient failure should be retried up to the attempt cap and never bleed into the next batch");
		EventFiles().Should().HaveCount(60,
			because: "undelivered events must stay spooled so the next trigger can retry");
	}

	[Test]
	[Category("Unit")]
	[Description("Delivers the batch on a later attempt when a transient failure recovers.")]
	public async Task FlushAsync_Should_Deliver_After_Retry_When_Server_Recovers()
	{
		// Arrange
		WriteEventFile(BaseTime);
		FakeHttpHandler handler = new();
		handler.Enqueue(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
		TelemetryFlushService service = CreateService(handler);
		// Skip the real backoff sleep so the retry path runs deterministically in milliseconds.
		service.DelayFactory = (_, _) => Task.CompletedTask;

		// Act
		await service.FlushAsync();

		// Assert
		handler.Requests.Should().HaveCount(2,
			because: "429 is throttling and should be retried with backoff");
		EventFiles().Should().BeEmpty(
			because: "the batch was accepted on the second attempt and must leave the spool");
	}

	[Test]
	[Category("Unit")]
	[Description("Drops a permanently rejected batch without retrying and continues with the next batch.")]
	public async Task FlushAsync_Should_Drop_Rejected_Batch_And_Continue_When_Server_Rejects_Permanently()
	{
		// Arrange
		for (int index = 0; index < 60; index++) {
			WriteEventFile(BaseTime.AddSeconds(index));
		}
		FakeHttpHandler handler = new();
		handler.Enqueue(HttpStatusCode.BadRequest, HttpStatusCode.OK);
		TelemetryFlushService service = CreateService(
			handler, timeProvider: new MutableTimeProvider(BaseTime.AddHours(1)));

		// Act
		await service.FlushAsync();

		// Assert
		handler.Requests.Should().HaveCount(2,
			because: "a 4xx rejection is permanent: no retry, but the next batch is still attempted");
		EventFiles().Should().BeEmpty(
			because: "telemetry is loss-tolerant by design — a poison batch must not wedge the spool");
	}

	[Test]
	[Category("Unit")]
	[Description("Prunes expired and excess spool files even when uploading is disabled.")]
	public async Task FlushAsync_Should_Prune_Spool_When_Sending_Disabled()
	{
		// Arrange
		MutableTimeProvider time = new(BaseTime);
		for (int index = 0; index < 5; index++) {
			WriteEventFile(BaseTime.AddDays(-(TelemetryFlushService.MaxSpoolAgeDays + 5)).AddSeconds(index));
		}
		string oldestKeptCandidate = null;
		string newestKeptCandidate = null;
		for (int index = 0; index < TelemetryFlushService.MaxSpoolFiles + 10; index++) {
			string path = WriteEventFile(BaseTime.AddHours(-1).AddSeconds(index));
			oldestKeptCandidate ??= path;
			newestKeptCandidate = path;
		}
		FakeHttpHandler handler = new();
		TelemetryFlushService service = CreateService(handler, endpoint: null, timeProvider: time);

		// Act
		await service.FlushAsync();

		// Assert
		handler.Requests.Should().BeEmpty(
			because: "pruning must not depend on a configured endpoint");
		EventFiles().Should().HaveCount(TelemetryFlushService.MaxSpoolFiles,
			because: "expired files and the oldest files above the size cap must be deleted locally");
		File.Exists(oldestKeptCandidate).Should().BeFalse(
			because: "the size cap must drop the oldest files first, so the oldest non-expired file is gone");
		File.Exists(newestKeptCandidate).Should().BeTrue(
			because: "the newest files within the size cap must survive pruning");
	}

	[Test]
	[Category("Unit")]
	[Description("Skips in-progress .json.tmp partials and deletes unparseable event files instead of sending them.")]
	public async Task FlushAsync_Should_Skip_Tmp_Files_And_Drop_Unparseable_Events()
	{
		// Arrange
		WriteEventFile(BaseTime, "session_started");
		WriteEventFile(BaseTime.AddSeconds(1), content: "{ not json ");
		string tmpPath = Path.Combine(EventsDirectory, $"{BaseTime.AddSeconds(2):yyyyMMddTHHmmssfffZ}_partial.json.tmp");
		File.WriteAllText(tmpPath, "partial");
		FakeHttpHandler handler = new();
		TelemetryFlushService service = CreateService(handler);

		// Act
		await service.FlushAsync();

		// Assert
		CapturedRequest request = handler.Requests.Should().ContainSingle(
			because: "only the valid event should be uploaded").Subject;
		LogRecordEventNames(request.Body!).Should().Equal(["session_started"],
			because: "tmp partials and corrupt files must never reach the collector");
		File.Exists(tmpPath).Should().BeTrue(
			because: "an in-progress .json.tmp file belongs to the writer and must be left alone");
		EventFiles().Should().BeEmpty(
			because: "the delivered event and the corrupt poison file should both leave the spool");
	}

	[Test]
	[Category("Unit")]
	[Description("Sends the configured ingest key as the X-Ingest-Key request header.")]
	public async Task FlushAsync_Should_Send_Ingest_Key_Header_When_Configured()
	{
		// Arrange
		WriteEventFile(BaseTime);
		FakeHttpHandler handler = new();
		TelemetryFlushService service = CreateService(handler, ingestKey: "public-key-123");

		// Act
		await service.FlushAsync();

		// Assert
		handler.Requests.Should().ContainSingle(
				because: "one event uploads as one batch").Subject
			.IngestKey.Should().Be("public-key-123",
				because: "the edge collector filters casual noise by the configured ingest-key header");
	}

	[Test]
	[Category("Unit")]
	[Description("Prunes session-state files older than the age cap and keeps recent ones, independent of the event spool.")]
	public async Task FlushAsync_Should_Prune_Stale_Session_State_Files()
	{
		// Arrange
		string sessionsDirectory = Path.Combine(_telemetryHome, "sessions");
		Directory.CreateDirectory(sessionsDirectory);
		string stalePath = Path.Combine(sessionsDirectory, "stale-session.json");
		string freshPath = Path.Combine(sessionsDirectory, "fresh-session.json");
		File.WriteAllText(stalePath, "{}");
		File.WriteAllText(freshPath, "{}");
		File.SetLastWriteTimeUtc(stalePath, BaseTime.AddDays(-(TelemetryFlushService.MaxSpoolAgeDays + 5)).UtcDateTime);
		File.SetLastWriteTimeUtc(freshPath, BaseTime.AddHours(-1).UtcDateTime);
		FakeHttpHandler handler = new();
		TelemetryFlushService service = CreateService(handler, endpoint: null, timeProvider: new MutableTimeProvider(BaseTime));

		// Act
		await service.FlushAsync();

		// Assert
		File.Exists(stalePath).Should().BeFalse(
			because: "session-state files older than the age cap are reclaimed so the directory cannot grow without bound");
		File.Exists(freshPath).Should().BeTrue(
			because: "recent session-state files are still needed for in-session duration inference");
		handler.Requests.Should().BeEmpty(
			because: "session pruning runs regardless of whether an upload endpoint is configured");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a really-stored event onto spec-valid OTLP without dropping attributes (storage-to-wire round trip).")]
	public async Task FlushAsync_Should_Map_Stored_Event_To_Otlp_Without_Dropping_Attributes()
	{
		// Arrange: write through the real store so the on-disk shape is authoritative, not hand-built.
		TelemetryService store = new(new System.IO.Abstractions.FileSystem(), _telemetryHome,
			new MutableTimeProvider(BaseTime));
		store.Send(new TelemetryEventRequest("sess-roundtrip", "business_plan_generated", "Codex", "0.2.0")
			with { TelemetryConsent = "granted" });
		FakeHttpHandler handler = new();
		TelemetryFlushService service = CreateService(handler);

		// Act
		await service.FlushAsync();

		// Assert
		CapturedRequest request = handler.Requests.Should().ContainSingle(
			because: "the single stored event should upload as one OTLP batch").Subject;
		using JsonDocument document = JsonDocument.Parse(request.Body!);
		JsonElement attributes = document.RootElement
			.GetProperty("resourceLogs")[0].GetProperty("scopeLogs")[0]
			.GetProperty("logRecords")[0].GetProperty("attributes");
		AttributeString(attributes, "session_id").Should().Be("sess-roundtrip",
			because: "the stored session_id attribute must survive the store-to-wire mapping");
		AttributeString(attributes, "coding_agent").Should().Be("Codex",
			because: "agent metadata must survive the mapping");
		AttributeString(attributes, "schema_version").Should().Be("1",
			because: "the schema_version enrichment must survive the mapping");
	}

	private static string AttributeString(JsonElement attributes, string key) =>
		attributes.EnumerateArray()
			.Single(attribute => attribute.GetProperty("key").GetString() == key)
			.GetProperty("value").GetProperty("stringValue").GetString();

	[Test]
	[Category("Unit")]
	[Description("Reaps crash-orphaned .json.tmp writer files older than the grace period.")]
	public async Task FlushAsync_Should_Reap_Aged_Orphan_Tmp_Files()
	{
		// Arrange
		Directory.CreateDirectory(EventsDirectory);
		string agedTmp = Path.Combine(EventsDirectory,
			$"{BaseTime.AddHours(-48):yyyyMMddTHHmmssfffZ}_orphan.json.tmp");
		File.WriteAllText(agedTmp, "half-written");
		FakeHttpHandler handler = new();
		TelemetryFlushService service = CreateService(
			handler, endpoint: null, timeProvider: new MutableTimeProvider(BaseTime.AddHours(1)));

		// Act
		await service.FlushAsync();

		// Assert
		File.Exists(agedTmp).Should().BeFalse(
			because: "a .json.tmp orphaned by a crashed writer must be reclaimed once it is clearly past any live write");
	}

	[Test]
	[Category("Unit")]
	[Description("Sends nothing when the locally stored telemetry consent is unknown (not just denied).")]
	public async Task FlushAsync_Should_Not_Post_When_Consent_Unknown()
	{
		// Arrange
		WriteEventFile(BaseTime);
		FakeHttpHandler handler = new();
		TelemetryFlushService service = CreateService(handler, consent: "unknown");

		// Act
		await service.FlushAsync();

		// Assert
		handler.Requests.Should().BeEmpty(
			because: "events must never leave the machine unless consent is explicitly granted");
		EventFiles().Should().ContainSingle(
			because: "the flusher must keep events spooled while consent is still unknown");
	}

	[Test]
	[Category("Unit")]
	[Description("Stops an in-progress multi-batch flush when consent is withdrawn mid-run, leaving the unsent batches spooled.")]
	public async Task FlushAsync_Should_Stop_Remaining_Batches_When_Consent_Withdrawn_Mid_Flush()
	{
		// Arrange: a spool larger than one batch, so the per-batch consent re-check runs more than once.
		for (int index = 0; index < 120; index++) {
			WriteEventFile(BaseTime.AddSeconds(index), $"evt_{index:D3}");
		}
		FakeHttpHandler handler = new();
		ITelemetryService telemetryService = Substitute.For<ITelemetryService>();
		// Granted for the top-of-flush gate and the first batch's re-check, then withdrawn: the flusher
		// re-reads consent before EACH batch (TelemetryFlushService.FlushCoreAsync), so a denial returned
		// on the third read must stop the run after the single batch already read into memory.
		telemetryService.GetConsentStatus().Returns(
			new TelemetryConsentResult(true, "known", "granted"),
			new TelemetryConsentResult(true, "known", "granted"),
			new TelemetryConsentResult(true, "known", "denied"));
		ITelemetryFlushOptionsProvider optionsProvider = Substitute.For<ITelemetryFlushOptionsProvider>();
		optionsProvider.Resolve().Returns(new TelemetryFlushOptions(DefaultEndpoint, null));
		TelemetryFlushService service = new(
			new System.IO.Abstractions.FileSystem(),
			new FakeHttpClientFactory(handler),
			telemetryService,
			optionsProvider,
			_telemetryHome,
			new MutableTimeProvider(BaseTime.AddHours(1)),
			NullLogger<TelemetryFlushService>.Instance);

		// Act
		await service.FlushAsync();

		// Assert
		handler.Requests.Should().ContainSingle(
			because: "withdrawing consent mid-flush must stop further uploads after the in-flight batch");
		LogRecordEventNames(handler.Requests[0].Body!).Should().HaveCount(TelemetryFlushService.MaxBatchSize,
			because: "only the first batch, read into memory before the withdrawal, may be delivered");
		EventFiles().Should().HaveCount(120 - TelemetryFlushService.MaxBatchSize,
			because: "batches not yet attempted when consent was withdrawn must stay spooled for a later run");
	}

	private TelemetryFlushService CreateService(FakeHttpHandler handler, string endpoint = DefaultEndpoint,
		string ingestKey = null, string consent = "granted", TimeProvider timeProvider = null)
	{
		ITelemetryService telemetryService = Substitute.For<ITelemetryService>();
		telemetryService.GetConsentStatus().Returns(new TelemetryConsentResult(true, "known", consent));
		ITelemetryFlushOptionsProvider optionsProvider = Substitute.For<ITelemetryFlushOptionsProvider>();
		optionsProvider.Resolve().Returns(new TelemetryFlushOptions(endpoint, ingestKey));
		return new TelemetryFlushService(
			new System.IO.Abstractions.FileSystem(),
			new FakeHttpClientFactory(handler),
			telemetryService,
			optionsProvider,
			_telemetryHome,
			timeProvider ?? new MutableTimeProvider(BaseTime.AddHours(1)),
			NullLogger<TelemetryFlushService>.Instance);
	}

	private string EventsDirectory => Path.Combine(_telemetryHome, "events");

	private string WriteEventFile(DateTimeOffset timestamp, string eventName = "session_started", string content = null)
	{
		Directory.CreateDirectory(EventsDirectory);
		string eventId = Guid.NewGuid().ToString("N");
		string path = Path.Combine(EventsDirectory, $"{timestamp:yyyyMMddTHHmmssfffZ}_{eventId}.json");
		File.WriteAllText(path, content ?? EventJson(eventName, eventId));
		return path;
	}

	private static string EventJson(string eventName, string eventId) =>
		$$"""
		{
			"time_unix_nano": 1767225600000000000,
			"severity_text": "INFO",
			"event_name": "{{eventName}}",
			"attributes": [
				{ "key": "event_id", "value": { "string_value": "{{eventId}}" } },
				{ "key": "duration_ms", "value": { "int_value": 12345 } }
			]
		}
		""";

	private string[] EventFiles() =>
		Directory.Exists(EventsDirectory)
			? Directory.GetFiles(EventsDirectory, "*.json")
			: [];

	private static List<string> LogRecordEventNames(string requestBody)
	{
		using JsonDocument document = JsonDocument.Parse(requestBody);
		return document.RootElement.GetProperty("resourceLogs")[0]
			.GetProperty("scopeLogs")[0]
			.GetProperty("logRecords")
			.EnumerateArray()
			.Select(record => record.GetProperty("eventName").GetString())
			.ToList();
	}

	private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => new(handler) {
			// Short timeout keeps the test fast in case of an accidental real network call.
			Timeout = TimeSpan.FromSeconds(5)
		};
	}

	private sealed record CapturedRequest(Uri Uri, string Body, string IngestKey);

	private sealed class FakeHttpHandler : HttpMessageHandler
	{
		private readonly Queue<HttpStatusCode> _statuses = new();

		public List<CapturedRequest> Requests { get; } = [];

		/// <summary>
		/// When set, the source is cancelled after the first request is captured (but the response is
		/// still returned), so a caller passing this token stops before attempting the next batch.
		/// </summary>
		public CancellationTokenSource CancelAfterFirstRequest { get; set; }

		public void Enqueue(params HttpStatusCode[] statuses)
		{
			foreach (HttpStatusCode status in statuses) {
				_statuses.Enqueue(status);
			}
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			string body = request.Content is null
				? null
				: await request.Content.ReadAsStringAsync(cancellationToken);
			request.Headers.TryGetValues(TelemetryFlushService.IngestKeyHeaderName, out IEnumerable<string> values);
			Requests.Add(new CapturedRequest(request.RequestUri, body, values?.FirstOrDefault()));
			HttpStatusCode status = _statuses.Count > 0 ? _statuses.Dequeue() : HttpStatusCode.OK;
			CancelAfterFirstRequest?.Cancel();
			return new HttpResponseMessage(status);
		}
	}

	private sealed class MutableTimeProvider : TimeProvider
	{
		private DateTimeOffset _utcNow;

		public MutableTimeProvider(DateTimeOffset start) => _utcNow = start;

		public override DateTimeOffset GetUtcNow() => _utcNow;
	}
}
