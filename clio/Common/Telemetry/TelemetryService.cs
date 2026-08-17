using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

	/// <summary>
	/// Withdraws telemetry consent: persists a denied decision and purges any not-yet-uploaded local
	/// events, so collection and upload both stop. Forward-looking (does not delete already-uploaded
	/// events) and safe to call from any prior state (granted, denied, or unknown).
	/// </summary>
	TelemetryConsentWithdrawalResult WithdrawConsent();
}

/// <inheritdoc />
public sealed class TelemetryService : ITelemetryService
{
	internal const string ConsentGranted = "granted";

	/// <summary>
	/// Result status returned when clio has recorded the event. The caller is done; any upload to a
	/// collector happens separately and is not confirmed by this result. Deliberately describes the
	/// contract outcome, not the mechanism, so the buffering/sending strategy can change freely.
	/// </summary>
	internal const string StatusRecorded = "recorded";

	private const string ConsentDenied = "denied";
	private const string Unknown = "unknown";
	private const string SessionStartedEvent = "session_started";

	/// <summary>Canonical session-start event; anchors every elapsed-time measurement for a run.</summary>
	internal const string WorkflowStartedEvent = "workflow_started";

	/// <summary>Developer approval of the presented plan; the span a build is measured from.</summary>
	private const string PlanApprovedEvent = "plan_approved";

	/// <summary>Start of execution; the narrowest span a terminal stage prefers to report.</summary>
	private const string BuildStartedEvent = "build_started";

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

	/// <summary>
	/// Canonical, ordered set of product workflow event names accepted by the telemetry tool.
	/// Single source of truth: both the runtime allow-list (<see cref="AllowedEventNameSet"/>) and
	/// the <c>get-tool-contract</c> <c>event_name</c> description are derived from this list, so the
	/// announced contract can never drift from what clio actually enforces. Kept in the same order
	/// as the CAADT telemetry contract (<c>context/product-telemetry.md</c>).
	/// </summary>
	internal static readonly IReadOnlyList<string> AllowedEventNames = [
		// --- Legacy app-creation names (DEPRECATED, still accepted) ---
		// Superseded by the flow-agnostic stages below, which carry the flow in the `workflow` field.
		// Kept accepted because clio and the toolkit release independently: an older installed toolkit
		// still emits these, and rejecting them would silently zero out its telemetry on a clio update.
		// New contracts must use the stage vocabulary.
		SessionStartedEvent,
		"pre_plan_clarification_requested",
		"pre_plan_user_input_received",
		"business_plan_generated",
		"business_plan_generation_skipped",
		"business_plan_feedback_received",
		"business_plan_regenerated",
		"business_plan_approved",
		"implementation_started",
		"implementation_user_input_received",
		"implementation_completed",
		"implementation_changes_requested",
		"implementation_changes_applied",
		"implementation_failed",

		// --- Flow-agnostic stage vocabulary (canonical) ---
		// WHICH flow a run belongs to travels in the `workflow` FIELD, not in the event name. The
		// alternative — a name per flow per stage (migration_plan_approved, branding_approved, ...) — encodes
		// a dimension into the enum: it multiplies names by flows, forces a clio release for every new skill,
		// and turns "what is our plan-approval rate" into a UNION over a hand-maintained list instead of one
		// GROUP BY workflow. These stages are deliberately generic so flows stay comparable.
		WorkflowStartedEvent,
		"clarification_requested",
		"user_input_received",
		"plan_presented",
		"plan_skipped",
		"plan_blocked",
		"plan_changes_requested",
		PlanApprovedEvent,
		BuildStartedEvent,
		"work_item_completed",
		"workflow_completed",
		"workflow_failed",
		"changes_requested",
		"changes_applied",

		// --- Session measurement (NOT a funnel stage) ---
		// Reports what a host session consumed. It is deliberately not a stage: it marks no progress
		// through a run, belongs to the session rather than to any one flow, and must never be counted
		// in a funnel.
		//
		// It exists because per-stage token attribution turned out to be unachievable, which only a
		// measurement showed: across 52 agent-emitted events, ZERO carried a token counter. An agent
		// cannot see its own running totals — nothing in the tool surface exposes them — so the guide's
		// promise that "the differences show which stage of which flow cost the tokens" could not be
		// kept. The host's own session transcript does have the numbers, and a hook can read it at the
		// end of a session, which is the one place a true total exists.
		SessionUsageEvent
	];

