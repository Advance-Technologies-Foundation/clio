using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.McpServer.Tools;

internal static class ModelingGuardrails {
	private static readonly string[] BaseLookupInheritedColumns = ["Name", "Description"];

	internal static string? ResolveCanonicalMainEntityName(
		string packageName,
		IReadOnlyList<ApplicationEntityInfoResult> entities) {
		if (string.IsNullOrWhiteSpace(packageName) || entities is null || entities.Count == 0) {
			return null;
		}

		return entities.FirstOrDefault(entity =>
			string.Equals(entity.Name, packageName, StringComparison.OrdinalIgnoreCase))?.Name;
	}

	internal static void EnsureLookupColumnsDoNotShadowInheritedBaseLookupColumns(
		IEnumerable<CreateEntitySchemaColumnArgs>? columns) {
		// ResolveName(), not Name: `column-name` is the contract's canonical column identity field, so a
		// payload that spells the column that way must still hit this guardrail. Reading the raw `Name` let
		// {"column-name":"Name"} slip through the IsNullOrWhiteSpace filter and reach the remote creator,
		// which has no create-path equivalent of the modify path's EnsureNameIsUnique (PR #984 review).
		string[] invalidColumns = columns?
			.Select(column => column.ResolveName()?.Trim())
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Where(name => BaseLookupInheritedColumns.Contains(name!, StringComparer.OrdinalIgnoreCase))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray() ?? [];
		if (invalidColumns.Length == 0) {
			return;
		}

		throw new ArgumentException(
			$"create-lookup inherits BaseLookup columns. Do not add inherited columns: {string.Join(", ", invalidColumns)}.");
	}
}
