namespace Clio.Command.ProcessModel;

using System;
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
	/// How many candidates a refusal names before it summarises the rest.
	/// </summary>
	/// <remarks>
	/// Enough for a caller to recognise the process they meant; the refusal's job is to make them re-run with a
	/// code, not to reproduce the family.
	/// </remarks>
	internal const int CandidatesNamed = 5;

	/// <summary>
	/// Picks the resolved process row, or returns a typed error.
	/// </summary>
	/// <param name="nameOrCaption">The value the caller passed (process code or display caption).</param>
	/// <param name="byName">The exact <c>Name</c> match, or <c>null</c> when none.</param>
	/// <param name="byCaption">Rows matching the value by <c>Caption</c> (used only when there is no name match).</param>
	/// <returns>
	/// The matched row; <see cref="ErrorType.NotFound"/> when nothing matches; or
	/// <see cref="ErrorType.Conflict"/> for any of the five shapes it refuses — the candidates span several
	/// distinct processes; they are one family flagging more than one active version; they are one family for
	/// which no active version was established; they are one family whose key the view did not establish (so
	/// nothing proves they belong together); or the only match is the one the view flags explicitly NOT active.
	/// An UNESTABLISHED active-version flag on a lone match deliberately does NOT refuse: it is no statement
	/// about the row, and refusing on it would break resolution for a process whose package does not resolve.
	/// </returns>
	/// <remarks>
	/// The last two refusals are new with the family-aware policy: a lone caption match that used to resolve
	/// unconditionally can now fail.
	/// </remarks>
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
		//
		// The family key decides, not the active-flag count. Counting flags looks equivalent and is not: a set
		// of one family's active version PLUS a row from a different process carries exactly one flagged row,
		// and narrowing on the count alone would answer for the first process while silently dropping the
		// second. That set is not exotic — the other row is flagged false whenever it is a family root whose
		// active member was renamed away from this caption, and null whenever its package does not resolve,
		// which is why the column is nullable at all. VersionParentUId is COALESCE(parent.UId, own.UId), so a
		// single distinct value across the candidates is what actually establishes "these rows are one process".
		//
		// The single value must also be a REAL key. VersionParentUId is the one column this feature left
		// non-nullable, so a view NULL arrives as Guid.Empty — and two candidates from DIFFERENT processes that
		// both defaulted would then share one distinct value and pass as a family, which is verbatim the failure
		// the paragraph above says the family key exists to prevent. ProcessVersionLibReader.Read guards the
		// identical value on the identical column for the same reason.
		List<Guid> families = captionMatches
			.Select(p => p.VersionParentUId)
			.Distinct()
			.ToList();
		bool oneFamily = families.Count == 1 && families[0] != Guid.Empty;
		IReadOnlyList<VwProcessLib> activeVersions = captionMatches
			.Where(p => p.IsActiveVersion == true)
			.ToList();
		if (oneFamily && activeVersions.Count == 1) {
			return activeVersions[0];
		}
		// A lone match is returned unless the view says outright that it is NOT the version that runs. An
		// UNESTABLISHED flag is no statement about the row, so refusing on it would break resolution for a
		// process whose package does not resolve; an explicit false IS a statement, and returning it would
		// answer for a graph nobody runs on the three surfaces that carry no version fields to reveal it.
		if (captionMatches.Count == 1 && captionMatches[0].IsActiveVersion != false) {
			return captionMatches[0];
		}
		return Error.Conflict("ResolveProcessByNameOrCaption",
			RefusalReason(nameOrCaption, captionMatches, oneFamily, activeVersions.Count,
				familyKeyUnestablished: families.Contains(Guid.Empty))
			+ " Re-run with the exact process code.");
	}

	/// <summary>
	/// Why a caption could not be resolved, phrased for the shape that actually blocked it.
	/// </summary>
	/// <remarks>
	/// One message for every refusal read "Multiple processes match caption" even when the candidates were
	/// ONE family whose active version the library could not establish — which sends the reader to look for
	/// a second process that does not exist.
	/// </remarks>
	private static string RefusalReason(string nameOrCaption, IReadOnlyList<VwProcessLib> captionMatches,
		bool oneFamily, int activeCount, bool familyKeyUnestablished) {
		string candidates = Candidates(captionMatches);
		if (familyKeyUnestablished) {
			return $"Caption '{nameOrCaption}' matches {captionMatches.Count} schemas for which the process "
				+ "library established no version family key, so nothing shows whether they are one process or "
				+ $"several: {candidates}.";
		}
		if (!oneFamily) {
			return $"Multiple processes match caption '{nameOrCaption}': {candidates}.";
		}
		if (activeCount > 1) {
			return $"Caption '{nameOrCaption}' belongs to one process family, but the process library flags "
				+ $"{activeCount} of its versions as active: {candidates}.";
		}
		if (captionMatches.Count == 1) {
			return $"Caption '{nameOrCaption}' matches only {candidates}, which the process library reports "
				+ "is NOT the active version of its family.";
		}
		return $"Caption '{nameOrCaption}' belongs to one process family, but the process library established "
			+ $"no active version for it: {candidates}.";
	}

	/// <summary>
	/// The candidate codes a caller needs in order to pick one, bounded.
	/// </summary>
	/// <remarks>
	/// The population this feature targets is a heavily versioned family, and this text is returned into an MCP
	/// response as agent context, so an unbounded join grows the refusal with the family. The tail states the
	/// number withheld: a silently shortened list would read as the complete candidate set.
	/// </remarks>
	private static string Candidates(IReadOnlyList<VwProcessLib> captionMatches) {
		string listed = string.Join("; ",
			captionMatches.Take(CandidatesNamed).Select(p => $"'{p.Caption}' (code: {p.Name})"));
		return captionMatches.Count <= CandidatesNamed
			? listed
			: $"{listed}; and {captionMatches.Count - CandidatesNamed} more";
	}
}