	/// <summary>
	/// Session-scoped consumption measurement, emitted once per host session. Not a funnel stage: it
	/// anchors nothing, terminates nothing, and is never counted as progress through a run.
	/// </summary>
	internal const string SessionUsageEvent = "session_usage";

	private static readonly HashSet<string> AllowedEventNameSet = new(AllowedEventNames, StringComparer.Ordinal);

	/// <summary>Maximum accepted length for the session identifier.</summary>
	private const int MaxSessionIdLength = 128;

	/// <summary>Maximum accepted length for short scalar metadata fields (agent/version strings).</summary>
	private const int MaxFieldLength = 64;

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
	public TelemetryConsentWithdrawalResult WithdrawConsent()
	{
		lock (SyncRoot) {
			try {
				// Record the opt-out first. The flusher re-checks consent on every run, so once this is
				// denied no spooled event can ever upload — even if the purge below is interrupted.
				WriteJson(ConsentPath, new ConsentState(ConsentDenied, _timeProvider.GetUtcNow()));
			} catch (Exception ex) {
				// The consent flip is the load-bearing step: if it fails, telemetry is NOT withdrawn, so
				// report a soft failure (never thrown into the MCP call) instead of a false success.
				_logger.LogDebug(ex, "telemetry-withdraw failed error={Error}", ex.Message);
				return new TelemetryConsentWithdrawalResult(false, "withdraw-failed", ReadConsent().TelemetryConsent, 0);
			}
			// Best-effort cleanup of the not-yet-uploaded outbox and per-session timers, so opting out also
			// discards locally buffered events instead of leaving them to age out over the spool's lifetime.
			// The installation id is intentionally kept (anonymous, and reused if consent is ever re-granted).
			int eventsPurged = PurgeEvents();
			PurgeFiles(SessionsDirectory);
			return new TelemetryConsentWithdrawalResult(true, "withdrawn", ConsentDenied, eventsPurged);
		}
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
				TelemetrySessionState sessionState =
					StartOfANewRun(ReadSessionState(request.SessionId, request.Workflow), request);
				long? inferredDurationMs = request.DurationMs ?? InferDurationMs(sessionState, request.EventName, eventTimestamp);
				TelemetryEventRequest enrichedRequest = request with { DurationMs = inferredDurationMs };
				long? durationSinceSessionStartMs = InferDurationSinceSessionStartMs(sessionState, request.EventName, eventTimestamp);
				OpenTelemetryLogEvent logEvent = BuildLogEvent(enrichedRequest, eventId, eventTimestamp, durationSinceSessionStartMs);
				WriteEvent(eventId, logEvent);
				UpdateSessionState(sessionState, request.EventName, eventTimestamp);
				return new TelemetryEventResult(true, StatusRecorded, eventId);
			} catch (Exception ex) {
				// Telemetry must never disturb the caller: any failure (I/O, serialization, etc.) is
				// reported as a soft result, never thrown into the MCP tool call. Mirrors the flusher's
				// broad catch and the ADR decision that the store never throws into the MCP call.
				_logger.LogDebug(ex, "telemetry-record failed error={Error}", ex.Message);
				return new TelemetryEventResult(false, "record-failed",
					Error: new TelemetryError("record-unavailable",
						"clio could not record the telemetry event; it was not retained."));
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
		TelemetryEventResult shapeResult = ValidateFieldShapes(request);
		if (!shapeResult.Success) {
			return shapeResult;
		}
		if (!AllowedEventNameSet.Contains(request.EventName)) {
			return Invalid("unknown-event-name", $"Unsupported event_name '{request.EventName}'.");
		}
		if (!string.IsNullOrWhiteSpace(request.TelemetryConsent) && !AllowedConsents.Contains(request.TelemetryConsent)) {
			return Invalid("unknown-consent", $"Unsupported telemetry_consent '{request.TelemetryConsent}'.");
		}
		// A client-supplied duration must be non-negative, matching the non-negative clamp the
		// inferred path applies (InferDurationMs uses Math.Max(0, ...)). Without this guard a
		// negative value supplied by a buggy/hostile client would be stored verbatim, since the
		// inference clamp is skipped whenever DurationMs is provided.
		if (request.DurationMs.HasValue && request.DurationMs.Value < 0) {
			return Invalid("invalid-duration", "duration_ms must be a non-negative value when supplied.");
		}
		// Token counters are a running total for the session, so they only ever grow; a negative value
		// is a client bug and storing it would poison any sum or max taken over the session.
		foreach ((string name, long? value) in TokenCounterFields(request)) {
			if (value.HasValue && value.Value < 0) {
				return Invalid("invalid-token-count", $"{name} must be a non-negative value when supplied.");
			}
		}
		return new TelemetryEventResult(true, "valid");
	}

