using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClioRing.Ipc;

/// <summary>
/// Ring-side <b>mirror</b> of clio's <c>ClioStageEvent</c> progress envelope (ADR D2). This is a
/// hand-copied record — the two repos ship on independent cadence and share the contract by mirror,
/// not by a binary/NuGet package. Cross-repo drift is guarded by a committed byte-identical JSON
/// fixture asserted on both sides (see <c>ClioStageEventAdapterTests</c> / TC-U-30).
/// </summary>
/// <remarks>
/// The wire shape is authoritative: camelCase property names, <b>string</b> wire values (NOT enums)
/// for <see cref="EventType"/> / <see cref="Operation"/> / stage status / outcome, 0-based
/// <c>index</c>, a stable <c>total</c>, and optional members omitted when <c>null</c>. Exactly one of
/// <see cref="Stages"/>, <see cref="Stage"/>, or <see cref="RunCompleted"/> is populated, selected by
/// <see cref="EventType"/>. The declaration order of every member here matches clio's record so that
/// a deserialize + canonical re-serialize round-trips byte-for-byte.
/// </remarks>
/// <param name="SchemaVersion">Contract version; currently <c>1</c>. Bumped only on a breaking field change.</param>
/// <param name="EventType">Envelope discriminator: one of <see cref="ClioStageEventContract.EventTypes"/>.</param>
/// <param name="RunId">Identifier of the real operation this event belongs to (one per run).</param>
/// <param name="Sequence">Monotonically increasing at the producer; consumers use it to restore producer order when transport callbacks arrive concurrently.</param>
/// <param name="Operation">Operation kind: one of <see cref="ClioStageEventContract.Operations"/>.</param>
/// <param name="Stages">Manifest of every stage that will run, present only when <see cref="EventType"/> is <c>manifest</c>.</param>
/// <param name="Stage">Per-stage transition, present only when <see cref="EventType"/> is <c>stage</c>.</param>
/// <param name="RunCompleted">Terminal outcome, present only when <see cref="EventType"/> is <c>run-completed</c>.</param>
public sealed record ClioStageEvent(
	[property: JsonPropertyName("schemaVersion")] int SchemaVersion,
	[property: JsonPropertyName("eventType")] string EventType,
	[property: JsonPropertyName("runId")] Guid RunId,
	[property: JsonPropertyName("sequence")] int Sequence,
	[property: JsonPropertyName("operation")] string Operation,
	[property: JsonPropertyName("stages")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<ClioStageManifestEntry>? Stages = null,
	[property: JsonPropertyName("stage")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	ClioStageDetail? Stage = null,
	[property: JsonPropertyName("runCompleted")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	ClioRunCompleted? RunCompleted = null);

/// <summary>
/// One entry in a <c>manifest</c> event: a stage that will run given the resolved execution path.
/// </summary>
/// <param name="StageId">Stable kebab-case stage key.</param>
/// <param name="Name">Human-readable stage name for the UI.</param>
/// <param name="Index">Zero-based position of this stage within the manifest.</param>
/// <param name="Total">Manifest length (stable denominator for the progress bar).</param>
/// <param name="Conditional">
/// <c>true</c> when the stage is inert by condition and will later be emitted as <c>skipped</c>.
/// </param>
public sealed record ClioStageManifestEntry(
	[property: JsonPropertyName("stageId")] string StageId,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("index")] int Index,
	[property: JsonPropertyName("total")] int Total,
	[property: JsonPropertyName("conditional")] bool Conditional);

/// <summary>
/// The <c>stage</c> payload: a single stage transition (running / done / failed / skipped).
/// Optional members are omitted from the wire when <c>null</c>.
/// </summary>
/// <param name="StageId">Stable kebab-case stage key.</param>
/// <param name="Name">Human-readable stage name for the UI.</param>
/// <param name="Index">Zero-based position within the manifest (matches the manifest entry).</param>
/// <param name="Total">Manifest length (matches the manifest entry).</param>
/// <param name="Status">Stage status: one of <see cref="ClioStageEventContract.StageStatuses"/>.</param>
/// <param name="StartedAtUtc">When the stage started running; set on <c>running</c>, omitted otherwise.</param>
/// <param name="DurationMs">Elapsed milliseconds; set on <c>done</c>/<c>failed</c>, omitted otherwise.</param>
/// <param name="Message">Non-secret human-readable progress message (always present).</param>
/// <param name="Detail">Optional non-secret technical context.</param>
/// <param name="ErrorCode">Optional stable symbolic error code.</param>
/// <param name="SkipReason">Why a <c>skipped</c> stage was skipped: one of <see cref="ClioStageEventContract.SkipReasons"/>.</param>
public sealed record ClioStageDetail(
	[property: JsonPropertyName("stageId")] string StageId,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("index")] int Index,
	[property: JsonPropertyName("total")] int Total,
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("startedAtUtc")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	DateTimeOffset? StartedAtUtc = null,
	[property: JsonPropertyName("durationMs")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	long? DurationMs = null,
	[property: JsonPropertyName("message")] string Message = "",
	[property: JsonPropertyName("detail")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? Detail = null,
	[property: JsonPropertyName("errorCode")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? ErrorCode = null,
	[property: JsonPropertyName("skipReason")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? SkipReason = null);

/// <summary>
/// The <c>run-completed</c> payload: the terminal outcome of a deploy/uninstall run.
/// Optional members are omitted from the wire when <c>null</c>.
/// </summary>
/// <param name="Outcome">Terminal outcome: one of <see cref="ClioStageEventContract.RunOutcomes"/>.</param>
/// <param name="Summary">Short non-secret human-readable summary of the run.</param>
/// <param name="Detail">Optional non-secret technical detail.</param>
/// <param name="ErrorCode">Optional stable symbolic error code on failure.</param>
/// <param name="DerivedUrl">Optional URL derived from the run (e.g. the deployed application URL).</param>
/// <param name="DerivedPath">Optional filesystem path derived from the run (e.g. the install directory).</param>
public sealed record ClioRunCompleted(
	[property: JsonPropertyName("outcome")] string Outcome,
	[property: JsonPropertyName("summary")] string Summary,
	[property: JsonPropertyName("detail")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? Detail = null,
	[property: JsonPropertyName("errorCode")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? ErrorCode = null,
	[property: JsonPropertyName("derivedUrl")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? DerivedUrl = null,
	[property: JsonPropertyName("derivedPath")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? DerivedPath = null);

/// <summary>
/// Shared constants mirroring clio's <c>ClioStageEventContract</c>: the schema version, the canonical
/// <see cref="System.Text.Json"/> options, and the stable string wire vocabularies. <see cref="SchemaVersion"/>
/// is the cross-repo compatibility gate; the byte shape is anchored by a committed JSON fixture.
/// </summary>
public static class ClioStageEventContract {

	/// <summary>Current contract version the Ring mirror understands. Bumped only on a breaking field change.</summary>
	public const int SchemaVersion = 1;

	/// <summary>
	/// Canonical serializer options for the contract: compact (no indentation), so each event
	/// serializes to a single line — identical to clio's emitter so a deserialize + re-serialize
	/// round-trips byte-for-byte. Unknown members are tolerated on read (System.Text.Json skips them).
	/// </summary>
	public static JsonSerializerOptions SerializerOptions { get; } = new() {
		WriteIndented = false
	};

	/// <summary>Allowed <see cref="ClioStageEvent.EventType"/> values.</summary>
	public static class EventTypes {

		/// <summary>The up-front manifest of every stage that will run.</summary>
		public const string Manifest = "manifest";

		/// <summary>A single stage transition.</summary>
		public const string Stage = "stage";

		/// <summary>The terminal outcome of the run.</summary>
		public const string RunCompleted = "run-completed";
	}

	/// <summary>Allowed <see cref="ClioStageEvent.Operation"/> values.</summary>
	public static class Operations {

		/// <summary>A Creatio deploy operation.</summary>
		public const string Deploy = "deploy";

		/// <summary>A Creatio uninstall operation.</summary>
		public const string Uninstall = "uninstall";
	}

	/// <summary>Allowed <see cref="ClioStageDetail.Status"/> values.</summary>
	public static class StageStatuses {

		/// <summary>The stage is currently running.</summary>
		public const string Running = "running";

		/// <summary>The stage completed successfully.</summary>
		public const string Done = "done";

		/// <summary>The stage failed.</summary>
		public const string Failed = "failed";

		/// <summary>The stage was skipped (see <see cref="ClioStageDetail.SkipReason"/>).</summary>
		public const string Skipped = "skipped";
	}

	/// <summary>Allowed <see cref="ClioRunCompleted.Outcome"/> values.</summary>
	public static class RunOutcomes {

		/// <summary>The run completed successfully.</summary>
		public const string Success = "success";

		/// <summary>The run failed.</summary>
		public const string Failure = "failure";
	}

	/// <summary>Allowed <see cref="ClioStageDetail.SkipReason"/> values.</summary>
	public static class SkipReasons {

		/// <summary>The stage is inert for the resolved inputs.</summary>
		public const string NotApplicable = "not-applicable";

		/// <summary>The stage was skipped because an earlier stage failed (failure cascade).</summary>
		public const string AfterFailure = "after-failure";

		/// <summary>The stage is not supported.</summary>
		public const string NotSupported = "not-supported";
	}
}

/// <summary>Source-generated JSON metadata for typed stage events under the AOT desktop host.</summary>
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ClioStageEvent))]
[JsonSerializable(typeof(ReceiptSummary))]
[JsonSerializable(typeof(ReceiptSummaryEnvelope))]
public sealed partial class ClioStageEventJsonContext : JsonSerializerContext;
