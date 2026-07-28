using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ClioRing.Ipc;

/// <summary>
/// A per-run deployment / uninstall <b>receipt</b> written as NDJSON of the same authoritative
/// <see cref="ClioStageEvent"/> stream the pipeline UI renders (story 10, ADR D6). One JSON line is
/// appended for each event exactly as it arrives on the wire (no second derivation that could disagree
/// with the UI — SM-03), and a final rolled-up summary line is written on <c>run-completed</c> for quick
/// diagnosis. The NDJSON event lines are replayable byte-for-byte: feeding them back into a fresh
/// <c>DeployPipelineViewModel</c> reconstructs the same model the user saw.
/// </summary>
/// <remarks>
/// <para>
/// The receipt is <b>secret-free by construction</b>: the events are redacted at the clio emitter source
/// (ADR D3) and the Ring adds no secret material, so nothing here is re-redacted (re-serialising would
/// break byte-for-byte replay). Every write is best-effort and never throws — a diagnostic must never
/// break a run, mirroring <c>StartupLog</c>.
/// </para>
/// <para>
/// One instance is wired as a side-observer of the pipeline's <c>StageEventObserved</c> event and lives
/// across runs; each run is keyed by its <c>runId</c>. A new <c>runId</c> (a fresh manifest) opens a new
/// per-run file; the terminal <c>run-completed</c> event flushes the summary and prunes old receipts.
/// This type is not thread-safe on its own; the pipeline marshals events onto the UI thread before they
/// reach here, and an internal lock guards the file append regardless.
/// </para>
/// </remarks>
public sealed class DeploymentReceipt {
	private readonly object _gate = new();
	private readonly string _logsFolder;
	private readonly int _maxRetainedReceipts;
	private readonly long _maxTotalBytes;

	// Per-run state, keyed by the run currently being written.
	private Guid _currentRunId;
	private string? _currentPath;
	private string _operation = ClioStageEventContract.Operations.Deploy;
	private string _summaryText = string.Empty;
	private long _totalDurationMs;
	// Ordered per-stage roll-up (manifest order), keyed by stageId for in-place status updates.
	private readonly List<StageRoll> _stageRolls = new();
	private readonly Dictionary<string, StageRoll> _stageRollsById = new(StringComparer.Ordinal);


	/// <summary>Creates a receipt writer targeting <paramref name="logsFolder"/>.</summary>
	/// <param name="logsFolder">Folder that receives the per-run <c>*.ndjson</c> receipt files.</param>
	/// <param name="maxRetainedReceipts">Cap on retained receipt files (oldest pruned first). Default 50.</param>
	/// <param name="maxTotalBytes">Cap on the total size of retained receipt files. Default 25 MB.</param>
	public DeploymentReceipt(string logsFolder, int maxRetainedReceipts = 50, long maxTotalBytes = 25L * 1024 * 1024) {
		_logsFolder = logsFolder ?? throw new ArgumentNullException(nameof(logsFolder));
		_maxRetainedReceipts = Math.Max(1, maxRetainedReceipts);
		_maxTotalBytes = Math.Max(1, maxTotalBytes);
	}

	/// <summary>The receipt file path for the run currently being written, or null when no run is active.</summary>
	public string? CurrentPath {
		get { lock (_gate) { return _currentPath; } }
	}

	/// <summary>
	/// The side-observer hook: subscribe this to the pipeline's <c>StageEventObserved</c> event. It appends
	/// one NDJSON line per event, opening a new per-run file on a fresh <c>runId</c> and flushing the rolled-up
	/// summary on <c>run-completed</c>. Best-effort; never throws.
	/// </summary>
	public void Observe(ClioStageEvent? stageEvent) {
		if (stageEvent is null) {
			return;
		}
		try {
			lock (_gate) {
				if (stageEvent.RunId != _currentRunId || _currentPath is null) {
					StartRun(stageEvent);
				}

				AppendEventLine(stageEvent);
				AccumulateSummary(stageEvent);

				if (stageEvent.EventType == ClioStageEventContract.EventTypes.RunCompleted) {
					FlushSummary(stageEvent);
					Prune();
					// Close the run so a later foreign/duplicate beat starts a fresh file rather than appending here.
					_currentPath = null;
					_currentRunId = Guid.Empty;
				}
			}
		}
		catch (Exception) {
			// Diagnostics must never break a run.
		}
	}

