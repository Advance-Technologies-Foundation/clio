using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ms = System.IO.Abstractions;

namespace Clio.Common.Telemetry;

/// <summary>
/// Uploads locally spooled product telemetry events to the configured OTLP/HTTP collector.
/// </summary>
public interface IMeasurementFlushService
{
	/// <summary>
	/// Prunes the local event spool (age and size caps), then uploads spooled events as
	/// OTLP/HTTP JSON batches when an endpoint is configured and telemetry consent is granted.
	/// Never throws to the caller.
	/// </summary>
	Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class MeasurementFlushService : IMeasurementFlushService
{
	/// <summary>
	/// Named HttpClient used for telemetry uploads. The timeout is configured once at
	/// registration in <c>BindingsModule</c> — never mutated on resolved instances.
	/// </summary>
	internal const string HttpClientName = "telemetry-flush";

	/// <summary>
	/// Request header carrying the public ingest key. Must match the edge-collector configuration.
	/// </summary>
	internal const string IngestKeyHeaderName = "X-Ingest-Key";

	internal const int MaxBatchSize = 50;
	internal const int MaxSpoolFiles = 500;
	internal const int MaxSpoolAgeDays = 30;
	internal const int PostAttempts = 3;
	internal static readonly TimeSpan PostTimeout = TimeSpan.FromSeconds(30);

	private const string EventFileSuffix = ".json";
	private const string ServiceName = "clio";

	/// <summary>
	/// Parse format for the event filename timestamp prefix written by
	/// <see cref="MeasurementService"/> (<c>yyyyMMddTHHmmssfffZ_{eventId}.json</c>);
	/// the trailing <c>Z</c> is a literal and is validated separately.
	/// </summary>
	private const string FileTimestampFormat = "yyyyMMddTHHmmssfff";
	private const int FileTimestampLength = 18;

	private static readonly JsonSerializerOptions ReadJsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};
	private static readonly JsonSerializerOptions WireJsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly Ms.IFileSystem _fileSystem;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IMeasurementService _measurementService;
	private readonly IMeasurementFlushOptionsProvider _optionsProvider;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<MeasurementFlushService> _logger;
	private readonly string _telemetryRoot;

	/// <summary>
	/// Initializes a new instance of the <see cref="MeasurementFlushService"/> class.
	/// </summary>
	/// <param name="fileSystem">Filesystem abstraction used for all spool I/O.</param>
	/// <param name="httpClientFactory">Factory resolving the named telemetry upload client.</param>
	/// <param name="measurementService">Source of the locally stored consent decision.</param>
	/// <param name="optionsProvider">Resolves endpoint and ingest-key configuration per run.</param>
	/// <param name="telemetryRoot">
	/// Optional local storage root. When omitted, the root is taken from the
	/// <c>CLIO_TELEMETRY_HOME</c> environment variable or the default user-profile location.
	/// </param>
	/// <param name="timeProvider">Optional time source for spool age pruning; defaults to system time.</param>
	/// <param name="logger">Optional diagnostics logger; silent when omitted.</param>
	public MeasurementFlushService(
		Ms.IFileSystem fileSystem,
		IHttpClientFactory httpClientFactory,
		IMeasurementService measurementService,
		IMeasurementFlushOptionsProvider optionsProvider,
		string telemetryRoot = null,
		TimeProvider timeProvider = null,
		ILogger<MeasurementFlushService> logger = null)
	{
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_measurementService = measurementService ?? throw new ArgumentNullException(nameof(measurementService));
		_optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
		_timeProvider = timeProvider ?? TimeProvider.System;
		_logger = logger ?? NullLogger<MeasurementFlushService>.Instance;
		_telemetryRoot = TelemetryStoragePaths.ResolveRoot(telemetryRoot);
	}

	/// <inheritdoc />
	public async Task FlushAsync(CancellationToken cancellationToken = default)
	{
		try {
			await FlushCoreAsync(cancellationToken).ConfigureAwait(false);
		} catch (Exception ex) {
			// Telemetry must never disturb the caller — swallow and log only.
			_logger.LogDebug(ex, "telemetry-flush failed error={Error}", ex.Message);
		}
	}

