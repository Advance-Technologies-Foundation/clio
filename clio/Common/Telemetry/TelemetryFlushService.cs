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
public interface ITelemetryFlushService
{
	/// <summary>
	/// Prunes the local event spool (age and size caps), then uploads spooled events as
	/// OTLP/HTTP JSON batches when an endpoint is configured and telemetry consent is granted.
	/// Never throws to the caller.
	/// </summary>
	Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class TelemetryFlushService : ITelemetryFlushService
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
	private const string TempFileSuffix = ".tmp";
	private const string ServiceName = "clio";

	/// <summary>
	/// Grace period before a crash-orphaned writer temp file (<c>*.json.tmp</c>) is reaped. Well
	/// past any live write, so an in-progress temp belonging to a concurrent writer is never deleted.
	/// </summary>
	internal static readonly TimeSpan MaxTmpAge = TimeSpan.FromHours(24);

	/// <summary>
	/// Parse format for the event filename timestamp prefix written by
	/// <see cref="TelemetryService"/> (<c>yyyyMMddTHHmmssfffZ_{eventId}.json</c>);
	/// the trailing <c>Z</c> is a literal and is validated separately.
	/// </summary>
	private const string FileTimestampFormat = "yyyyMMddTHHmmssfff";
	private const int FileTimestampLength = 18;

