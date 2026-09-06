using System;
using System.Text.RegularExpressions;

namespace Clio.Common;

#region Class: ClioIdentifier

/// <summary>
/// The one identifier rule clio applies to names it concatenates into generated C# code and into
/// directory names - package names and schema-name prefixes alike.
/// </summary>
/// <remarks>
/// Kept in a single place on purpose: a package name and a schema-name prefix end up in the same
/// generated string, so two copies of the rule can only ever disagree. Callers add their own extra
/// constraints (a length cap, a reserved value) on top of this predicate rather than restating it.
/// </remarks>
internal static class ClioIdentifier {

	#region Fields: Private

	private static readonly Regex IdentifierPattern = new("\\A[A-Za-z_][A-Za-z0-9_]*\\z",
		RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	#endregion

	#region Methods: Internal

	/// <summary>
	/// Tells whether <paramref name="value"/> is a non-empty fragment usable inside a C# identifier.
	/// </summary>
	/// <param name="value">Candidate value.</param>
	/// <returns><see langword="true"/> when the value starts with a letter or underscore and continues
	/// with letters, digits or underscores only.</returns>
	internal static bool IsIdentifierFragment(string value) =>
		!string.IsNullOrEmpty(value) && IdentifierPattern.IsMatch(value);

	#endregion

}

#endregion
