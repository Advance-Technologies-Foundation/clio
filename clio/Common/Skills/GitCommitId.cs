using System;

namespace Clio.Common.Skills;

/// <summary>
/// Validates and normalizes full git commit ids for the toolkit up-to-date check.
/// </summary>
/// <remarks>
/// Only a full 40-character SHA-1 id is accepted. Anything else (an abbreviated id, a ref name, a
/// SHA-256 repository id, stray text) is treated as "unknown", which makes the caller fall back to a
/// full update instead of comparing values that are not comparable.
/// </remarks>
internal static class GitCommitId {
	private const int Sha1Length = 40;

	/// <summary>
	/// Returns the lower-case commit id when <paramref name="value"/> is a full SHA-1 id; otherwise <c>null</c>.
	/// </summary>
	/// <param name="value">Candidate text; surrounding whitespace is ignored.</param>
	public static string Normalize(string value) {
		string candidate = value?.Trim();
		if (candidate is null || candidate.Length != Sha1Length) {
			return null;
		}

		foreach (char character in candidate) {
			if (!Uri.IsHexDigit(character)) {
				return null;
			}
		}

		return candidate.ToLowerInvariant();
	}
}