	// Value-level guards (defense in depth): the agent-supplied free strings are bounded and the
	// session id is shape-checked so a buggy or hostile client cannot smuggle oversized blobs or
	// PII-shaped content past the field-name allow-list, and the values stay safe to embed as
	// attributes and to derive a session file name from.
	private static TelemetryEventResult ValidateFieldShapes(TelemetryEventRequest request)
	{
		if (request.SessionId.Length > MaxSessionIdLength || !IsAllowedSessionId(request.SessionId)) {
			return Invalid("invalid-session-id",
				$"session_id must be 1-{MaxSessionIdLength} characters of letters, digits, '.', '_', ':' or '-'.");
		}
		foreach ((string name, string value) in BoundedFields(request)) {
			if (value.Length > MaxFieldLength) {
				return Invalid("field-too-long", $"Telemetry field '{name}' exceeds {MaxFieldLength} characters.");
			}
		}
		foreach ((string name, string value) in OptionalTokenFields(request)) {
			if (value is not null && !IsAllowedToken(value)) {
				return Invalid("invalid-token",
					$"Telemetry field '{name}' must be 1-{MaxFieldLength} characters of lowercase letters, digits, '.', '_' or '-'.");
			}
		}
		return new TelemetryEventResult(true, "valid");
	}

	// workflow/variant are shape-checked rather than matched against a fixed list of known values. A
	// closed list would need a clio release every time a skill is added — the exact coupling the
	// dimension-in-a-field design exists to remove — while a bounded lowercase token is already too
	// narrow to carry a prompt, a path, or a customer name past the field allow-list.
	private static IReadOnlyList<(string name, string value)> OptionalTokenFields(TelemetryEventRequest request) =>
	[
		("workflow", request.Workflow),
		("variant", request.Variant),
		// The model identifier answers "which model produced this run", which is the first thing asked
		// of any regression in the funnel. It shares the bounded-token shape because published model
		// ids already fit it (claude-opus-5, gpt-5, claude-haiku-4-5-20251001) and because the shape is
		// what keeps a free-text field from becoming a place to leak prompt content.
		("model", request.Model)
	];

	/// <summary>
	/// The running token counters a caller may attach to any stage.
	/// </summary>
	/// <remarks>
	/// Deliberately a snapshot per event rather than one total at the end: an event is emitted when a
	/// stage is reached, so the series is monotonic and a session's real consumption is the maximum,
	/// while the differences show which stage of which flow actually cost the tokens. A single total
	/// would also require a session-end signal, which not every host provides.
	/// </remarks>
	private static IReadOnlyList<(string name, long? value)> TokenCounterFields(TelemetryEventRequest request) =>
	[
		("input_tokens", request.InputTokens),
		("output_tokens", request.OutputTokens),
		("cached_input_tokens", request.CachedInputTokens)
	];

