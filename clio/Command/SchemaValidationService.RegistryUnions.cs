using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Clio.Command;

/// <summary>
/// ENG-95270: the parts of the registry-driven chart walk that understand the tagged unions the registry
/// publishes for the devkit ESQ filter and expression classes. Each concrete variant pins its discriminators
/// (<c>filterType</c>, <c>expressionType</c>, <c>functionType</c>) to one value; abstract bases list every value.
/// </summary>
public static partial class SchemaValidationService {

	private static void ValidateValueAgainstUnion(
		JsonElement value, string unionExpression,
		IReadOnlyDictionary<string, JsonElement> typeDefinitions,
		string path, SchemaValidationResult result, bool report) {
		if (value.ValueKind != JsonValueKind.Object) {
			return;
		}
		UnionBranchMatch match = MatchUnionBranches(value, unionExpression, typeDefinitions);
		if (match.Compatible.Count == 0) {
			// Report only when the registry proves no branch fits: an opaque branch (undefined, an undefined alias,
			// no discriminators) or an accepting abstract base may hold a variant it does not publish (a Segment
			// filter, say), and guessing would turn valid configs into false errors.
			if (report && match.HasPinnedBranch && !match.HasOpenBranch && !match.BaseAccepts) {
				result.Errors.Add($"{path} does not match any of {unionExpression}: its discriminator fields " +
					$"({DescribeSelectors(unionExpression, typeDefinitions)}) name no known variant. {GuidanceHint(path)}");
			}
			return;
		}
		ValidateValueAgainstMostSpecificBranches(value, match.Compatible, typeDefinitions, path, result, report);
	}

	private static UnionBranchMatch MatchUnionBranches(
		JsonElement value, string unionExpression, IReadOnlyDictionary<string, JsonElement> typeDefinitions) {
		List<(string Branch, int Specificity)> compatible = [];
		bool hasOpenBranch = false;
		bool hasPinnedBranch = false;
		bool baseAccepts = false;
		foreach (string branch in FlattenUnion(unionExpression, typeDefinitions)) {
			if (!TryGetTypeFields(branch, typeDefinitions, out JsonElement fields)) {
				hasOpenBranch = true;
				continue;
			}
			IReadOnlyList<(string Name, JsonElement Values)> selectors = GetSelectors(fields);
			List<(string Name, JsonElement Values)> pinned = selectors.Where(selector => selector.Values.GetArrayLength() == 1).ToList();
			if (pinned.Count > 0) {
				hasPinnedBranch = true;
				if (SelectorsMatch(value, pinned)) {
					compatible.Add((branch, pinned.Count));
				}
			} else if (selectors.Count > 0) {
				baseAccepts |= SelectorsMatch(value, selectors);
			} else {
				hasOpenBranch = true;
			}
		}
		return new UnionBranchMatch(compatible, hasOpenBranch, hasPinnedBranch, baseAccepts);
	}

	private static void ValidateValueAgainstMostSpecificBranches(
		JsonElement value, IReadOnlyList<(string Branch, int Specificity)> compatible,
		IReadOnlyDictionary<string, JsonElement> typeDefinitions,
		string path, SchemaValidationResult result, bool report) {
		int best = compatible.Max(candidate => candidate.Specificity);
		List<string> fewestErrors = [];
		foreach ((string branch, int _) in compatible.Where(candidate => candidate.Specificity == best)) {
			var branchResult = new SchemaValidationResult { IsValid = true };
			ValidateValueAgainstType(value, branch, EmptyTypeSubstitution, typeDefinitions, path, branchResult, report);
			if (branchResult.Errors.Count == 0) {
				return;
			}
			if (fewestErrors.Count == 0 || branchResult.Errors.Count < fewestErrors.Count) {
				fewestErrors = branchResult.Errors;
			}
		}
		result.Errors.AddRange(fewestErrors);
	}

	private static bool SelectorsMatch(JsonElement value, IEnumerable<(string Name, JsonElement Values)> selectors) =>
		selectors.All(selector => value.TryGetProperty(selector.Name, out JsonElement actual)
			&& selector.Values.EnumerateArray().Any(allowed => ScalarEquals(allowed, actual)));

	private sealed record UnionBranchMatch(
		IReadOnlyList<(string Branch, int Specificity)> Compatible,
		bool HasOpenBranch, bool HasPinnedBranch, bool BaseAccepts);

	// Required fields only: a value that omits an optional pinned field is still that variant.
	private static IReadOnlyList<(string Name, JsonElement Values)> GetSelectors(JsonElement fields) {
		List<(string Name, JsonElement Values)> selectors = [];
		foreach (JsonProperty field in fields.EnumerateObject()) {
			if (field.Value.ValueKind == JsonValueKind.Object &&
			    field.Value.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.True &&
			    field.Value.TryGetProperty("values", out JsonElement values) &&
			    values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0) {
				selectors.Add((field.Name, values));
			}
		}
		return selectors;
	}