	private void StartRun(ClioStageEvent stageEvent) {
		_currentRunId = stageEvent.RunId;
		_operation = string.Equals(stageEvent.Operation, ClioStageEventContract.Operations.Uninstall, StringComparison.Ordinal)
			? ClioStageEventContract.Operations.Uninstall
			: ClioStageEventContract.Operations.Deploy;
		_summaryText = string.Empty;
		_totalDurationMs = 0;
		_stageRolls.Clear();
		_stageRollsById.Clear();

		Directory.CreateDirectory(_logsFolder);
		// Containment guard (defence in depth): resolve the per-run file and PROVE it stays directly inside the
		// logs folder before writing. The runId is a Guid and cannot path-traverse, but a hostile/future run key
		// with a path separator or ".." must never let a receipt escape the logs folder. On escape, leave the
		// run pathless (all writers no-op on a null path) rather than write outside the folder.
		_currentPath = ResolveContainedReceiptPath(_logsFolder, _operation, stageEvent.RunId.ToString("D"));
		if (_currentPath is null) {
			return;
		}
		// A fresh run truncates any stale file with the same runId so replay never sees two runs concatenated.
		File.WriteAllText(_currentPath, string.Empty);
	}

	/// <summary>
	/// Resolves the receipt file path for a run key and asserts it stays <b>directly inside</b>
	/// <paramref name="logsFolder"/> (defence in depth). Returns <c>null</c> when the resolved path would escape
	/// the folder — e.g. a path separator or <c>..</c> in <paramref name="runKey"/> — so a hostile key can never
	/// path-traverse. In normal operation the run key is a <see cref="Guid"/> and always resolves inside.
	/// </summary>
	/// <param name="logsFolder">The folder that must contain the receipt file.</param>
	/// <param name="operation">The sanitized operation kind (<c>deploy</c> / <c>uninstall</c>).</param>
	/// <param name="runKey">The per-run key that forms the file name (normally a Guid string).</param>
	/// <returns>The contained full path, or <c>null</c> when it would escape the logs folder.</returns>
	public static string? ResolveContainedReceiptPath(string logsFolder, string operation, string runKey) {
		string fileName = $"{operation}-{runKey}.ndjson";
		string folderFull = Path.GetFullPath(logsFolder);
		string candidateFull = Path.GetFullPath(Path.Combine(folderFull, fileName));
		string? parent = Path.GetDirectoryName(candidateFull);
		if (parent is null) {
			return null;
		}
		char[] trims = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
		string parentNorm = parent.TrimEnd(trims);
		string folderNorm = folderFull.TrimEnd(trims);
		// Directly inside: the resolved file's parent is exactly the logs folder (no sub-path, no escape).
		return string.Equals(parentNorm, folderNorm, StringComparison.OrdinalIgnoreCase)
			? candidateFull
			: null;
	}

	private void AppendEventLine(ClioStageEvent stageEvent) {
		if (_currentPath is null) {
			return;
		}
		// Canonical (compact) serialisation — identical to clio's emitter, so a write→read→write round-trips
		// byte-for-byte and the NDJSON is replayable.
		string line = JsonSerializer.Serialize(stageEvent, ClioStageEventJsonContext.Default.ClioStageEvent);
		File.AppendAllText(_currentPath, line + "\n");
	}

