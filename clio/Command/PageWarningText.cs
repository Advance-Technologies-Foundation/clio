namespace Clio.Command;

using System.Linq;

/// <summary>
/// Joins the optional warning sentences of a page read into the one <c>warning</c> text a response carries.
/// </summary>
internal static class PageWarningText {
	/// <summary>
	/// Joins the non-blank warnings with a space.
	/// </summary>
	/// <param name="warnings">Warning sentences; blank entries are skipped.</param>
	/// <returns>The joined text, or <c>null</c> when every entry is blank.</returns>
	internal static string Join(params string[] warnings) {
		string joined = string.Join(" ", warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)));
		return joined.Length == 0 ? null : joined;
	}
}
