namespace Clio.Command.ProcessModel;

using System.Collections.Generic;
using System.Linq;
using Clio.CreatioModel;
using ErrorOr;

/// <summary>
/// Pure selection logic for resolving a process from <see cref="VwProcessLib"/> rows by system
/// <c>Name</c> (process code) with a fallback to display <c>Caption</c>, and — because a version family
/// shares one caption — to the ACTIVE version within that caption. Kept free of data access so it is
/// unit-testable with plain in-memory rows.
/// </summary>
/// <remarks>
/// This is the single seam both caption call sites go through (<c>ServerProcessDescriber</c> and
/// <c>ProcessModelGenerator</c>), so the active-version policy exists once. Each call site keeps its own
/// error vocabulary; only the selection lives here.
/// </remarks>
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
			return Error.NotFound("ResolveProcessByNameOrCaption",
				$"Could not find process with name or caption:{nameOrCaption}");
		}
		// Caption is not unique, and a version family shares one: every version of a process is a separate
		// schema with its own Name but the SAME Caption, so a caption matching several rows is usually ONE
		// process rather than several. Narrow to the version the runtime executes before calling it ambiguous.
		if (captionMatches.Count > 1) {
			IReadOnlyList<VwProcessLib> activeVersions = captionMatches
				.Where(p => p.IsActiveVersion == true)
				.ToList();
			if (activeVersions.Count == 1) {
				return activeVersions[0];
			}
			// Everything else is genuine ambiguity, reported over ALL matches so the caller can pick a code.
			// Several active rows are several distinct processes. NONE active means the process library could
			// not establish the flag — it is nullable for exactly that reason — and an unestablished flag is
			// no licence to pick a row.
			string candidates = string.Join("; ",
				captionMatches.Select(p => $"'{p.Caption}' (code: {p.Name})"));
			return Error.Conflict("ResolveProcessByNameOrCaption",
				$"Multiple processes match caption '{nameOrCaption}': {candidates}. "
				+ "Re-run with the exact process code.");
		}
		return captionMatches[0];
	}
}