	/// <summary>
	/// Validates a bounded lowercase identifier (a workflow or variant value). Linear scan rather than a
	/// regex, matching <see cref="IsAllowedSessionId"/>: no ReDoS surface and O(length) on capped input.
	/// </summary>
	/// <summary>
	/// Canonicalizes <c>coding_agent</c> to a lowercase slug so one host counts as one cohort.
	/// </summary>
	/// <remarks>
	/// Unlike <c>workflow</c> / <c>variant</c> / <c>model</c>, this field is only length-checked, because
	/// a host name is a proper noun rather than a bounded token the flow chooses. That let the same host
	/// arrive spelled three different ways in a single measured session ("Claude Code", "claude-code",
	/// "claude"), splitting one host across three cohorts — and instructing agents not to reshape the
	/// value did not stop it. Slugging is deterministic and merges the spellings that differ only in case
	/// and separators. Values that differ in WORDS (a truncated "claude") are deliberately left distinct:
	/// mapping them onto a canonical host would be a guess recorded as data.
	/// </remarks>
	internal static string NormalizeCodingAgent(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) {
			return value;
		}
		StringBuilder slug = new(value.Length);
		foreach (char character in value.Trim()) {
			if (char.IsAsciiLetterOrDigit(character)) {
				slug.Append(char.ToLowerInvariant(character));
			} else if (character is ('.' or '_' or '-' or ' ') && slug.Length > 0 && slug[^1] != '-') {
				// Collapse runs of separators so "GitHub  Copilot-CLI" and "github-copilot-cli" agree.
				slug.Append('-');
			}
		}
		return slug.ToString().TrimEnd('-');
	}

	internal static bool IsAllowedToken(string value) =>
		!string.IsNullOrWhiteSpace(value)
		&& value.Length <= MaxFieldLength
		&& value.All(character =>
			char.IsAsciiDigit(character) || char.IsAsciiLetterLower(character) || character is '.' or '_' or '-');

	private static IReadOnlyList<(string name, string value)> BoundedFields(TelemetryEventRequest request) =>
	[
		("coding_agent", request.CodingAgent),
		("plugin_version", request.PluginVersion)
	];

	// Linear character-set check instead of a regex: a session id is letters/digits plus '.', '_',
	// ':' or '-'. No regex means no ReDoS surface, and runtime is O(length) on an already
	// length-capped input.
	private static bool IsAllowedSessionId(string value) =>
		value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');

	private static IReadOnlyList<(string name, string value)> RequiredFields(TelemetryEventRequest request) =>
	[
		("session_id", request.SessionId),
		("event_name", request.EventName),
		("coding_agent", request.CodingAgent),
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
				?? new ConsentState(Unknown, DateTimeOffset.MinValue);
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
			StringAttribute("event_timestamp", timestamp.ToString("O")),
			StringAttribute("platform", GetPlatform()),
			StringAttribute("clio_version", GetClioVersion()),
			StringAttribute("coding_agent", NormalizeCodingAgent(request.CodingAgent)),
			StringAttribute("installation_id", GetOrCreateInstallationId()),
			StringAttribute("plugin_version", request.PluginVersion),
			StringAttribute("event_id", eventId)
		];
		// The flow dimension. Every stage event carries it, which is what keeps the stage names generic
		// and lets one query compare the same funnel step across flows.
		if (!string.IsNullOrWhiteSpace(request.Workflow)) {
			attributes.Add(StringAttribute("workflow", request.Workflow));
		}
		if (!string.IsNullOrWhiteSpace(request.Variant)) {
			attributes.Add(StringAttribute("variant", request.Variant));
		}
		if (!string.IsNullOrWhiteSpace(request.Model)) {
			attributes.Add(StringAttribute("model", request.Model));
		}
		foreach ((string name, long? value) in TokenCounterFields(request)) {
			if (value.HasValue) {
				attributes.Add(new OpenTelemetryAttribute(name, new OpenTelemetryValue(IntValue: value.Value)));
			}
		}
		if (request.DurationMs.HasValue) {
			attributes.Add(new OpenTelemetryAttribute("duration_ms", new OpenTelemetryValue(IntValue: request.DurationMs.Value)));
		}
		if (durationSinceSessionStartMs.HasValue) {
			attributes.Add(new OpenTelemetryAttribute("duration_since_session_start_ms",
				new OpenTelemetryValue(IntValue: durationSinceSessionStartMs.Value)));
		}
		return new OpenTelemetryLogEvent(
			TimeUnixNano: timestamp.ToUnixTimeMilliseconds() * 1_000_000,
			SeverityText: "INFO",
			Attributes: attributes,
			EventName: request.EventName);
	}

	private TelemetrySessionState ReadSessionState(string sessionId, string workflow)
	{
		string path = SessionStatePath(sessionId, workflow);
		if (!_fileSystem.File.Exists(path)) {
			return NewSessionState(sessionId, workflow);
		}
		try {
			return JsonSerializer.Deserialize<TelemetrySessionState>(_fileSystem.File.ReadAllText(path, Encoding.UTF8), JsonOptions)
				?? NewSessionState(sessionId, workflow);
		} catch {
			return NewSessionState(sessionId, workflow);
		}
	}

	private static TelemetrySessionState NewSessionState(string sessionId, string workflow) =>
		new(sessionId, new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal), workflow);

	/// <summary>
	/// Starts a clean state when a session-start arrives for a pair whose previous run already ended.
	/// </summary>
	/// <remarks>
	/// One session legitimately runs the same flow twice — the developer asks for one edit, then
	/// another. Both land in the same (session_id, workflow) pair, so without this the second run
	/// inherits the first one's stage timestamps: a measured 4-second edit reported
	/// <c>duration_ms</c> of 18 minutes, anchored on a <c>build_started</c> from the earlier run.
	/// A stale span is worse than a missing one — it is indistinguishable from a genuinely slow run.
	///
	/// Only a TERMINATED previous run is cleared. A repeated session-start inside a run that is still
	/// open keeps its history, so a stray second start cannot erase the stages already recorded.
	/// </remarks>
	private static TelemetrySessionState StartOfANewRun(TelemetrySessionState sessionState,
		TelemetryEventRequest request)
	{
		if (!IsSessionStartEvent(request.EventName)) {
			return sessionState;
		}
		bool previousRunEnded = sessionState.Events.Keys.Any(IsTerminalEvent);
		return previousRunEnded
			? NewSessionState(sessionState.SessionId, sessionState.Workflow)
			: sessionState;
	}

	/// <summary>
	/// True for a stage that ENDS a run, canonical or deprecated, after which a session-start belongs
	/// to a new run rather than the finished one.
	/// </summary>
	private static bool IsTerminalEvent(string eventName) =>
		eventName is "workflow_completed" or "workflow_failed"
			or "implementation_completed" or "implementation_failed";

	private static long? InferDurationMs(TelemetrySessionState sessionState, string eventName, DateTimeOffset timestamp)
	{
		string startEventName = eventName switch {
			"business_plan_generated" => SessionStartedEvent,
			"business_plan_approved" => FirstKnown(sessionState, "business_plan_generated"),
			"implementation_completed" => "implementation_started",
			"implementation_failed" => "implementation_started",
			"implementation_changes_applied" => "implementation_changes_requested",
			// Stage vocabulary: one mapping serves every flow, because the stages are flow-agnostic.
			// Each pair answers a question the raw funnel counts cannot — how long the plan took to
			// produce, how long the developer took to approve it, and how long the build then ran.
			"plan_presented" => WorkflowStartedEvent,
			"plan_blocked" => WorkflowStartedEvent,
			PlanApprovedEvent => FirstKnown(sessionState, "plan_presented"),
			BuildStartedEvent => FirstKnown(sessionState, PlanApprovedEvent),
			// Terminal events report the NARROWEST span available, so a run that failed during the build
			// reports the build duration rather than the whole session (total elapsed is carried
			// separately as duration_since_session_start_ms).
			"workflow_completed" => PreferredKnown(sessionState, BuildStartedEvent, PlanApprovedEvent, WorkflowStartedEvent),
			"workflow_failed" => PreferredKnown(sessionState, BuildStartedEvent, PlanApprovedEvent, WorkflowStartedEvent),
			"changes_applied" => FirstKnown(sessionState, "changes_requested"),
			_ => null
		};
		if (string.IsNullOrWhiteSpace(startEventName)
			|| !sessionState.Events.TryGetValue(startEventName, out DateTimeOffset startedAt)) {
			return null;
		}
		return Math.Max(0, (long)(timestamp - startedAt).TotalMilliseconds);
	}

	// Anchored on whichever start event this (session_id, workflow) pair actually produced — the
	// canonical `workflow_started` or the deprecated app-creation `session_started`, so anchoring only
	// on the app-creation name would leave every migration, mobile-conversion and branding event with
	// no elapsed-time measure at all. State is keyed per pair, so a flow never inherits the anchor of
	// another flow in the same host session (notably the `unattributed` session-start floor): a stage
	// with no start of its own reports no elapsed time rather than a span measured from a foreign run.
	private static long? InferDurationSinceSessionStartMs(TelemetrySessionState sessionState, string eventName, DateTimeOffset timestamp)
	{
		if (IsSessionStartEvent(eventName)) {
			return null;
		}
		string anchorEventName = sessionState.Events.Keys
			.Where(IsSessionStartEvent)
			.OrderBy(name => sessionState.Events[name])
			.FirstOrDefault();
		if (anchorEventName is null) {
			return null;
		}
		return Math.Max(0, (long)(timestamp - sessionState.Events[anchorEventName]).TotalMilliseconds);
	}

	/// <summary>
	/// True for a session-start event — the canonical <c>workflow_started</c> or the deprecated
	/// app-creation <c>session_started</c> — which anchors that session's elapsed-time measurements.
	/// </summary>
	private static bool IsSessionStartEvent(string eventName) =>
		eventName is WorkflowStartedEvent or SessionStartedEvent;

	private static string FirstKnown(TelemetrySessionState sessionState, params string[] eventNames)
	{
		return eventNames
			.Where(eventName => sessionState.Events.ContainsKey(eventName))
			.OrderBy(eventName => sessionState.Events[eventName])
			.FirstOrDefault();
	}

	/// <summary>
	/// Returns the first recorded event in the caller's PREFERENCE order, unlike
	/// <see cref="FirstKnown"/> which returns the chronologically earliest.
	/// </summary>
	/// <remarks>
	/// Used by the terminal failure events, where the useful span is the narrowest one available: a
	/// migration that failed after approval should report the BUILD duration, not the whole session
	/// (total elapsed is already carried separately as <c>duration_since_session_start_ms</c>).
	/// Chronological order would always pick the session start and lose that distinction.
	/// </remarks>
	private static string PreferredKnown(TelemetrySessionState sessionState, params string[] eventNames) =>
		eventNames.FirstOrDefault(sessionState.Events.ContainsKey);

	private void UpdateSessionState(TelemetrySessionState sessionState, string eventName, DateTimeOffset timestamp)
	{
		sessionState.Events[eventName] = timestamp;
		WriteJson(SessionStatePath(sessionState.SessionId, sessionState.Workflow), sessionState);
	}

	private static OpenTelemetryAttribute StringAttribute(string key, string value) =>
		new(key, new OpenTelemetryValue(StringValue: value));

	private void WriteEvent(string eventId, OpenTelemetryLogEvent logEvent)
	{
		string fileName = $"{_timeProvider.GetUtcNow():yyyyMMddTHHmmssfffZ}_{eventId}.json";
		WriteJson(Path.Combine(EventsDirectory, fileName), logEvent);
	}

	private void EnsureDirectories()
	{
		_fileSystem.Directory.CreateDirectory(TelemetryRoot);
		_fileSystem.Directory.CreateDirectory(EventsDirectory);
		_fileSystem.Directory.CreateDirectory(SessionsDirectory);
	}

	// Atomic write: serialize to a sibling ".tmp" then move-with-overwrite into place so a reader
	// (this process or another clio process) never observes a half-written file. For fixed-path
	// targets (consent/session) the temp name is reused, so it self-heals; for the unique-named
	// event files a crash between write and move leaves an orphan ".json.tmp" that the flusher reaps.
	private void WriteJson<T>(string path, T value)
	{
		_fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		string json = JsonSerializer.Serialize(value, JsonOptions);
		string tempPath = path + ".tmp";
		_fileSystem.File.WriteAllText(tempPath, json, Encoding.UTF8);
		_fileSystem.File.Move(tempPath, path, overwrite: true);
	}

	private string GetOrCreateInstallationId()
	{
		string existing = ReadInstallationId();
		if (!string.IsNullOrWhiteSpace(existing)) {
			return existing;
		}
		string installationId = Guid.NewGuid().ToString("N");
		string tempPath = $"{InstallationIdPath}.{installationId}.tmp";
		_fileSystem.File.WriteAllText(tempPath, installationId, Encoding.UTF8);
		// Replace only a blank/corrupt file; for a missing file use create-only Move so concurrent
		// clio processes converge on a single installation id (first writer wins) instead of churning.
		bool replaceExisting = _fileSystem.File.Exists(InstallationIdPath);
		try {
			_fileSystem.File.Move(tempPath, InstallationIdPath, replaceExisting);
			return installationId;
		} catch (IOException) {
			TryDeleteFile(tempPath);
			string winner = ReadInstallationId();
			return string.IsNullOrWhiteSpace(winner) ? installationId : winner;
		}
	}

	private string ReadInstallationId() =>
		_fileSystem.File.Exists(InstallationIdPath)
			? _fileSystem.File.ReadAllText(InstallationIdPath, Encoding.UTF8).Trim()
			: string.Empty;

	// Deletes every spooled file, returning the number of removed event files (".json", excluding any
	// crash-orphaned ".json.tmp"). Used by withdrawal to clear the not-yet-uploaded outbox.
	private int PurgeEvents()
	{
		string eventsDirectory = EventsDirectory;
		if (!_fileSystem.Directory.Exists(eventsDirectory)) {
			return 0;
		}
		int purged = 0;
		foreach (string path in _fileSystem.Directory.GetFiles(eventsDirectory)) {
			bool removed = TryDeleteFile(path);
			if (removed && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
				purged++;
			}
		}
		return purged;
	}

	// Best-effort delete of every file directly under a telemetry subdirectory (e.g. sessions/),
	// tolerating a momentarily locked file so withdrawal cleanup never fails the opt-out.
	private void PurgeFiles(string directory)
	{
		if (!_fileSystem.Directory.Exists(directory)) {
			return;
		}
		foreach (string path in _fileSystem.Directory.GetFiles(directory)) {
			TryDeleteFile(path);
		}
	}

	private bool TryDeleteFile(string path)
	{
		try {
			_fileSystem.File.Delete(path);
			return true;
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			_logger.LogDebug(ex, "telemetry file delete failed file={File}", Path.GetFileName(path));
			return false;
		}
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
	private string SessionStatePath(string sessionId, string workflow) =>
		Path.Combine(SessionsDirectory, $"{SessionFileName(sessionId, workflow)}.json");

	// Derive the session-state file name from a hash of the (validated) session id AND workflow: the
	// funnel's unit of a run is that pair, so each flow in a session keeps its own start anchor and
	// stage history. Hashing is collision-free (distinct pairs can never share state, unlike a lossy
	// character-replace) and traversal-safe. The unit separator cannot occur in either validated
	// value, so no ("ab", null) / ("a", "b") pair can collide.
	private static string SessionFileName(string sessionId, string workflow) =>
		Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{sessionId}\u001f{workflow ?? string.Empty}")))
			.ToLowerInvariant();

	private string EventsDirectory => TelemetryStoragePaths.EventsDirectory(TelemetryRoot);
}
