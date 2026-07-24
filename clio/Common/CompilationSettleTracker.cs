using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.CreatioModel;

namespace Clio.Common;

/// <summary>
/// Detects when a Creatio compilation session observed via <see cref="CompilationHistory"/>
/// records has gone quiet, and whether the observed session looks like a clean full finish.
/// </summary>
public interface ICompilationSettleTracker {

	/// <summary>
	/// Seeds tracker state from the record that was already the latest at watch-start.
	/// </summary>
	/// <param name="baseline">The latest known record at watch-start, or <c>null</c> when the
	/// environment has no compilation history at all.</param>
	/// <param name="now">Current time, used when <paramref name="baseline"/> is <c>null</c>.</param>
	void SeedFromBaseline(CompilationHistory baseline, DateTime now);

	/// <summary>
	/// Records a newly observed compilation history row (created after the baseline).
	/// </summary>
	void Observe(CompilationHistory record, DateTime now);

	/// <summary>
	/// True once no new activity has been observed for at least the adaptive quiet window.
	/// </summary>
	bool IsSettled(DateTime now);

	/// <summary>
	/// Current tracked state, read after <see cref="IsSettled"/> returns <c>true</c>.
	/// </summary>
	CompilationSettleSnapshot Snapshot { get; }

}

/// <summary>
/// Point-in-time view of a <see cref="ICompilationSettleTracker"/>.
/// </summary>
/// <param name="HasErrors">True if any observed record (including the seeded baseline) carried errors/warnings.</param>
/// <param name="SawFinalMarker">True if the ODataEntities marker project was observed.</param>
/// <param name="NewRecordCount">Number of records observed strictly after the baseline (genuine new activity).</param>
/// <param name="LastActivityAt">Timestamp the quiet window is measured from.</param>
public record CompilationSettleSnapshot(bool HasErrors, bool SawFinalMarker, int NewRecordCount, DateTime LastActivityAt);

public class CompilationSettleTracker : ICompilationSettleTracker {

	#region Constants

	// ODataEntities is always the last project a full Creatio configuration compile builds.
	// Seeing it with a clean session is the strongest available signal that we observed a
	// genuine full finish rather than a lull between two projects.
	internal const string FinalMarkerProjectName = "Terrasoft.Configuration.ODataEntities.csproj";

	// There is no signal that a compile is about to write its next row, only observed gaps to
	// calibrate against - two measured live against a real environment (cec, full
	// compile-configuration --all): ~18s between the HTTP trigger and the first
	// CompilationHistory row ever being written, and ~31-33s between an ordinary package
	// finishing and the next (slow, aggregate) project's own row. Both are legitimate
	// "still working" gaps, not "finished". 45s clears both with margin. This applies
	// UNCONDITIONALLY, not just before the first row is seen: the same gap shape recurs
	// mid-stream whenever the NEXT project happens to be slower than everything seen so
	// far, and by definition its own duration can't have been used to size the window in
	// advance - an earlier version of this constant (8s) gated ONLY the before-first-row
	// case for exactly this reason and still falsely settled ~32s into the real Dev.csproj
	// gap once activity had started, because BaseQuietWindowSeconds itself was too small.
	internal const int BaseQuietWindowSeconds = 45;

	// A single project's own build can legitimately run even longer than the base window (a
	// very large package can take a minute or more), so the window scales further once one
	// that slow has actually been observed, instead of relying on the fixed base alone.
	internal const double DurationScaleFactor = 1.5;

	#endregion

	#region Fields: Private

	private DateTime _lastActivityAt;
	private int _slowestDurationSeconds;
	private bool _hasErrors;
	private bool _sawFinalMarker;
	private int _newRecordCount;

	#endregion

	#region Methods: Public

	public void SeedFromBaseline(CompilationHistory baseline, DateTime now) {
		// Always seed the quiet-window clock from wall-clock "now", never from baseline.CreatedOn.
		// A stale baseline (last known compile could be hours old) must not let the very first
		// empty poll look instantly settled - that would return success before a compile someone
		// just triggered has had a chance to write its first row. baseline is still used below to
		// carry over error/marker state, and separately as the query cutoff in WatchCompilationCommand.
		_lastActivityAt = now;
		if (baseline is not null) {
			ApplyRecordState(baseline);
		}
	}

	public void Observe(CompilationHistory record, DateTime now) {
		ArgumentNullException.ThrowIfNull(record);
		_newRecordCount++;
		_lastActivityAt = now;
		ApplyRecordState(record);
	}

	public bool IsSettled(DateTime now) {
		double windowSeconds = Math.Max(BaseQuietWindowSeconds, _slowestDurationSeconds * DurationScaleFactor);
		if ((now - _lastActivityAt).TotalSeconds < windowSeconds) {
			return false;
		}
		// Activity started (e.g. a package-only compile) but the full-compile marker never
		// showed up: assume a full compile may still be running rather than declaring an
		// unconfirmed partial finish after just one quiet gap (observed live in a CI/TeamCity
		// trigger: a single package row, then 45s quiet, while the actual full compile was
		// still running). The caller keeps polling, bounded by its own overall give-up-after
		// budget, until either the marker appears or that budget is exhausted.
		return _newRecordCount == 0 || _sawFinalMarker;
	}

	public CompilationSettleSnapshot Snapshot =>
		new(_hasErrors, _sawFinalMarker, _newRecordCount, _lastActivityAt);

	#endregion

	#region Methods: Private

	private void ApplyRecordState(CompilationHistory record) {
		_slowestDurationSeconds = Math.Max(_slowestDurationSeconds, record.DurationInSeconds);
		if (HasRealError(record.ErrorsWarnings)) {
			_hasErrors = true;
		}
		if (string.Equals(record.ProjectName, FinalMarkerProjectName, StringComparison.OrdinalIgnoreCase)) {
			_sawFinalMarker = true;
		}
	}

	// ErrorsWarnings can legitimately hold warning-only entries on an otherwise successful compile
	// (observed live: a real compile-configuration --all run on cec finished successfully with one
	// CS0114 "hides inherited member" warning). Only a non-warning entry should fail the watch's
	// exit code, matching the IsWarning field CompileConfigurationCommand's own CompError already
	// parses. Unparseable content is treated as an error - favor under-claiming success in a
	// CI-facing exit code over a false "clean" result.
	private static bool HasRealError(string errorsWarnings) {
		if (string.IsNullOrWhiteSpace(errorsWarnings) || string.Equals(errorsWarnings, "[]", StringComparison.OrdinalIgnoreCase)) {
			return false;
		}
		try {
			List<CompilationLogEntry> entries = JsonSerializer.Deserialize<List<CompilationLogEntry>>(errorsWarnings, JsonOptions);
			return entries is not null && entries.Exists(entry => !entry.IsWarning);
		} catch (JsonException) {
			return true;
		}
	}

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	private sealed record CompilationLogEntry([property: JsonPropertyName("IsWarning")] bool IsWarning);

	#endregion

}