	private void AccumulateSummary(ClioStageEvent stageEvent) {
		switch (stageEvent.EventType) {
			case ClioStageEventContract.EventTypes.Manifest when stageEvent.Stages is not null:
				foreach (ClioStageManifestEntry entry in stageEvent.Stages) {
					var roll = new StageRoll { StageId = entry.StageId, Name = entry.Name, Status = "pending" };
					_stageRolls.Add(roll);
					_stageRollsById[entry.StageId] = roll;
				}
				break;

			case ClioStageEventContract.EventTypes.Stage when stageEvent.Stage is { } stage:
				if (!_stageRollsById.TryGetValue(stage.StageId, out StageRoll? existing)) {
					existing = new StageRoll { StageId = stage.StageId, Name = stage.Name };
					_stageRolls.Add(existing);
					_stageRollsById[stage.StageId] = existing;
				}
				existing.Name = stage.Name;
				existing.Status = stage.Status;
				if (stage.DurationMs is long ms) {
					existing.DurationMs = ms;
				}
				break;
		}
	}

	private void FlushSummary(ClioStageEvent stageEvent) {
		if (_currentPath is null) {
			return;
		}
		_summaryText = stageEvent.RunCompleted?.Summary ?? string.Empty;
		_totalDurationMs = _stageRolls.Sum(r => r.DurationMs ?? 0);
		string outcome = stageEvent.RunCompleted?.Outcome ?? string.Empty;

		var summary = new ReceiptSummary(
			RunId: _currentRunId,
			Operation: _operation,
			Outcome: outcome,
			Summary: _summaryText,
			TotalDurationMs: _totalDurationMs,
			Stages: _stageRolls
				.Select(r => new ReceiptStageOutcome(r.StageId, r.Name, r.Status, r.DurationMs))
				.ToList());

		// Wrapped under "receiptSummary" so a replay reader (which keys off "eventType") can tell it apart
		// from the event lines and skip it — the event lines alone stay a pure, replayable wire stream.
		var envelope = new ReceiptSummaryEnvelope(summary);
		string line = JsonSerializer.Serialize(envelope, ClioStageEventJsonContext.Default.ReceiptSummaryEnvelope);
		File.AppendAllText(_currentPath, line + "\n");
	}

	// Best-effort rotation: keep the folder bounded by both file count and total size (oldest receipts first).
	private void Prune() {
		try {
			FileInfo[] files = new DirectoryInfo(_logsFolder)
				.GetFiles("*.ndjson")
				.OrderByDescending(f => f.LastWriteTimeUtc)
				.ToArray();

			long running = 0;
			for (int i = 0; i < files.Length; i++) {
				running += files[i].Length;
				bool overCount = i >= _maxRetainedReceipts;
				bool overSize = running > _maxTotalBytes;
				if (overCount || overSize) {
					try { files[i].Delete(); }
					catch (IOException) { /* locked file — skip, retry next run */ }
					catch (UnauthorizedAccessException) { /* skip */ }
				}
			}
		}
		catch (DirectoryNotFoundException) {
			// Nothing to prune.
		}
	}

