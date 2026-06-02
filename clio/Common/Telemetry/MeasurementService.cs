using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ms = System.IO.Abstractions;

namespace Clio.Common.Telemetry;

/// <summary>
/// Stores product measurement events as local OpenTelemetry-shaped JSON files.
/// </summary>
public interface IMeasurementService
{
	/// <summary>
	/// Validates and persists a product measurement event locally.
	/// </summary>
	MeasurementResult Send(MeasurementRequest request);

	/// <summary>
	/// Reads the locally persisted telemetry consent decision without writing analytics.
	/// </summary>
	MeasurementConsentResult GetConsentStatus();
}

/// <inheritdoc />
public sealed class MeasurementService : IMeasurementService
{
	private const string ConsentGranted = "granted";
	private const string ConsentDenied = "denied";

	/// <summary>
	/// Version of the persisted event payload shape. Bump when attributes are added or renamed
	/// so downstream consumers can parse events without relying on their creation date.
	/// </summary>
	private const string SchemaVersion = "1";

	/// <summary>
	/// Environment variable that redirects the local telemetry storage root (used by tests).
	/// </summary>
	private const string TelemetryHomeEnvironmentVariable = "CLIO_TELEMETRY_HOME";

	private static readonly object SyncRoot = new();
	private static readonly JsonSerializerOptions JsonOptions = new() {
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};
	private readonly Ms.IFileSystem _fileSystem;
	private readonly string _telemetryRoot;

