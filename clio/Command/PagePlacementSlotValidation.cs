namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>
/// Finds <c>viewConfigDiff</c> placements (<c>insert</c> / <c>move</c>) that name a <c>parentName</c> but no
/// <c>propertyName</c> slot, on web and mobile page bodies alike (GH-1752).
/// </summary>
/// <remarks>
/// <para>
/// The Creatio differ places an inserted element, and a moved one (a move is applied as remove + insert), into
/// <c>parent[propertyName]</c>. With a <c>parentName</c> and no <c>propertyName</c> it indexes an undefined slot
/// and throws <c>Item "&lt;parent&gt;" is not a container for other items</c>, so the platform cannot render the
/// page and clio's own resolver (<see cref="JsonDiffApplier"/>) cannot resolve it for <c>get-page</c>. Without a
/// <c>parentName</c> the element goes to the root list and <c>propertyName</c> is never read, so that shape is
/// left alone.
/// </para>
/// <para>
/// <c>set</c> is deliberately out of scope: when its element already exists it reuses that element's slot, which a
/// check that reads one body cannot see. Verbs are matched exact-case because the differ's verb switch is
/// exact-case and discards anything else.
/// </para>
/// </remarks>
internal static class PagePlacementSlotValidation {
	private const string ViewConfigDiffProperty = "viewConfigDiff";
	private const string OperationKey = "operation";
	private const string NameKey = "name";
	private const string ParentNameKey = "parentName";
	private const string PropertyNameKey = "propertyName";
	private const string NameToKey = "nameTo";
	private static readonly HashSet<string> PlacementOperations =
		new(StringComparer.Ordinal) { PageViewConfigDiffVerbs.Insert, PageViewConfigDiffVerbs.Move };

	/// <summary>
	/// Validates a raw page body (web AMD body or mobile JSON body). Returns one error per placement without a
	/// slot. A body whose diff cannot be read is reported valid: syntax and section shape belong to other
	/// validators, and this check must never block a save on a parse failure of its own.
	/// </summary>
	internal static SchemaValidationResult Validate(string body) {
		var result = new SchemaValidationResult { IsValid = true };
		JArray diff;
		try {
			diff = ReadViewConfigDiff(body);
		} catch (Exception ex) when (ex is not OperationCanceledException) {
			return result;
		}
		foreach (SlotlessPlacement placement in Find(diff)) {
			result.IsValid = false;
			result.Errors.Add(DescribeError(placement));
		}
		return result;
	}

	/// <summary>
	/// Lists the placements without a slot in one <c>viewConfigDiff</c> operation array. A <c>move</c> whose
	/// element the same array removes is skipped: the differ drops such a move before applying anything
	/// (<c>FilterMoveOperation</c>), the same exemption <see cref="PageParentNameValidation"/> makes.
	/// </summary>
	internal static IReadOnlyList<SlotlessPlacement> Find(JToken viewConfigDiff) {
		if (viewConfigDiff is not JArray operations) {
			return [];
		}
		HashSet<string> removed = operations.OfType<JObject>()
			.Where(op => StringValue(op[OperationKey]) == PageViewConfigDiffVerbs.Remove && op["properties"] is not JArray)
			.Select(op => StringValue(op[NameKey]))
			.Where(name => name is not null)
			.ToHashSet(StringComparer.Ordinal);
		var found = new List<SlotlessPlacement>();
		for (int index = 0; index < operations.Count; index++) {
			if (operations[index] is JObject operation && IsSlotless(operation)
				&& !(StringValue(operation[OperationKey]) == PageViewConfigDiffVerbs.Move
					&& removed.Contains(StringValue(operation[NameKey]) ?? string.Empty))) {
				found.Add(new SlotlessPlacement(
					index,
					operation.Value<string>(OperationKey),
					StringValue(operation[NameKey]),
					StringValue(operation[ParentNameKey]),
					StringValue(operation[NameToKey])));
			}
		}
		return found;
	}