	private async Task FlushCoreAsync(CancellationToken cancellationToken)
	{
		string eventsDirectory = TelemetryStoragePaths.EventsDirectory(_telemetryRoot);
		if (!_fileSystem.Directory.Exists(eventsDirectory)) {
			return;
		}
		List<string> spool = ListSpool(eventsDirectory);
		spool = PruneSpool(spool);
		if (spool.Count == 0) {
			return;
		}
		MeasurementFlushOptions options = _optionsProvider.Resolve();
		if (!options.IsSendingEnabled) {
			_logger.LogDebug("telemetry-flush skipped reason=endpoint-not-configured spool={Count}", spool.Count);
			return;
		}
		if (_measurementService.GetConsentStatus().TelemetryConsent != MeasurementService.ConsentGranted) {
			_logger.LogDebug("telemetry-flush skipped reason=consent-not-granted spool={Count}", spool.Count);
			return;
		}
		HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
		int offset = 0;
		while (offset < spool.Count && !cancellationToken.IsCancellationRequested) {
			List<string> batchFiles = spool.Skip(offset).Take(MaxBatchSize).ToList();
			offset += batchFiles.Count;
			List<(string Path, OpenTelemetryLogEvent Event)> batch = ReadBatch(batchFiles);
			if (batch.Count == 0) {
				continue;
			}
			string payload = BuildOtlpPayload(batch.Select(item => item.Event).ToList());
			PostOutcome outcome = await PostWithRetryAsync(http, options, payload, cancellationToken).ConfigureAwait(false);
			switch (outcome) {
				case PostOutcome.Delivered:
					DeleteFiles(batch.Select(item => item.Path));
					_logger.LogDebug("telemetry-flush delivered events={Count}", batch.Count);
					break;
				case PostOutcome.PermanentRejection:
					// Loss-tolerant by design: a rejected batch must not wedge the spool.
					DeleteFiles(batch.Select(item => item.Path));
					_logger.LogDebug("telemetry-flush dropped reason=permanent-rejection events={Count}", batch.Count);
					break;
				default:
					// Transient failure after all attempts — keep files; next trigger retries.
					_logger.LogDebug("telemetry-flush stopped reason=transient-failure remaining={Count}",
						spool.Count - offset + batch.Count);
					return;
			}
		}
	}

	private List<string> ListSpool(string eventsDirectory)
	{
		try {
			return _fileSystem.Directory.GetFiles(eventsDirectory)
				.Where(path => path.EndsWith(EventFileSuffix, StringComparison.Ordinal))
				.OrderBy(System.IO.Path.GetFileName, StringComparer.Ordinal)
				.ToList();
		} catch (Exception ex) {
			_logger.LogDebug(ex, "telemetry-flush spool-list failed error={Error}", ex.Message);
			return [];
		}
	}

	private List<string> PruneSpool(List<string> spool)
	{
		DateTimeOffset cutoff = _timeProvider.GetUtcNow().AddDays(-MaxSpoolAgeDays);
		List<string> remaining = new(spool.Count);
		foreach (string path in spool) {
			DateTimeOffset? writtenAt = TryParseFileTimestamp(System.IO.Path.GetFileName(path));
			if (writtenAt is null || writtenAt < cutoff) {
				// Unparseable prefixes are treated as expired so foreign files cannot pile up.
				DeleteFile(path);
				continue;
			}
			remaining.Add(path);
		}
		if (remaining.Count > MaxSpoolFiles) {
			int excess = remaining.Count - MaxSpoolFiles;
			foreach (string path in remaining.Take(excess)) {
				DeleteFile(path);
			}
			remaining.RemoveRange(0, excess);
		}
		return remaining;
	}

	private static DateTimeOffset? TryParseFileTimestamp(string fileName)
	{
		if (fileName is null || fileName.Length <= FileTimestampLength || fileName[FileTimestampLength] != 'Z') {
			return null;
		}
		return DateTimeOffset.TryParseExact(fileName[..FileTimestampLength], FileTimestampFormat,
			CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out DateTimeOffset parsed)
			? parsed
			: null;
	}

	private List<(string Path, OpenTelemetryLogEvent Event)> ReadBatch(IReadOnlyList<string> batchFiles)
	{
		List<(string, OpenTelemetryLogEvent)> batch = new(batchFiles.Count);
		foreach (string path in batchFiles) {
			OpenTelemetryLogEvent logEvent = TryReadEvent(path);
			if (logEvent is null) {
				// Poison-file guard: corrupt or empty events must not wedge the spool.
				DeleteFile(path);
				_logger.LogDebug("telemetry-flush dropped reason=unparseable-event file={File}",
					System.IO.Path.GetFileName(path));
				continue;
			}
			batch.Add((path, logEvent));
		}
		return batch;
	}