	private static readonly HashSet<string> AllowedEventNames = new(StringComparer.Ordinal) {
		"session_started",
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
	/// Initializes a new instance of the <see cref="MeasurementService"/> class.
	/// </summary>
	/// <param name="fileSystem">Filesystem abstraction used for all local telemetry I/O.</param>
	/// <param name="telemetryRoot">
	/// Optional local storage root. When omitted, the root is taken from the
	/// <c>CLIO_TELEMETRY_HOME</c> environment variable or the default user-profile location.
	/// </param>
	public MeasurementService(Ms.IFileSystem fileSystem, string telemetryRoot = null)
	{
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_telemetryRoot = string.IsNullOrWhiteSpace(telemetryRoot)
			? DefaultTelemetryRoot
			: telemetryRoot;
	}

	/// <inheritdoc />
	public MeasurementConsentResult GetConsentStatus()
	{
		ConsentState consentState = ReadConsent();
		return consentState.TelemetryConsent switch {
			ConsentGranted => new MeasurementConsentResult(true, "known", ConsentGranted),
			ConsentDenied => new MeasurementConsentResult(true, "known", ConsentDenied),
			_ => new MeasurementConsentResult(true, "unknown", "unknown")
		};
	}

	/// <inheritdoc />
	public MeasurementResult Send(MeasurementRequest request)
	{
		if (request is null) {
			return Invalid("invalid-request", "Measurement request is required.");
		}
		MeasurementResult validation = ValidateRequest(request);
		if (!validation.Success) {
			return validation;
		}
		lock (SyncRoot) {
			EnsureDirectories();
			ConsentState consentState = ResolveConsent(request.TelemetryConsent);
			if (consentState.TelemetryConsent == ConsentDenied) {
				return new MeasurementResult(true, "consent-denied");
			}
			if (consentState.TelemetryConsent != ConsentGranted) {
				return Invalid("telemetry-consent-required",
					"Telemetry consent is required before measurements can be stored. Ask the user and retry with telemetry_consent set to granted or denied.");
			}

			string eventId = Guid.NewGuid().ToString("N");
			DateTimeOffset eventTimestamp = DateTimeOffset.UtcNow;
			MeasurementSessionState sessionState = ReadSessionState(request.SessionId);
			long? inferredDurationMs = request.DurationMs ?? InferDurationMs(sessionState, request.EventName, eventTimestamp);
			MeasurementRequest enrichedRequest = request with { DurationMs = inferredDurationMs };
			long? durationSinceSessionStartMs = InferDurationSinceSessionStartMs(sessionState, request.EventName, eventTimestamp);
			OpenTelemetryLogEvent logEvent = BuildLogEvent(enrichedRequest, eventId, eventTimestamp, durationSinceSessionStartMs);
			WriteEvent(eventId, logEvent);
			UpdateSessionState(sessionState, request.EventName, eventTimestamp);
			return new MeasurementResult(true, "stored", eventId);
		}
	}

	private static MeasurementResult ValidateRequest(MeasurementRequest request)
	{
		if (request.ExtensionData is { Count: > 0 }) {
			string invalidFields = string.Join(", ", request.ExtensionData.Keys.OrderBy(key => key, StringComparer.Ordinal));
			return Invalid("unsupported-fields", $"Unsupported measurement fields: {invalidFields}.");
		}
		foreach ((string name, string value) in RequiredFields(request)) {
			if (string.IsNullOrWhiteSpace(value)) {
				return Invalid("missing-required-field", $"Measurement field '{name}' is required.");
			}
		}
		if (!AllowedEventNames.Contains(request.EventName)) {
			return Invalid("unknown-event-name", $"Unsupported event_name '{request.EventName}'.");
		}
		if (!string.IsNullOrWhiteSpace(request.TelemetryConsent) && !AllowedConsents.Contains(request.TelemetryConsent)) {
			return Invalid("unknown-consent", $"Unsupported telemetry_consent '{request.TelemetryConsent}'.");
		}
		return new MeasurementResult(true, "valid");
	}

	private static IReadOnlyList<(string name, string value)> RequiredFields(MeasurementRequest request) =>
	[
		("session_id", request.SessionId),
		("event_name", request.EventName),
		("coding_agent", request.CodingAgent),
		("skill_version", request.SkillVersion),
		("plugin_version", request.PluginVersion)
	];

	private static MeasurementResult Invalid(string code, string message) =>
		new(false, "rejected", Error: new MeasurementError(code, message));

	private ConsentState ResolveConsent(string explicitConsent)
	{
		ConsentState current = ReadConsent();
		if (string.IsNullOrWhiteSpace(explicitConsent)
			|| current.TelemetryConsent is ConsentGranted or ConsentDenied) {
			return current;
		}
		ConsentState updated = new(explicitConsent, DateTimeOffset.UtcNow);
		WriteJson(ConsentPath, updated);
		return updated;
	}

	private ConsentState ReadConsent()
	{
		if (!_fileSystem.File.Exists(ConsentPath)) {
			return new ConsentState("unknown", DateTimeOffset.MinValue);
		}
		try {
			return JsonSerializer.Deserialize<ConsentState>(_fileSystem.File.ReadAllText(ConsentPath, Encoding.UTF8), JsonOptions)
				?? new ConsentState("unknown", DateTimeOffset.MinValue);
		} catch {
			return new ConsentState("unknown", DateTimeOffset.MinValue);
		}
	}

	private OpenTelemetryLogEvent BuildLogEvent(MeasurementRequest request, string eventId, DateTimeOffset timestamp,
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

	private MeasurementSessionState ReadSessionState(string sessionId)
	{
		string path = SessionStatePath(sessionId);
		if (!_fileSystem.File.Exists(path)) {
			return new MeasurementSessionState(sessionId, new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal));
		}
		try {
			return JsonSerializer.Deserialize<MeasurementSessionState>(_fileSystem.File.ReadAllText(path, Encoding.UTF8), JsonOptions)
				?? new MeasurementSessionState(sessionId, new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal));
		} catch {
			return new MeasurementSessionState(sessionId, new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal));
		}
	}

	private static long? InferDurationMs(MeasurementSessionState sessionState, string eventName, DateTimeOffset timestamp)
	{
		string startEventName = eventName switch {
			"business_plan_generated" => "session_started",
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

	private static long? InferDurationSinceSessionStartMs(MeasurementSessionState sessionState, string eventName, DateTimeOffset timestamp)
	{
		if (eventName == "session_started"
			|| !sessionState.Events.TryGetValue("session_started", out DateTimeOffset sessionStartedAt)) {
			return null;
		}
		return Math.Max(0, (long)(timestamp - sessionStartedAt).TotalMilliseconds);
	}

	private static string FirstKnown(MeasurementSessionState sessionState, params string[] eventNames)
	{
		return eventNames
			.Where(eventName => sessionState.Events.ContainsKey(eventName))
			.OrderBy(eventName => sessionState.Events[eventName])
			.FirstOrDefault();
	}

	private void UpdateSessionState(MeasurementSessionState sessionState, string eventName, DateTimeOffset timestamp)
	{
		sessionState.Events[eventName] = timestamp;
		WriteJson(SessionStatePath(sessionState.SessionId), sessionState);
	}

	private static OpenTelemetryAttribute StringAttribute(string key, string value) =>
		new(key, new OpenTelemetryValue(StringValue: value));

	private string WriteEvent(string eventId, OpenTelemetryLogEvent logEvent)
	{
		string fileName = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}_{eventId}.json";
		string tempPath = Path.Combine(EventsDirectory, fileName + ".tmp");
		string finalPath = Path.Combine(EventsDirectory, fileName);
		WriteJson(tempPath, logEvent);
		_fileSystem.File.Move(tempPath, finalPath);
		return finalPath;
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
		?? typeof(MeasurementService).Assembly.GetName().Version?.ToString()
		?? "unknown";

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
		return "unknown";
	}

	private static string DefaultTelemetryRoot
	{
		get {
			string overridePath = Environment.GetEnvironmentVariable(TelemetryHomeEnvironmentVariable);
			return string.IsNullOrWhiteSpace(overridePath)
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
					".creatio-ai-app-development-toolkit", "telemetry")
				: overridePath;
		}
	}

	private string TelemetryRoot => _telemetryRoot;
	private string ConsentPath => Path.Combine(TelemetryRoot, "consent.json");
	private string InstallationIdPath => Path.Combine(TelemetryRoot, "installation-id.txt");
	private string SessionsDirectory => Path.Combine(TelemetryRoot, "sessions");
	private string SessionStatePath(string sessionId) =>
		Path.Combine(SessionsDirectory, $"{SafeFileName(sessionId)}.json");

	private static string SafeFileName(string value) =>
		new(value.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_')
			.ToArray());

	private string EventsDirectory => Path.Combine(TelemetryRoot, "events");
}