	/// <summary>Whether one operation is an <c>insert</c> / <c>move</c> with a parent but no slot.</summary>
	internal static bool IsSlotless(JObject operation) =>
		StringValue(operation[OperationKey]) is { } verb
		&& PlacementOperations.Contains(verb)
		&& !string.IsNullOrEmpty(StringValue(operation[ParentNameKey]))
		&& string.IsNullOrEmpty(StringValue(operation[PropertyNameKey]));

	/// <summary>The blocking diagnostic for a body that would save such a placement.</summary>
	internal static string DescribeError(SlotlessPlacement placement) =>
		$"viewConfigDiff[{placement.Index}] ({placement.Operation} '{placement.Name}') names parentName "
		+ $"'{placement.ParentName}' but no propertyName. The Creatio differ places the element into the parent's "
		+ $"propertyName slot, so without it the platform rejects the page (\"{NotContainerMessage(placement)}\") "
		+ "and get-page can no longer resolve it. Add the parent's slot to this operation, e.g. "
		+ "\"propertyName\": \"items\".";

	/// <summary>
	/// The advisory <c>get-page</c> reports when it had to resolve a page without such a placement.
	/// </summary>
	/// <param name="schemaName">Name of the schema whose body carries the operation.</param>
	/// <param name="packageName">Package of that schema; replacing schemas share one name, so it tells the layers
	/// apart.</param>
	/// <param name="placement">The skipped operation.</param>
	internal static string DescribeSkipped(string schemaName, string packageName, SlotlessPlacement placement) =>
		$"Schema '{schemaName}' (package '{packageName}') viewConfigDiff[{placement.Index}] ({placement.Operation} "
		+ $"'{placement.Name}') names parentName '{placement.ParentName}' but no propertyName. The Creatio differ "
		+ $"rejects such an operation (\"{NotContainerMessage(placement)}\"), so the page cannot render; get-page "
		+ "resolved the bundle WITHOUT it. Fix the operation in that schema's body (add the parent's slot, e.g. "
		+ "\"propertyName\": \"items\", or remove the operation) and save it with update-page.";

	/// <summary>
	/// Whether a differ error is the not-a-container rejection this placement causes (the differ names the
	/// placement's <c>nameTo</c> when it is set, otherwise its <c>parentName</c>).
	/// </summary>
	internal static bool IsRejectionOf(SlotlessPlacement placement, string differError) =>
		string.Equals(differError, NotContainerMessage(placement), StringComparison.Ordinal);

	private static string NotContainerMessage(SlotlessPlacement placement) =>
		string.Format(System.Globalization.CultureInfo.InvariantCulture,
			JsonDiffApplierResources.NotContainerItemInsertException,
			string.IsNullOrEmpty(placement.NameTo) ? placement.ParentName : placement.NameTo);

	private static JArray ReadViewConfigDiff(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			return [];
		}
		if (PageSchemaTypeExtensions.FromBody(body) == PageSchemaType.Mobile) {
			return JObject.Parse(body)[ViewConfigDiffProperty] as JArray ?? [];
		}
		return PageParentNameValidation.ReadDiff(body);
	}

	// Only a string is a usable name or slot for the differ; any other value kind is treated as missing.
	private static string StringValue(JToken token) =>
		token is { Type: JTokenType.String } ? token.Value<string>() : null;
}

/// <summary>One <c>insert</c> / <c>move</c> that names a parent but no slot.</summary>
/// <param name="Index">Position of the operation in its <c>viewConfigDiff</c> array.</param>
/// <param name="Operation">The operation verb (<c>insert</c> or <c>move</c>).</param>
/// <param name="Name">The element the operation places.</param>
/// <param name="ParentName">The parent it names.</param>
/// <param name="NameTo">The operation's <c>nameTo</c>, when it is a string; the differ names it instead of
/// <paramref name="ParentName"/> in its rejection.</param>
internal sealed record SlotlessPlacement(int Index, string Operation, string Name, string ParentName, string NameTo = null);
