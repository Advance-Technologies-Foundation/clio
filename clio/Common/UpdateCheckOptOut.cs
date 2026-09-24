using System;

namespace Clio.Common;

/// <summary>
/// The one definition of clio's "make no unattended update in this process" opt-out.
/// </summary>
/// <remarks>
/// <c>CLIO_NO_UPDATE_CHECK</c> exists so a harness — the MCP end-to-end suite above all — can suppress
/// every background update for each spawned clio from a single seam, instead of editing
/// <c>appsettings.json</c> per process. That only holds while every unattended-update path agrees on
/// what the variable means, so the predicate lives here rather than being restated per call site: a
/// widened rule (<c>no</c>, <c>off</c>, an inverted variable) that reached one path and not the other
/// would fail silently, in exactly the run the opt-out was set for.
/// </remarks>
internal static class UpdateCheckOptOut {

	/// <summary>The environment variable an operator or harness sets to suppress unattended updates.</summary>
	internal const string VariableName = "CLIO_NO_UPDATE_CHECK";

	/// <summary>
	/// Reports whether unattended updates are suppressed in this process.
	/// </summary>
	/// <remarks>
	/// Any non-empty value other than <c>false</c> or <c>0</c> suppresses, so setting the variable at all
	/// is enough; the two negative spellings exist so a shell that always defines it can still opt in.
	/// </remarks>
	/// <returns><see langword="true"/> when no unattended update may run.</returns>
	internal static bool IsSuppressed() {
		string value = Environment.GetEnvironmentVariable(VariableName);
		return !string.IsNullOrWhiteSpace(value)
			&& !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(value, "0", StringComparison.Ordinal);
	}
}
