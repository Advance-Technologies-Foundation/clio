namespace Clio.Common.db;

/// <summary>
/// Validates database identifiers (database names, etc.) before they are interpolated into SQL
/// DDL text (e.g. <c>RESTORE/ALTER/DROP DATABASE [name]</c>), where the identifier position cannot
/// be parameterized via <c>SqlParameter</c>/<c>NpgsqlParameter</c>.
/// </summary>
internal static class SqlIdentifierGuard {

	// 1s timeout: short, bounded ASCII allow-list, no backtracking risk, but follow repo convention of always bounding Regex.
	private static readonly System.Text.RegularExpressions.Regex ValidIdentifier =
		new(@"\A[A-Za-z0-9_\-\.\$#]{1,128}\z", System.Text.RegularExpressions.RegexOptions.Compiled, System.TimeSpan.FromSeconds(1));

	/// <summary>Throws if <paramref name="name"/> is not safe to interpolate as a bracketed/quoted SQL identifier.</summary>
	internal static void EnsureValidIdentifier(string name, string paramName) {
		if (string.IsNullOrWhiteSpace(name) || !ValidIdentifier.IsMatch(name)) {
			throw new System.ArgumentException(
				$"'{name}' is not a valid SQL database identifier (letters, digits, underscore, hyphen, period, dollar, hash only, max 128 chars).",
				paramName);
		}
	}
}
