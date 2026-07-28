using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.McpServer;

/// <summary>
/// Shared parser for comma-separated CLI option / environment-variable values used by the MCP
/// HTTP credential-passthrough configuration resolvers. Splits on commas, trims each entry, and
/// drops empty entries — defined once so the split/trim/drop-empty behavior cannot drift between
/// the platform-API-key and allowed-base-urls resolvers.
/// </summary>
internal static class CommaSet
{
	/// <summary>
	/// Splits <paramref name="value"/> on commas into trimmed, non-empty entries.
	/// </summary>
	/// <param name="value">The comma-separated value; may be <see langword="null"/> or blank.</param>
	/// <returns>The trimmed, non-empty entries (possibly empty). Order is preserved; no de-duplication.</returns>
	public static IReadOnlyList<string> Split(string value) {
		if (string.IsNullOrWhiteSpace(value)) {
			return [];
		}

		return value
			.Split(',')
			.Select(part => part.Trim())
			.Where(part => part.Length > 0)
			.ToList();
	}
}
