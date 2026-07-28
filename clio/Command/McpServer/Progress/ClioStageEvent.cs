using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Progress;

/// <summary>
/// A versioned, ordered progress envelope emitted once per deploy/uninstall stage transition.
/// </summary>
/// <remarks>
/// This is the single typed contract shared (by <b>mirror</b>, not by binary) between the clio
/// MCP progress emitter, the on-disk deployment receipt, and the Ring UI (ADR D1/D2). Exactly one
/// of <see cref="Stages"/>, <see cref="Stage"/>, or <see cref="RunCompleted"/> is populated,
/// selected by <see cref="EventType"/>. Cross-repo compatibility is anchored by
/// <see cref="SchemaVersion"/> plus a committed byte-identical JSON fixture asserted on both sides.
/// The envelope carries <b>no</b> field that could hold a connection string, credential, or token
/// by design; redaction is the emitter's responsibility (ADR D3).
/// </remarks>
/// <param name="SchemaVersion">Contract version; currently <c>1</c>. Bumped only on a breaking field change.</param>
/// <param name="EventType">Envelope discriminator: one of <see cref="ClioStageEventContract.EventTypes"/>.</param>
/// <param name="RunId">Identifier of the real operation this event belongs to (one per run).</param>
/// <param name="Sequence">Monotonically increasing per run; consumers de-dup and drop out-of-order events.</param>
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
/// <param name="StageId">Stable kebab-case stage key from <see cref="StageIds"/>.</param>
/// <param name="Name">Human-readable stage name for the UI.</param>
/// <param name="Index">Zero-based position of this stage within the manifest.</param>
/// <param name="Total">Manifest length (stable denominator for the progress bar).</param>
/// <param name="Conditional">
/// <c>true</c> when the stage is inert by condition (e.g. <c>stage-build</c> for a non-network source)
/// and will later be emitted as <c>skipped</c> with <c>skipReason=not-applicable</c>.
/// </param>
public sealed record ClioStageManifestEntry(
	[property: JsonPropertyName("stageId")] string StageId,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("index")] int Index,
	[property: JsonPropertyName("total")] int Total,
	[property: JsonPropertyName("conditional")] bool Conditional);

/// <summary>
/// The <c>stage</c> payload: a single stage transition (running / done / failed / skipped).
/// </summary>
/// <remarks>Optional members are omitted from the wire when <c>null</c> (no null noise).</remarks>
/// <param name="StageId">Stable kebab-case stage key from <see cref="StageIds"/>.</param>
/// <param name="Name">Human-readable stage name for the UI.</param>
/// <param name="Index">Zero-based position within the manifest (matches the manifest entry).</param>
/// <param name="Total">Manifest length (matches the manifest entry).</param>
/// <param name="Status">Stage status: one of <see cref="ClioStageEventContract.StageStatuses"/>.</param>
/// <param name="StartedAtUtc">When the stage started running; set on <c>running</c>, omitted otherwise.</param>
/// <param name="DurationMs">Elapsed milliseconds; set on <c>done</c>/<c>failed</c>, omitted otherwise.</param>
/// <param name="Message">Non-secret human-readable progress message (always present).</param>
/// <param name="Detail">Optional non-secret technical context.</param>
/// <param name="ErrorCode">Optional stable symbolic error code (never a secret or raw exception text).</param>
/// <param name="SkipReason">
/// Why a <c>skipped</c> stage was skipped: one of <see cref="ClioStageEventContract.SkipReasons"/>.
/// </param>
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
/// </summary>
/// <remarks>Optional members are omitted from the wire when <c>null</c>.</remarks>
/// <param name="Outcome">Terminal outcome: one of <see cref="ClioStageEventContract.RunOutcomes"/>.</param>
/// <param name="Summary">Short non-secret human-readable summary of the run.</param>
/// <param name="Detail">Optional non-secret technical detail (e.g. the failing stage context).</param>
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
