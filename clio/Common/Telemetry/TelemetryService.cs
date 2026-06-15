using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ms = System.IO.Abstractions;

namespace Clio.Common.Telemetry;

/// <summary>
/// Stores product telemetry events as local OpenTelemetry-shaped JSON files.
/// </summary>
public interface ITelemetryService
{
	/// <summary>
	/// Validates and persists a product telemetry event locally.
	/// </summary>
	TelemetryEventResult Send(TelemetryEventRequest request);

	/// <summary>
	/// Reads the locally persisted telemetry consent decision without writing analytics.
	/// </summary>
	TelemetryConsentResult GetConsentStatus();
}

/// <inheritdoc />
public sealed class TelemetryService : ITelemetryService
{
	internal const string ConsentGranted = "granted";

	/// <summary>
	/// Result status returned when an event has been persisted to the local spool.
	/// </summary>
	internal const string StatusStored = "stored";

	private const string ConsentDenied = "denied";
	private const string Unknown = "unknown";
	private const string SessionStartedEvent = "session_started";

	/// <summary>
	/// Version of the persisted event payload shape. Bump when attributes are added or renamed
	/// so downstream consumers can parse events without relying on their creation date.
	/// </summary>
	private const string SchemaVersion = "1";

	private static readonly object SyncRoot = new();
	private static readonly JsonSerializerOptions JsonOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};
	private readonly Ms.IFileSystem _fileSystem;
	private readonly TimeProvider _timeProvider;
	private readonly string _telemetryRoot;
	private readonly ILogger<TelemetryService> _logger;

	private static readonly HashSet<string> AllowedEventNames = new(StringComparer.Ordinal) {
		SessionStartedEvent,
		"pre_plan_clarification_requested",
		"pre_plan_user_input_received",
		"business_plan_generated",
		"business_plan_generation_skipped",
		"business_plan_feedback_received",
		"business_plan_regenerated",
		"business_plan_approved",
		"implementation_started",
		"implementation_completed",
		"implementation_failed",
		"implementation_user_input_received",
		"implementation_changes_requested",
		"implementation_changes_applied"
	};

	private static readonly HashSet<string> AllowedConsents = new(StringComparer.Ordinal) {
		ConsentGranted, ConsentDenied
	};

	/// <summary>
	/// Initializes a new instance of the <see cref="TelemetryService"/> class.
	/// </summary>
	/// <param name="fileSystem">Filesystem abstraction used for all local telemetry I/O.</param>
	/// <param name="telemetryRoot">
	/// Optional local storage root. When omitted, the root is taken from the
	/// <c>CLIO_TELEMETRY_HOME</c> environment variable or the default user-profile location.
	/// </param>
	/// <param name="timeProvider">
	/// Optional time source for event timestamps and duration inference. Defaults to
	/// <see cref="TimeProvider.System"/>; tests can supply a controllable provider.
	/// </param>
	/// <param name="logger">Optional diagnostics logger; silent when omitted.</param>
	public TelemetryService(Ms.IFileSystem fileSystem, string telemetryRoot = null, TimeProvider timeProvider = null,
		ILogger<TelemetryService> logger = null)
	{
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_timeProvider = timeProvider ?? TimeProvider.System;
		_telemetryRoot = string.IsNullOrWhiteSpace(telemetryRoot)
			? DefaultTelemetryRoot
			: telemetryRoot;
		_logger = logger ?? NullLogger<TelemetryService>.Instance;
	}

	/// <inheritdoc />
	public TelemetryConsentResult GetConsentStatus()
	{
		ConsentState consentState = ReadConsent();
		return consentState.TelemetryConsent switch {
			ConsentGranted => new TelemetryConsentResult(true, "known", ConsentGranted),
			ConsentDenied => new TelemetryConsentResult(true, "known", ConsentDenied),
			_ => new TelemetryConsentResult(true, Unknown, Unknown)
		};
	}

	/// <inheritdoc />
	public TelemetryEventResult Send(TelemetryEventRequest request)
	{
		if (request is null) {
			return Invalid("invalid-request", "Telemetry request is required.");
		}
		TelemetryEventResult validation = ValidateRequest(request);
		if (!validation.Success) {
			return validation;
		}
		lock (SyncRoot) {
			try {
				EnsureDirectories();
				ConsentState consentState = ResolveConsent(request.TelemetryConsent);
				if (consentState.TelemetryConsent == ConsentDenied) {
					return new TelemetryEventResult(true, "consent-denied");
				}
				if (consentState.TelemetryConsent != ConsentGranted) {
					return Invalid("telemetry-consent-required",
						"Telemetry consent is required before telemetry events can be stored. Ask the user and retry with telemetry_consent set to granted or denied.");
				}

				string eventId = Guid.NewGuid().ToString("N");
				DateTimeOffset eventTimestamp = _timeProvider.GetUtcNow();
				TelemetrySessionState sessionState = ReadSessionState(request.SessionId);
				long? inferredDurationMs = request.DurationMs ?? InferDurationMs(sessionState, request.EventName, eventTimestamp);
				TelemetryEventRequest enrichedRequest = request with { DurationMs = inferredDurationMs };
				long? durationSinceSessionStartMs = InferDurationSinceSessionStartMs(sessionState, request.EventName, eventTimestamp);
				OpenTelemetryLogEvent logEvent = BuildLogEvent(enrichedRequest, eventId, eventTimestamp, durationSinceSessionStartMs);
				WriteEvent(eventId, logEvent);
				UpdateSessionState(sessionState, request.EventName, eventTimestamp);
				return new TelemetryEventResult(true, StatusStored, eventId);
			} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
				// Telemetry must never disturb the caller: a local storage I/O failure is reported as a
				// soft result, never thrown into the MCP tool call. Mirrors the flusher's contract.
				_logger.LogDebug(ex, "telemetry-store failed error={Error}", ex.Message);
				return new TelemetryEventResult(false, "store-failed",
					Error: new TelemetryError("storage-unavailable",
						"Local telemetry storage is unavailable; the event was not recorded."));
			}
		}
	}

	private static TelemetryEventResult ValidateRequest(TelemetryEventRequest request)
	{
		if (request.ExtensionData is { Count: > 0 }) {
			string invalidFields = string.Join(", ", request.ExtensionData.Keys.OrderBy(key => key, StringComparer.Ordinal));
			return Invalid("unsupported-fields", $"Unsupported telemetry fields: {invalidFields}.");
		}
		foreach ((string name, string value) in RequiredFields(request)) {
			if (string.IsNullOrWhiteSpace(value)) {
				return Invalid("missing-required-field", $"Telemetry field '{name}' is required.");
			}
		}
		if (!AllowedEventNames.Contains(request.EventName)) {
			return Invalid("unknown-event-name", $"Unsupported event_name '{request.EventName}'.");
		}
		if (!string.IsNullOrWhiteSpace(request.TelemetryConsent) && !AllowedConsents.Contains(request.TelemetryConsent)) {
			return Invalid("unknown-consent", $"Unsupported telemetry_consent '{request.TelemetryConsent}'.");
		}
		return new TelemetryEventResult(true, "valid");
	}

	private static IReadOnlyList<(string name, string value)> RequiredFields(TelemetryEventRequest request) =>
	[
		("session_id", request.SessionId),
		("event_name", request.EventName),
		("coding_agent", request.CodingAgent),
		("skill_version", request.SkillVersion),
		("plugin_version", request.PluginVersion)
	];

	private static TelemetryEventResult Invalid(string code, string message) =>
		new(false, "rejected", Error: new TelemetryError(code, message));

	private ConsentState ResolveConsent(string explicitConsent)
	{
		ConsentState current = ReadConsent();
		if (string.IsNullOrWhiteSpace(explicitConsent)
			|| current.TelemetryConsent is ConsentGranted or ConsentDenied) {
			return current;
		}
		ConsentState updated = new(explicitConsent, _timeProvider.GetUtcNow());
		WriteJson(ConsentPath, updated);
		return updated;
	}

	private ConsentState ReadConsent()
	{
		if (!_fileSystem.File.Exists(ConsentPath)) {
			return new ConsentState(Unknown, DateTimeOffset.MinValue);
		}
		try {
			return JsonSerializer.Deserialize<ConsentState>(_fileSystem.File.ReadAllText(ConsentPath, Encoding.UTF8), JsonOptions)
				?? new ConsentState("unknown", DateTimeOffset.MinValue);
		} catch {
			return new ConsentState(Unknown, DateTimeOffset.MinValue);
		}
	}

	private OpenTelemetryLogEvent BuildLogEvent(TelemetryEventRequest request, string eventId, DateTimeOffset timestamp,
		long? durationSinceSessionStartMs)
	{
		List<OpenTelemetryAttribute> attributes = [
			StringAttribute("schema_version", SchemaVersion),
			StringAttribute("session_id", request.SessionId),
			StringAttribute("event_name", request.EventName),
			StringAttribute("event_timestamp", timestamp.ToString("O")),
			StringAttribute("platform", GetPlatform()),
			StringAttribute("clio_version", GetClioVersion()),
			StringAttribute("coding_agent", request.CodingAgent),
			StringAttribute("installation_id", GetOrCreateInstallationId()),
			StringAttribute("skill_version", request.SkillVersion),
			StringAttribute("plugin_version", request.PluginVersion),
			StringAttribute("event_id", eventId)
		];
		if (request.DurationMs.HasValue) {
			attributes.Add(new OpenTelemetryAttribute("duration_ms", new OpenTelemetryValue(IntValue: request.DurationMs.Value)));
		}
		if (durationSinceSessionStartMs.HasValue) {
			attributes.Add(new OpenTelemetryAttribute("duration_since_session_start_ms",
				new OpenTelemetryValue(IntValue: durationSinceSessionStartMs.Value)));
		}
		return new OpenTelemetryLogEvent(
			timestamp.ToUnixTimeMilliseconds() * 1_000_000,
			"INFO",
			new OpenTelemetryValue(StringValue: request.EventName),
			attributes);
	}

	private TelemetrySessionState ReadSessionState(string sessionId)
	{
		string path = SessionStatePath(sessionId);
		if (!_fileSystem.File.Exists(path)) {
			return new TelemetrySessionState(sessionId, new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal));
		}
		try {
			return JsonSerializer.Deserialize<TelemetrySessionState>(_fileSystem.File.ReadAllText(path, Encoding.UTF8), JsonOptions)
				?? new TelemetrySessionState(sessionId, new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal));
		} catch {
			return new TelemetrySessionState(sessionId, new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal));
		}
	}

	private static long? InferDurationMs(TelemetrySessionState sessionState, string eventName, DateTimeOffset timestamp)
	{
		string startEventName = eventName switch {
			"business_plan_generated" => SessionStartedEvent,
			"business_plan_approved" => FirstKnown(sessionState, "business_plan_generated"),
			"implementation_completed" => "implementation_started",
			"implementation_failed" => "implementation_started",
			"implementation_changes_applied" => "implementation_changes_requested",
			_ => null
		};
		if (string.IsNullOrWhiteSpace(startEventName)
			|| !sessionState.Events.TryGetValue(startEventName, out DateTimeOffset startedAt)) {
			return null;
		}
		return Math.Max(0, (long)(timestamp - startedAt).TotalMilliseconds);
	}

	private static long? InferDurationSinceSessionStartMs(TelemetrySessionState sessionState, string eventName, DateTimeOffset timestamp)
	{
		if (eventName == SessionStartedEvent
			|| !sessionState.Events.TryGetValue(SessionStartedEvent, out DateTimeOffset sessionStartedAt)) {
			return null;
		}
		return Math.Max(0, (long)(timestamp - sessionStartedAt).TotalMilliseconds);
	}

	private static string FirstKnown(TelemetrySessionState sessionState, params string[] eventNames)
	{
		return eventNames
			.Where(eventName => sessionState.Events.ContainsKey(eventName))
			.OrderBy(eventName => sessionState.Events[eventName])
			.FirstOrDefault();
	}

	private void UpdateSessionState(TelemetrySessionState sessionState, string eventName, DateTimeOffset timestamp)
	{
		sessionState.Events[eventName] = timestamp;
		WriteJson(SessionStatePath(sessionState.SessionId), sessionState);
	}

	private static OpenTelemetryAttribute StringAttribute(string key, string value) =>
		new(key, new OpenTelemetryValue(StringValue: value));

	private void WriteEvent(string eventId, OpenTelemetryLogEvent logEvent)
	{
		string fileName = $"{_timeProvider.GetUtcNow():yyyyMMddTHHmmssfffZ}_{eventId}.json";
		string tempPath = Path.Combine(EventsDirectory, fileName + ".tmp");
		string finalPath = Path.Combine(EventsDirectory, fileName);
		WriteJson(tempPath, logEvent);
		_fileSystem.File.Move(tempPath, finalPath);
	}

	private void EnsureDirectories()
	{
		_fileSystem.Directory.CreateDirectory(TelemetryRoot);
		_fileSystem.Directory.CreateDirectory(EventsDirectory);
		_fileSystem.Directory.CreateDirectory(SessionsDirectory);
	}

	private void WriteJson<T>(string path, T value)
	{
		_fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		string json = JsonSerializer.Serialize(value, JsonOptions);
		_fileSystem.File.WriteAllText(path, json, Encoding.UTF8);
	}

	private string GetOrCreateInstallationId()
	{
		if (_fileSystem.File.Exists(InstallationIdPath)) {
			string current = _fileSystem.File.ReadAllText(InstallationIdPath, Encoding.UTF8).Trim();
			if (!string.IsNullOrWhiteSpace(current)) {
				return current;
			}
		}
		string installationId = Guid.NewGuid().ToString("N");
		_fileSystem.File.WriteAllText(InstallationIdPath, installationId, Encoding.UTF8);
		return installationId;
	}

	private static string GetClioVersion() =>
		Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
		?? typeof(TelemetryService).Assembly.GetName().Version?.ToString()
		?? Unknown;

	private static string GetPlatform()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			return "windows";
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			return "macos";
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
			return "linux";
		}
		return Unknown;
	}

	private static string DefaultTelemetryRoot => TelemetryStoragePaths.ResolveRoot();

	private string TelemetryRoot => _telemetryRoot;
	private string ConsentPath => Path.Combine(TelemetryRoot, "consent.json");
	private string InstallationIdPath => Path.Combine(TelemetryRoot, "installation-id.txt");
	private string SessionsDirectory => TelemetryStoragePaths.SessionsDirectory(TelemetryRoot);
	private string SessionStatePath(string sessionId) =>
		Path.Combine(SessionsDirectory, $"{SafeFileName(sessionId)}.json");

	private static string SafeFileName(string value) =>
		new(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_')
			.ToArray());

	private string EventsDirectory => TelemetryStoragePaths.EventsDirectory(TelemetryRoot);
}
