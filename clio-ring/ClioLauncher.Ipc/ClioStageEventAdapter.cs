using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClioLauncher.Ipc;

/// <summary>
/// Turns a raw MCP <c>notifications/progress</c> notification into a typed <see cref="ClioStageEvent"/>
/// and forwards it to a sink, applying the FR-12 tolerance rules (ADR D5). This deliberately bypasses
/// the SDK's <c>IProgress&lt;ProgressNotificationValue&gt;</c> callback, which drops the <c>_meta</c>
/// envelope (ADR fact 6): the structured payload travels in <c>params._meta.clioStageEvent</c> and can
/// only be read from the raw <see cref="JsonNode"/> notification params.
/// </summary>
/// <remarks>
/// One adapter instance is created per tool call. It is fed every progress notification the session
/// dispatches; correlation is by <c>progressToken</c> so concurrent calls never cross-contaminate.
/// The adapter never throws on bad input — a missing/malformed <c>_meta</c>, an unknown extra field,
/// or a duplicate <c>sequence</c> is skipped silently so a noisy stream can never crash a deploy.
/// Distinct out-of-order events are buffered because SDK notification callbacks may execute concurrently;
/// the sink receives the producer's contiguous sequence beginning with the manifest.
/// <see cref="Consume"/> is thread-safe.
/// </remarks>
public sealed class ClioStageEventAdapter {
	private readonly string? _expectedProgressToken;
	private readonly IProgress<ClioStageEvent> _sink;

	private readonly object _consumeLock = new();
	private readonly Dictionary<Guid, RunBuffer> _buffersByRun = new();

	private sealed class RunBuffer {
		public int NextSequence { get; set; }
		public SortedDictionary<int, ClioStageEvent> Pending { get; } = new();
	}

	/// <summary>
	/// Creates an adapter that forwards decoded events to <paramref name="sink"/>.
	/// </summary>
	/// <param name="sink">Where decoded, de-duplicated typed events are reported.</param>
	/// <param name="expectedProgressToken">
	/// The <c>progressToken</c> this call issued; when non-null, notifications carrying any other token
	/// (a foreign/concurrent run) are ignored. When null, correlation is skipped and every progress
	/// notification is considered (used by isolated unit tests).
	/// </param>
	public ClioStageEventAdapter(IProgress<ClioStageEvent> sink, string? expectedProgressToken = null) {
		_sink = sink ?? throw new ArgumentNullException(nameof(sink));
		_expectedProgressToken = expectedProgressToken;
	}

	/// <summary>
	/// Processes one <c>notifications/progress</c> params node. Reads and correlates the
	/// <c>progressToken</c>, extracts <c>_meta.clioStageEvent</c>, deserializes it into the typed
	/// mirror (tolerating unknown fields), drops an exact duplicate <c>sequence</c> per <c>runId</c>,
	/// and reports the survivor to the sink. Never throws.
	/// </summary>
	/// <param name="notificationParams">The raw <c>params</c> object of the notification, or null.</param>
	/// <returns>The typed event that was raised, or null when the notification was skipped.</returns>
	public ClioStageEvent? Consume(JsonNode? notificationParams) {
		lock (_consumeLock) {
			return ConsumeLocked(notificationParams);
		}
	}

	private ClioStageEvent? ConsumeLocked(JsonNode? notificationParams) {
		if (notificationParams is not JsonObject paramsObject) {
			return null;
		}

		// Correlation (AC-05): ignore notifications for a foreign/unknown run when a token is expected.
		if (_expectedProgressToken is not null &&
			!string.Equals(ReadProgressToken(paramsObject), _expectedProgressToken, StringComparison.Ordinal)) {
			return null;
		}

		// The structured envelope rides in _meta.clioStageEvent; anything else here is not our payload.
		if (paramsObject["_meta"] is not JsonObject metaObject ||
			metaObject["clioStageEvent"] is not JsonObject stageEventObject) {
			return null;
		}

		ClioStageEvent? stageEvent = TryDeserialize(stageEventObject);
		if (stageEvent is null) {
			return null;
		}

		// The MCP SDK invokes notification handlers concurrently. Buffer by producer sequence and release
		// only a contiguous prefix, starting with the seq=0 manifest. This preserves the producer's order
		// even when callbacks enter as 2,1,0 and prevents both manifest and terminal loss in the UI.
		if (!_buffersByRun.TryGetValue(stageEvent.RunId, out RunBuffer? buffer)) {
			buffer = new RunBuffer();
			_buffersByRun[stageEvent.RunId] = buffer;
		}
		if (stageEvent.Sequence < buffer.NextSequence || buffer.Pending.ContainsKey(stageEvent.Sequence)) {
			return null;
		}
		buffer.Pending[stageEvent.Sequence] = stageEvent;

		while (buffer.Pending.Remove(buffer.NextSequence, out ClioStageEvent? next)) {
			_sink.Report(next);
			buffer.NextSequence++;
		}
		return stageEvent;
	}

	// The progressToken wire value can be a string or a number (JSON-RPC union); normalize to text
	// so a string comparison works regardless of which form clio emitted.
	private static string? ReadProgressToken(JsonObject paramsObject) {
		if (paramsObject["progressToken"] is not JsonValue tokenValue) {
			return null;
		}
		if (tokenValue.TryGetValue(out string? asString)) {
			return asString;
		}
		if (tokenValue.TryGetValue(out long asLong)) {
			return asLong.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}
		return tokenValue.ToJsonString();
	}

	// Unknown extra fields are tolerated (System.Text.Json skips them by default); a genuinely
	// malformed envelope (wrong types, unparseable) is swallowed and reported as a skip, never a throw.
	private static ClioStageEvent? TryDeserialize(JsonObject stageEventObject) {
		try {
			return stageEventObject.Deserialize(ClioStageEventJsonContext.Default.ClioStageEvent);
		}
		catch (JsonException) {
			return null;
		}
		catch (NotSupportedException) {
			return null;
		}
	}
}
