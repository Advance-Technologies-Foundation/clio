using System;

namespace Clio.Common.Skills;

/// <summary>
/// Validates a user-supplied <c>--repo</c> source before it flows into agent CLI
/// command lines or <c>git clone</c>.
/// </summary>
/// <remarks>
/// The <c>--repo</c> value is reachable from the MCP tool surface (so it may be
/// model-supplied), and it ends up as a <c>git</c> argument and as a marketplace
/// URL passed to agent CLIs. This rejects values that would let git's pluggable
/// transports (<c>ext::</c>/<c>fd::</c>) execute arbitrary commands, or that would
/// be parsed as an option rather than a positional.
/// </remarks>
public static class RepositorySource {
	private static readonly string[] AllowedUrlSchemes = ["https", "http", "ssh", "git", "file"];

	/// <summary>
	/// Validates a repository locator. A blank value (use the default source),
	/// a local path, an scp-style git address, or a URL with an allowed scheme are accepted.
	/// </summary>
	/// <param name="repository">The raw <c>--repo</c> value.</param>
	/// <param name="error">The validation error when the value is rejected.</param>
	/// <returns><c>true</c> when the value is allowed; otherwise <c>false</c>.</returns>
	public static bool TryValidate(string repository, out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(repository)) {
			return true;
		}

		string value = repository.Trim();

		if (value.StartsWith('-')) {
			error = $"Invalid --repo value '{repository}': must not start with '-' (it would be parsed as a command-line option).";
			return false;
		}

		// git's ext::/fd:: transports execute arbitrary commands — never allow them.
		if (value.Contains("::", StringComparison.Ordinal)) {
			error = $"Invalid --repo value '{repository}': git transport helpers (e.g. 'ext::', 'fd::') are not allowed.";
			return false;
		}

		int schemeSeparator = value.IndexOf("://", StringComparison.Ordinal);
		if (schemeSeparator > 0) {
			string scheme = value[..schemeSeparator].ToLowerInvariant();
			if (Array.IndexOf(AllowedUrlSchemes, scheme) < 0) {
				error = $"Invalid --repo value '{repository}': unsupported URL scheme '{scheme}'. "
					+ "Allowed: https, http, ssh, git, file (or a local path).";
				return false;
			}
		}

		return true;
	}
}