	private sealed class StageRoll {
		public string StageId { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
		public string Status { get; set; } = "pending";
		public long? DurationMs { get; set; }
	}
}

/// <summary>
/// The rolled-up summary of one receipt: the terminal outcome plus every stage's final outcome and
/// duration. Written as the last line of the NDJSON receipt for quick diagnosis without replaying the
/// whole stream. Wrapped in <see cref="ReceiptSummaryEnvelope"/> on disk so it is distinguishable from
/// the event lines.
/// </summary>
/// <param name="RunId">The run this receipt belongs to.</param>
/// <param name="Operation">Operation kind (<c>deploy</c> / <c>uninstall</c>).</param>
/// <param name="Outcome">Terminal outcome (<c>success</c> / <c>failure</c>).</param>
/// <param name="Summary">Friendly terminal summary from <c>run-completed</c>.</param>
/// <param name="TotalDurationMs">Sum of the per-stage durations.</param>
/// <param name="Stages">Per-stage final outcome + duration, in manifest order.</param>
public sealed record ReceiptSummary(
	[property: JsonPropertyName("runId")] Guid RunId,
	[property: JsonPropertyName("operation")] string Operation,
	[property: JsonPropertyName("outcome")] string Outcome,
	[property: JsonPropertyName("summary")] string Summary,
	[property: JsonPropertyName("totalDurationMs")] long TotalDurationMs,
	[property: JsonPropertyName("stages")] IReadOnlyList<ReceiptStageOutcome> Stages);

/// <summary>One stage's final outcome in a <see cref="ReceiptSummary"/>.</summary>
/// <param name="StageId">Stable kebab-case stage key.</param>
/// <param name="Name">Human-readable stage name.</param>
/// <param name="Status">Final status (<c>done</c> / <c>failed</c> / <c>skipped</c> / <c>pending</c>).</param>
/// <param name="DurationMs">Elapsed milliseconds, when known.</param>
public sealed record ReceiptStageOutcome(
	[property: JsonPropertyName("stageId")] string StageId,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("status")] string Status,
	[property: JsonPropertyName("durationMs")] long? DurationMs);

/// <summary>On-disk wrapper that tags the summary line so a replay reader can skip it.</summary>
/// <param name="ReceiptSummary">The rolled-up summary.</param>
public sealed record ReceiptSummaryEnvelope(
	[property: JsonPropertyName("receiptSummary")] ReceiptSummary ReceiptSummary);

/// <summary>
/// Reads a receipt NDJSON file back into its parts: the replayable typed event stream (each event line)
/// and the optional rolled-up summary (the wrapped final line). Used by the SM-03 replay-equality proof
/// and by any tooling that needs to reconstruct exactly what the UI showed. Never throws — a corrupt or
/// unreadable line is skipped.
/// </summary>
public static class DeploymentReceiptReader {
	/// <summary>
	/// Returns the ordered <see cref="ClioStageEvent"/>s recorded in the receipt (the summary line and any
	/// unparseable line are skipped), ready to replay into a fresh pipeline.
	/// </summary>
	public static IReadOnlyList<ClioStageEvent> ReadEvents(string path) {
		var events = new List<ClioStageEvent>();
		if (!File.Exists(path)) {
			return events;
		}
		foreach (string line in File.ReadLines(path)) {
			if (string.IsNullOrWhiteSpace(line)) {
				continue;
			}
			try {
				if (JsonNode.Parse(line) is not JsonObject obj) {
					continue;
				}
				// Replay separation: a replayable event line is identified SOLELY by "eventType". The trailing
				// rolled-up summary is wrapped under "receiptSummary" and is explicitly excluded so it can never
				// be fed into replay — the event lines alone stay a pure, replayable wire stream.
				if (obj["receiptSummary"] is not null || obj["eventType"] is null) {
					continue;
				}
				ClioStageEvent? evt = obj.Deserialize(ClioStageEventJsonContext.Default.ClioStageEvent);
				if (evt is not null) {
					events.Add(evt);
				}
			}
			catch (JsonException) {
				// Skip a corrupt line rather than fail the whole replay.
			}
		}
		return events;
	}

	/// <summary>Returns the rolled-up summary from the receipt's final line, or null when absent.</summary>
	public static ReceiptSummary? ReadSummary(string path) {
		if (!File.Exists(path)) {
			return null;
		}
		foreach (string line in File.ReadLines(path)) {
			if (string.IsNullOrWhiteSpace(line)) {
				continue;
			}
			try {
				if (JsonNode.Parse(line) is JsonObject obj && obj["receiptSummary"] is JsonObject summaryObj) {
					return summaryObj.Deserialize(ClioStageEventJsonContext.Default.ReceiptSummary);
				}
			}
			catch (JsonException) {
				// Skip and keep scanning.
			}
		}
		return null;
	}
}