	private OpenTelemetryLogEvent TryReadEvent(string path)
	{
		try {
			string json = _fileSystem.File.ReadAllText(path, Encoding.UTF8);
			return string.IsNullOrWhiteSpace(json)
				? null
				: JsonSerializer.Deserialize<OpenTelemetryLogEvent>(json, ReadJsonOptions);
		} catch {
			return null;
		}
	}

	private static string BuildOtlpPayload(IReadOnlyList<OpenTelemetryLogEvent> events)
	{
		List<OtlpLogRecord> logRecords = events
			.Select(logEvent => new OtlpLogRecord(
				logEvent.TimeUnixNano.ToString(CultureInfo.InvariantCulture),
				logEvent.SeverityText,
				ToOtlpValue(logEvent.Body),
				logEvent.Attributes?.Select(attribute => new OtlpKeyValue(attribute.Key, ToOtlpValue(attribute.Value))).ToList()
				?? []))
			.ToList();
		OtlpExportLogsServiceRequest request = new([
			new OtlpResourceLogs(
				new OtlpResource([new OtlpKeyValue("service.name", new OtlpAnyValue(StringValue: ServiceName))]),
				[new OtlpScopeLogs(new OtlpScope(ServiceName), logRecords)])
		]);
		return JsonSerializer.Serialize(request, WireJsonOptions);
	}

	private static OtlpAnyValue ToOtlpValue(OpenTelemetryValue value) =>
		new(value?.StringValue, value?.IntValue?.ToString(CultureInfo.InvariantCulture));

	private async Task<PostOutcome> PostWithRetryAsync(HttpClient http, MeasurementFlushOptions options,
		string payload, CancellationToken cancellationToken)
	{
		for (int attempt = 1; attempt <= PostAttempts; attempt++) {
			PostOutcome outcome = await PostOnceAsync(http, options, payload, attempt, cancellationToken).ConfigureAwait(false);
			if (outcome != PostOutcome.TransientFailure || attempt == PostAttempts) {
				return outcome;
			}
			if (!await DelayBackoffAsync(attempt, cancellationToken).ConfigureAwait(false)) {
				return PostOutcome.TransientFailure;
			}
		}
		return PostOutcome.TransientFailure;
	}

	private async Task<PostOutcome> PostOnceAsync(HttpClient http, MeasurementFlushOptions options,
		string payload, int attempt, CancellationToken cancellationToken)
	{
		try {
			using HttpRequestMessage request = new(HttpMethod.Post, options.Endpoint);
			request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
			if (!string.IsNullOrWhiteSpace(options.IngestKey)) {
				request.Headers.TryAddWithoutValidation(IngestKeyHeaderName, options.IngestKey);
			}
			using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
			if (response.IsSuccessStatusCode) {
				return PostOutcome.Delivered;
			}
			if (IsTransientStatus(response.StatusCode)) {
				_logger.LogDebug("telemetry-flush retry status={Status} attempt={Attempt}",
					(int)response.StatusCode, attempt);
				return PostOutcome.TransientFailure;
			}
			_logger.LogDebug("telemetry-flush rejected status={Status}", (int)response.StatusCode);
			return PostOutcome.PermanentRejection;
		} catch (HttpRequestException ex) {
			_logger.LogDebug(ex, "telemetry-flush retry attempt={Attempt} error={Error}", attempt, ex.Message);
			return PostOutcome.TransientFailure;
		} catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
			_logger.LogDebug(ex, "telemetry-flush retry attempt={Attempt} reason=timeout", attempt);
			return PostOutcome.TransientFailure;
		}
	}

	private static bool IsTransientStatus(HttpStatusCode statusCode) =>
		(int)statusCode >= 500
		|| statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;

	private static async Task<bool> DelayBackoffAsync(int attempt, CancellationToken cancellationToken)
	{
		TimeSpan backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1))
			+ TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
		try {
			await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
			return true;
		} catch (TaskCanceledException) {
			return false;
		}
	}

	private void DeleteFiles(IEnumerable<string> paths)
	{
		foreach (string path in paths) {
			DeleteFile(path);
		}
	}

	private void DeleteFile(string path)
	{
		try {
			_fileSystem.File.Delete(path);
		} catch (Exception ex) {
			// Delete races (another clio process flushed first) and IO hiccups are non-fatal.
			_logger.LogDebug(ex, "telemetry-flush delete failed file={File} error={Error}",
				System.IO.Path.GetFileName(path), ex.Message);
		}
	}

	private enum PostOutcome
	{
		Delivered,
		TransientFailure,
		PermanentRejection
	}
}