	private static readonly JsonSerializerOptions ReadJsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};
	private static readonly JsonSerializerOptions WireJsonOptions = new() {
		// camelCase fallback so a future Otlp* member added without an explicit [JsonPropertyName]
		// still serializes as spec-valid OTLP/HTTP JSON (proto3 JSON requires lowerCamelCase keys).
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly Ms.IFileSystem _fileSystem;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly ITelemetryService _telemetryService;
	private readonly ITelemetryFlushOptionsProvider _optionsProvider;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<TelemetryFlushService> _logger;
	private readonly string _telemetryRoot;

	/// <summary>
	/// Seam for the inter-attempt backoff delay. Defaults to the real <see cref="Task.Delay(TimeSpan, CancellationToken)"/>;
	/// tests substitute a no-op so retry paths run deterministically without real wall-clock sleeps. Not a behavior
	/// change in production — the system clock provider has no virtual timer, so the delay cannot ride on it.
	/// </summary>
	internal Func<TimeSpan, CancellationToken, Task> DelayFactory = Task.Delay;

	/// <summary>
	/// Initializes a new instance of the <see cref="TelemetryFlushService"/> class.
	/// </summary>
	/// <param name="fileSystem">Filesystem abstraction used for all spool I/O.</param>
	/// <param name="httpClientFactory">Factory resolving the named telemetry upload client.</param>
	/// <param name="telemetryService">Source of the locally stored consent decision.</param>
	/// <param name="optionsProvider">Resolves endpoint and ingest-key configuration per run.</param>
	/// <param name="telemetryRoot">
	/// Optional local storage root. When omitted, the root is taken from the
	/// <c>CLIO_TELEMETRY_HOME</c> environment variable or the default user-profile location.
	/// </param>
	/// <param name="timeProvider">Optional time source for spool age pruning; defaults to system time.</param>
	/// <param name="logger">Optional diagnostics logger; silent when omitted.</param>
	public TelemetryFlushService(
		Ms.IFileSystem fileSystem,
		IHttpClientFactory httpClientFactory,
		ITelemetryService telemetryService,
		ITelemetryFlushOptionsProvider optionsProvider,
		string telemetryRoot = null,
		TimeProvider timeProvider = null,
		ILogger<TelemetryFlushService> logger = null)
	{
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		_telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
		_optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
		_timeProvider = timeProvider ?? TimeProvider.System;
		_logger = logger ?? NullLogger<TelemetryFlushService>.Instance;
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
		// Session-state files are pruned every run (independent of the event spool) so the
		// sessions directory cannot grow without bound on long-lived machines.
		PruneSessions();
		string eventsDirectory = TelemetryStoragePaths.EventsDirectory(_telemetryRoot);
		if (!_fileSystem.Directory.Exists(eventsDirectory)) {
			return;
		}
		PruneTmp(eventsDirectory);
		List<string> spool = ListSpool(eventsDirectory);
		spool = PruneSpool(spool);
		if (spool.Count == 0) {
			return;
		}
		TelemetryFlushOptions options = _optionsProvider.Resolve();
		if (!options.IsSendingEnabled) {
			_logger.LogDebug("telemetry-flush skipped reason=endpoint-not-configured spool={Count}", spool.Count);
			return;
		}
		if (_telemetryService.GetConsentStatus().TelemetryConsent != TelemetryService.ConsentGranted) {
			_logger.LogDebug("telemetry-flush skipped reason=consent-not-granted spool={Count}", spool.Count);
			return;
		}
		HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
		int offset = 0;
		while (offset < spool.Count && !cancellationToken.IsCancellationRequested) {
			// Re-check consent before each batch: a withdraw-telemetry-consent call during a long
			// flush must stop further uploads (it also purges the on-disk spool). Only the single
			// batch already read into memory and in flight at the moment of withdrawal may complete.
			if (_telemetryService.GetConsentStatus().TelemetryConsent != TelemetryService.ConsentGranted) {
				_logger.LogDebug("telemetry-flush stopped reason=consent-withdrawn remaining={Count}", spool.Count - offset);
				return;
			}
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
					// Loss-tolerant by design: a rejected batch must not wedge the spool. Logged at
					// Warning (not Debug) so a server-wide 4xx — e.g. a wire/schema regression that
					// would otherwise silently zero out telemetry — is detectable by operators.
					DeleteFiles(batch.Select(item => item.Path));
					_logger.LogWarning("telemetry-flush dropped reason=permanent-rejection events={Count}", batch.Count);
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
				SeverityNumberFor(logEvent.SeverityText),
				logEvent.SeverityText,
				logEvent.Attributes?.Select(attribute => new OtlpKeyValue(attribute.Key, ToOtlpValue(attribute.Value))).ToList()
				?? [],
				logEvent.EventName))
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

	private const int SeverityNumberInfo = 9;

	// OTLP severity_number paired with severity_text so backends that key on the numeric severity
	// classify the record as INFO. Serialized as a JSON number (32-bit enum), unlike the int64 fields.
	private static int? SeverityNumberFor(string severityText) =>
		string.Equals(severityText, "INFO", StringComparison.Ordinal) ? SeverityNumberInfo : null;

	private async Task<PostOutcome> PostWithRetryAsync(HttpClient http, TelemetryFlushOptions options,
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

	private async Task<PostOutcome> PostOnceAsync(HttpClient http, TelemetryFlushOptions options,
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
			string body = await ReadBodySnippetAsync(response, cancellationToken).ConfigureAwait(false);
			_logger.LogWarning("telemetry-flush rejected status={Status} body={Body}", (int)response.StatusCode, body);
			return PostOutcome.PermanentRejection;
		} catch (HttpRequestException ex) {
			_logger.LogDebug(ex, "telemetry-flush retry attempt={Attempt} error={Error}", attempt, ex.Message);
			return PostOutcome.TransientFailure;
		} catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) {
			_logger.LogDebug(ex, "telemetry-flush retry attempt={Attempt} reason=timeout", attempt);
			return PostOutcome.TransientFailure;
		}
	}

	private const int RejectionBodySnippetLength = 200;

	private static async Task<string> ReadBodySnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		try {
			string body = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))?.Trim()
				?? string.Empty;
			return body.Length <= RejectionBodySnippetLength ? body : body[..RejectionBodySnippetLength];
		} catch {
			// Diagnostics only — a body that cannot be read must never affect the flush outcome.
			return string.Empty;
		}
	}

	private void PruneSessions()
	{
		string sessionsDirectory = TelemetryStoragePaths.SessionsDirectory(_telemetryRoot);
		if (!_fileSystem.Directory.Exists(sessionsDirectory)) {
			return;
		}
		List<string> sessions;
		try {
			sessions = _fileSystem.Directory.GetFiles(sessionsDirectory)
				.Where(path => path.EndsWith(EventFileSuffix, StringComparison.Ordinal))
				.ToList();
		} catch (Exception ex) {
			_logger.LogDebug(ex, "telemetry-flush session-list failed error={Error}", ex.Message);
			return;
		}
		DateTimeOffset cutoff = _timeProvider.GetUtcNow().AddDays(-MaxSpoolAgeDays);
		List<string> live = new(sessions.Count);
		foreach (string path in sessions) {
			// Session-state files matter only for in-session duration inference, so stale or
			// unreadable ones are reclaimed to keep the directory bounded.
			if (SessionLastWriteUtc(path) is { } writtenAt && writtenAt >= cutoff) {
				live.Add(path);
			} else {
				DeleteFile(path);
			}
		}
		if (live.Count > MaxSpoolFiles) {
			foreach (string path in live.OrderBy(SessionLastWriteUtc).Take(live.Count - MaxSpoolFiles)) {
				DeleteFile(path);
			}
		}
	}

	private DateTimeOffset? SessionLastWriteUtc(string path)
	{
		try {
			return new DateTimeOffset(_fileSystem.File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
		} catch {
			return null;
		}
	}

	private void PruneTmp(string eventsDirectory)
	{
		List<string> tempFiles;
		try {
			tempFiles = _fileSystem.Directory.GetFiles(eventsDirectory)
				.Where(path => path.EndsWith(TempFileSuffix, StringComparison.Ordinal))
				.ToList();
		} catch (Exception ex) {
			_logger.LogDebug(ex, "telemetry-flush tmp-list failed error={Error}", ex.Message);
			return;
		}
		DateTimeOffset cutoff = _timeProvider.GetUtcNow().Subtract(MaxTmpAge);
		foreach (string path in tempFiles) {
			// Event temp files carry the same timestamp prefix as their target; reap only those
			// clearly past any live write (or with an unparseable prefix), leaving fresh temps alone.
			DateTimeOffset? writtenAt = TryParseFileTimestamp(System.IO.Path.GetFileName(path));
			if (writtenAt is null || writtenAt < cutoff) {
				DeleteFile(path);
			}
		}
	}

	private static bool IsTransientStatus(HttpStatusCode statusCode) =>
		(int)statusCode >= 500
		|| statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;

	private async Task<bool> DelayBackoffAsync(int attempt, CancellationToken cancellationToken)
	{
		TimeSpan backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1))
			+ TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
		try {
			await DelayFactory(backoff, cancellationToken).ConfigureAwait(false);
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
