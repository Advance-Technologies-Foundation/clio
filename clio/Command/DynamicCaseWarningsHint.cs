using System.Collections.Generic;

namespace Clio.Command;

/// <summary>
/// The sentence a dynamic-case command appends to its verdict line when the result carries warnings. The warnings
/// themselves travel only inside the result document, so the verdict is the one top-level line that says they exist.
/// </summary>
internal static class DynamicCaseWarningsHint {

	/// <summary>The suffix for a verdict line, or an empty string when the result has no warning.</summary>
	/// <param name="warnings">The result document's <c>warnings</c>; blank entries are not counted.</param>
	/// <param name="beforeAnyWrite"><see langword="true"/> for a command that writes, whose warnings must be shown
	/// before the next write; <see langword="false"/> for a read.</param>
	internal static string For(IReadOnlyCollection<string> warnings, bool beforeAnyWrite) {
		int count = 0;
		foreach (string warning in warnings ?? []) {
			if (!string.IsNullOrWhiteSpace(warning)) {
				count++;
			}
		}
		if (count == 0) {
			return string.Empty;
		}
		string subject = count == 1 ? "1 warning" : $"{count} warnings";
		string pronoun = count == 1 ? "it" : "them";
		return $" {subject} in the result: show {pronoun} to the user"
			+ (beforeAnyWrite ? " before any write." : ".");
	}
}
