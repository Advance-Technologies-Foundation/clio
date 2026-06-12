namespace Clio.Command.ProcessModel;

using System.Collections.Generic;
using System.Linq;
using Clio.CreatioModel;
using ErrorOr;

/// <summary>
/// Pure selection logic for resolving a process from <see cref="VwProcessLib"/> rows by system
/// <c>Name</c> (process code) with a fallback to display <c>Caption</c> (ENG-91168). Kept free of data
/// access so it is unit-testable with plain in-memory rows.
/// </summary>
internal static class ProcessLibResolver {

	/// <summary>
	/// Picks the resolved process row, or returns a typed error.
	/// </summary>
	/// <param name="nameOrCaption">The value the caller passed (process code or display caption).</param>
	/// <param name="byName">The exact <c>Name</c> match, or <c>null</c> when none.</param>
	/// <param name="byCaption">Rows matching the value by <c>Caption</c> (used only when there is no name match).</param>
	/// <returns>
	/// The matched row; <see cref="ErrorType.NotFound"/> when nothing matches; or
	/// <see cref="ErrorType.Conflict"/> when the caption matches more than one process.
	/// </returns>
	public static ErrorOr<VwProcessLib> Resolve(string nameOrCaption, VwProcessLib byName,
		IReadOnlyList<VwProcessLib> byCaption) {
		// Exact match by the system Name (process code) wins — Name is unique.
		if (byName is not null) {
			return byName;
		}
		IReadOnlyList<VwProcessLib> captionMatches = byCaption ?? [];
		if (captionMatches.Count == 0) {
			return Error.NotFound("GetProcessIdFromName",
				$"Could not find process with name or caption:{nameOrCaption}");
		}
		// Caption is not unique — a multi-match is reported as an ambiguity to resolve by code.
		if (captionMatches.Count > 1) {
			string candidates = string.Join("; ",
				captionMatches.Select(p => $"'{p.Caption}' (code: {p.Name})"));
			return Error.Conflict("GetProcessIdFromName",
				$"Multiple processes match caption '{nameOrCaption}': {candidates}. "
				+ "Re-run with the exact process code.");
		}
		return captionMatches[0];
	}
}