	private static string DescribeSelectors(string unionExpression, IReadOnlyDictionary<string, JsonElement> typeDefinitions) {
		var names = new SortedSet<string>(StringComparer.Ordinal);
		foreach (string branch in FlattenUnion(unionExpression, typeDefinitions)) {
			if (TryGetTypeFields(branch, typeDefinitions, out JsonElement fields)) {
				foreach ((string name, JsonElement _) in GetSelectors(fields)) {
					names.Add(name);
				}
			}
		}
		return string.Join(", ", names);
	}

	// Expands alias branches one level only: the registry never nests deeper.
	private static IEnumerable<string> FlattenUnion(string unionExpression, IReadOnlyDictionary<string, JsonElement> typeDefinitions) {
		foreach (string branch in SplitTopLevelUnion(unionExpression)) {
			string resolved = ResolveTypeAliasExpression(branch, typeDefinitions);
			IReadOnlyList<string> nested = SplitTopLevelUnion(resolved);
			foreach (string part in nested.Count > 1 ? nested : [resolved]) {
				yield return part;
			}
		}
	}

	private static IReadOnlyList<string> SplitTopLevelUnion(string expression) {
		List<string> parts = [];
		int depth = 0;
		int start = 0;
		for (int i = 0; i < expression.Length; i++) {
			switch (expression[i]) {
				case '<' or '(' or '[' or '{':
					depth++;
					break;
				case '>' or ')' or ']' or '}':
					depth--;
					break;
				case '|' when depth == 0:
					parts.Add(expression[start..i].Trim());
					start = i + 1;
					break;
			}
		}
		parts.Add(expression[start..].Trim());
		return parts.Where(part => part.Length > 0).ToList();
	}

	// IESQFilter → its union text. Bounded: an alias chain is short but could in theory cycle.
	private static string ResolveTypeAliasExpression(string typeExpression, IReadOnlyDictionary<string, JsonElement> typeDefinitions) {
		string current = typeExpression.Trim();
		for (int hop = 0; hop < 8; hop++) {
			if (!typeDefinitions.TryGetValue(current, out JsonElement definition) ||
			    definition.ValueKind != JsonValueKind.Object ||
			    definition.TryGetProperty("fields", out _) ||
			    definition.TryGetProperty("shape", out _) ||
			    definition.TryGetProperty("values", out _) ||
			    !definition.TryGetProperty("type", out JsonElement aliased) ||
			    aliased.ValueKind != JsonValueKind.String) {
				return current;
			}
			current = (aliased.GetString() ?? current).Trim();
		}
		return current;
	}

	// Null is left to the required-field check.
	private static bool ValueIsAllowed(JsonElement value, JsonElement fieldDefinition, string path, SchemaValidationResult result) {
		if (!fieldDefinition.TryGetProperty("values", out JsonElement values) || values.ValueKind != JsonValueKind.Array ||
		    values.GetArrayLength() == 0 || value.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null) {
			return true;
		}
		if (values.EnumerateArray().Any(allowed => ScalarEquals(allowed, value))) {
			return true;
		}
		result.Errors.Add($"{path} is {value.GetRawText()}, which is not one of {values.GetRawText()}. {GuidanceHint(path)}");
		return false;
	}

	private static bool PrimitiveMatches(JsonElement value, string typeExpression) =>
		value.ValueKind == JsonValueKind.Null || typeExpression switch {
			"string" => value.ValueKind == JsonValueKind.String,
			"number" => value.ValueKind == JsonValueKind.Number || TryReadNumber(value, out _),
			"boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
			_ => true
		};

	private static bool ScalarEquals(JsonElement left, JsonElement right) {
		if (TryReadNumber(left, out decimal leftNumber) && TryReadNumber(right, out decimal rightNumber)) {
			return leftNumber == rightNumber;
		}
		return left.ValueKind == right.ValueKind && left.ValueKind switch {
			JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
			JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
			_ => false
		};
	}

	// A number stored as its string counts as that number: the page runtime coerces both ways.
	private static bool TryReadNumber(JsonElement element, out decimal number) {
		number = 0;
		return element.ValueKind switch {
			JsonValueKind.Number => element.TryGetDecimal(out number),
			JsonValueKind.String => decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number),
			_ => false
		};
	}

	private static string GuidanceHint(string path) =>
		path.EndsWith(".filters.filter", StringComparison.Ordinal) || path.Contains(".filters.filter.", StringComparison.Ordinal)
			? "Call get-guidance with name 'esq-filters-frontend' for the filter contract."
			: "Call get-guidance with name 'chart-widget' for the full contract.";
}
