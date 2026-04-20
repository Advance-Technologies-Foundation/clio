using System;
using System.Collections.Generic;
using System.Linq;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Parses standard Creatio validator parameter contracts from the validators guide text.
/// </summary>
internal static class StandardValidatorContractParser {

	private static IReadOnlyDictionary<string, string[]>? _cachedContracts;

	/// <summary>
	/// Returns the standard validator parameter contracts extracted from the validators guidance resource.
	/// The result is cached after the first parse because the guide text is constant.
	/// </summary>
	internal static IReadOnlyDictionary<string, string[]> GetContracts() =>
		_cachedContracts ??= BuildContracts(
			((TextResourceContents)new PageSchemaValidatorsGuidanceResource().GetGuide()).Text);

	private static IReadOnlyDictionary<string, string[]> BuildContracts(string guideText) {
		var contracts = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
		string[] lines = guideText.Split(["\r\n", "\n"], StringSplitOptions.None);
		foreach (string rawLine in lines) {
			string line = rawLine.Trim();
			if (!line.StartsWith("| ", StringComparison.Ordinal) || !line.Contains("`crt.", StringComparison.Ordinal)) {
				continue;
			}
			string[] cells = line.Split('|')
				.Select(cell => cell.Trim())
				.Where(cell => cell.Length > 0)
				.ToArray();
			if (cells.Length < 4) {
				continue;
			}
			string validatorType = ExtractBacktickValue(cells[1]);
			if (string.IsNullOrWhiteSpace(validatorType) ||
			    !validatorType.StartsWith("crt.", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			string paramsCell = cells[2];
			contracts[validatorType] = paramsCell.Equals("none", StringComparison.OrdinalIgnoreCase)
				? []
				: ExtractBacktickValues(paramsCell).ToArray();
		}
		return contracts;
	}

	private static string ExtractBacktickValue(string cell) {
		int start = cell.IndexOf('`');
		if (start < 0) {
			return string.Empty;
		}
		int end = cell.IndexOf('`', start + 1);
		if (end <= start) {
			return string.Empty;
		}
		return cell.Substring(start + 1, end - start - 1);
	}

	private static IEnumerable<string> ExtractBacktickValues(string cell) {
		int startIndex = 0;
		while (startIndex < cell.Length) {
			int start = cell.IndexOf('`', startIndex);
			if (start < 0) {
				yield break;
			}
			int end = cell.IndexOf('`', start + 1);
			if (end <= start) {
				yield break;
			}
			string value = cell.Substring(start + 1, end - start - 1);
			if (!string.IsNullOrWhiteSpace(value)) {
				yield return value;
			}
			startIndex = end + 1;
		}
	}
}
