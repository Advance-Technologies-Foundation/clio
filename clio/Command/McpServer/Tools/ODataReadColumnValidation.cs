using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>One name the caller sent that the entity does not have, with its closest valid names.</summary>
/// <param name="Argument">Where the name was sent: select, expand, order-by or filters.</param>
/// <param name="Name">The name as the caller spelled it (sanitized for display).</param>
/// <param name="Suggestions">Up to <see cref="ODataReadColumnValidation.MaxSuggestions"/> closest valid names.</param>
public sealed record ODataUnknownMember(
	[property: JsonPropertyName("argument")] string Argument,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("suggestions")] IReadOnlyList<string> Suggestions);

/// <summary>
/// Explains an <c>odata-read</c> query that Creatio rejected as invalid-query: checks the names it used
/// (select, filter fields, expand, order-by) against the entity's OData metadata and reports each unknown
/// name with the closest valid names, instead of the opaque "could not be resolved" answer.
/// </summary>
/// <remarks>
/// It runs only AFTER the server rejected the query. The metadata is the multi-megabyte service-root
/// document and odata-read runs in a per-call worker process, where no in-process cache survives between
/// calls: a pre-flight check would download and parse that document on every read.
/// The check must never become a new way to fail: when the metadata cannot be fetched or parsed,
/// <see cref="Validate"/> returns <see langword="null"/> and the server's own rejection is returned
/// unchanged. Names that differ from a valid one only by letter case count as known.
/// </remarks>
internal static class ODataReadColumnValidation {

	/// <summary>Most closest-name suggestions reported per unknown name.</summary>
	internal const int MaxSuggestions = 3;

	/// <summary>Most unknown names reported in one rejection.</summary>
	internal const int MaxReportedUnknown = 20;

	/// <summary>Timeout of the single metadata request; on expiry the query is sent unvalidated.</summary>
	internal const int MetadataTimeoutMs = 20_000;

	private const int MinContainmentLength = 4;

	internal sealed record Reference(string Argument, string Path, bool NavigationOnly);

	/// <summary>
	/// Validates the names of a query the server rejected. Returns <see langword="null"/> when every name
	/// is known, when there is nothing to check, or when the metadata is unavailable; otherwise the
	/// failure that names the unknown members.
	/// </summary>
	internal static ODataReadResponse? Validate(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		ODataReadArgs args,
		IReadOnlyList<string>? selectColumns,
		IReadOnlyList<string>? expandColumns) {
		List<Reference> references = CollectReferences(args, selectColumns, expandColumns);
		if (references.Count == 0) {
			return null;
		}
		string entity = args.Entity.Trim();
		ODataFieldValidation.EntityShape? shape =
			ODataFieldValidation.TryGetEntityShape(client, urlBuilder, entity, MetadataTimeoutMs);
		if (shape is null) {
			return null;
		}
		List<(Reference Reference, string Name)> unknown = FindUnknown(shape, references);
		return unknown.Count == 0 ? null : BuildFailure(shape, unknown, entity);
	}

	private static List<Reference> CollectReferences(ODataReadArgs args, IReadOnlyList<string>? select,
		IReadOnlyList<string>? expand) {
		List<Reference> references = [];
		foreach (string name in select ?? []) {
			references.Add(new Reference("select", name, false));
		}
		foreach (string name in expand ?? []) {
			references.Add(new Reference("expand", name, true));
		}
		if (!string.IsNullOrWhiteSpace(args.OrderBy)) {
			foreach (string clause in args.OrderBy.Split(',')) {
				string[] parts = clause.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				if (parts.Length > 0) {
					references.Add(new Reference("order-by", parts[0], false));
				}
			}
		}
		foreach (ODataFilterCondition condition in (args.Filters?.All ?? []).Concat(args.Filters?.Any ?? [])
			.Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Field))) {
			references.Add(new Reference("filters", condition.Field.Trim(), false));
		}
		return references;
	}

	private static List<(Reference, string)> FindUnknown(ODataFieldValidation.EntityShape shape,
		List<Reference> references) =>
		references
			.Where(reference => !IsKnown(shape, reference))
			.DistinctBy(reference => (reference.Argument, reference.Path))
			.Select(reference => (reference, reference.Path))
			.ToList();

	/// <summary>
	/// A single segment must be a property or navigation of the entity (a navigation only, for expand).
	/// A two-segment path needs a navigation first and a member of its target second; deeper segments are
	/// not checked. A name that differs from a valid one only by case is treated as known.
	/// </summary>
	private static bool IsKnown(ODataFieldValidation.EntityShape shape, Reference reference) {
		// The shape's sets compare case-insensitively (see ODataFieldValidation.BuildShape).
		string[] segments = reference.Path.Split('/');
		string first = segments[0];
		if (segments.Length == 1) {
			return shape.Root.Navigations.Contains(first)
				|| (!reference.NavigationOnly && shape.Root.Properties.Contains(first));
		}
		if (!shape.Root.Navigations.Contains(first)) {
			return false;
		}
		if (!shape.NavigationTargets.TryGetValue(first, out ODataFieldValidation.TypeShape? target)) {
			return true;
		}
		// An expand path must end in a navigation; a column there is an invalid $expand.
		return target.Navigations.Contains(segments[1])
			|| (!reference.NavigationOnly && target.Properties.Contains(segments[1]));
	}

	private static ODataReadResponse BuildFailure(ODataFieldValidation.EntityShape shape,
		List<(Reference Reference, string Name)> unknown, string entity) {
		List<ODataUnknownMember> members = unknown
			.Take(MaxReportedUnknown)
			.Select(item => new ODataUnknownMember(
				item.Reference.Argument,
				McpToolArgumentSupport.DescribeCallerKey(item.Name),
				Suggest(shape, item.Reference, entity)))
			.ToList();
		string detail = string.Join("; ", members.Select(member => member.Suggestions.Count > 0
			? $"{member.Argument} '{member.Name}' (closest: {string.Join(", ", member.Suggestions)})"
			: $"{member.Argument} '{member.Name}' (no close match)"));
		string more = unknown.Count > members.Count ? $" and {unknown.Count - members.Count} more" : string.Empty;
		int columns = shape.Root.Properties.Count;
		string message =
			$"Creatio rejected the query as invalid: {unknown.Count} name(s) are not on the OData type of {entity}{more}: {detail}. " +
			$"{entity} has {columns} columns. Use one of the suggested names (a lookup is filtered through its navigation path, " +
			"for example AccountId -> Account/Id). If the column was added moments ago, wait for the OData rebuild and retry once.";
		return ODataReadResponse.Failure(message, ODataReadErrorCodes.InvalidQuery, entity: entity) with {
			UnknownMembers = members,
			ValidColumnCount = columns
		};
	}

	/// <summary>Closest valid names for an unknown one, best first, at most <see cref="MaxSuggestions"/>.</summary>
	internal static IReadOnlyList<string> Suggest(ODataFieldValidation.EntityShape shape, Reference reference,
		string entity) {
		string[] segments = reference.Path.Split('/');
		string prefix = string.Empty;
		HashSet<string> pool;
		string subject;
		bool lookupForms;
		if (segments.Length > 1 && shape.Root.Navigations.TryGetValue(segments[0], out string? navigation)
			&& shape.NavigationTargets.TryGetValue(navigation, out ODataFieldValidation.TypeShape? target)) {
			// The navigation as the metadata spells it, so a mis-cased one is corrected in the suggestion.
			prefix = navigation + "/";
			pool = reference.NavigationOnly ? [.. target.Navigations] : [.. target.Properties, .. target.Navigations];
			subject = segments[1];
			lookupForms = false;
		} else {
			pool = reference.NavigationOnly ? [.. shape.Root.Navigations] : [.. shape.Root.Properties, .. shape.Root.Navigations];
			subject = segments[0];
			lookupForms = segments.Length == 1 || segments.Length == 2;
		}
		List<(int Tier, int Distance, string Name)> ranked = RankCandidates(pool, subject, entity);
		List<string> result = [];
		if (lookupForms && prefix.Length == 0) {
			AddLookupForm(shape, reference, subject, result);
		}
		foreach ((int _, int _, string name) in ranked.OrderBy(r => r.Tier).ThenBy(r => r.Distance)
			.ThenBy(r => r.Name.Length).ThenBy(r => r.Name, StringComparer.Ordinal)) {
			string full = prefix + name;
			if (!result.Contains(full, StringComparer.Ordinal)) {
				result.Add(full);
			}
		}
		return result.Take(MaxSuggestions).ToList();
	}

	private static List<(int Tier, int Distance, string Name)> RankCandidates(HashSet<string> pool, string subject,
		string entity) {
		List<(int Tier, int Distance, string Name)> ranked = [];
		foreach (string candidate in pool) {
			(int, int)? score = Score(subject, candidate);
			string? stripped = StripEntityPrefix(subject, entity);
			if (stripped is not null && Score(stripped, candidate) is (int, int) strippedScore
				&& (score is null || strippedScore.CompareTo(score.Value) < 0)) {
				score = strippedScore;
			}
			if (score is { } value) {
				ranked.Add((value.Item1, value.Item2, candidate));
			}
		}
		return ranked;
	}

	/// <summary>The <c>&lt;Name&gt;Id</c> and <c>&lt;Name&gt;/Id</c> forms of one lookup, in either direction.</summary>
	private static void AddLookupForm(ODataFieldValidation.EntityShape shape, Reference reference, string subject,
		List<string> result) {
		string[] segments = reference.Path.Split('/');
		if (segments.Length == 2 && string.Equals(segments[1], "Id", StringComparison.OrdinalIgnoreCase)
			&& shape.Root.Properties.FirstOrDefault(name => string.Equals(name, segments[0] + "Id",
				StringComparison.OrdinalIgnoreCase)) is { } column) {
			result.Add(column);
			return;
		}
		if (segments.Length == 1 && subject.Length > 2
			&& subject.EndsWith("Id", StringComparison.OrdinalIgnoreCase)) {
			string stem = subject[..^2];
			string? navigation = shape.Root.Navigations.FirstOrDefault(
				name => string.Equals(name, stem, StringComparison.OrdinalIgnoreCase));
			if (navigation is not null) {
				result.Add(reference.Argument == "filters" || reference.Argument == "order-by"
					? navigation + "/Id"
					: navigation);
			}
		}
	}

	private static string? StripEntityPrefix(string name, string entity) =>
		name.Length > entity.Length && name.StartsWith(entity, StringComparison.OrdinalIgnoreCase)
			? name[entity.Length..]
			: null;

	/// <summary>
	/// Ranks a candidate against the unknown name: tier 0 differs by case only, tier 1 one contains the
	/// other (the shorter at least <see cref="MinContainmentLength"/> characters), tier 2 a small edit
	/// distance. <see langword="null"/> means not close enough to suggest.
	/// </summary>
	private static (int, int)? Score(string name, string candidate) {
		string left = name.ToLowerInvariant();
		string right = candidate.ToLowerInvariant();
		if (left == right) {
			return (0, 0);
		}
		(string shorter, string longer) = left.Length <= right.Length ? (left, right) : (right, left);
		if (shorter.Length >= MinContainmentLength && longer.Contains(shorter, StringComparison.Ordinal)) {
			return (1, longer.Length - shorter.Length);
		}
		int distance = EditDistance(left, right);
		return distance <= Math.Max(2, left.Length / 4) ? (2, distance) : null;
	}

	private static int EditDistance(string a, string b) {
		int[] previous = new int[b.Length + 1];
		int[] current = new int[b.Length + 1];
		for (int j = 0; j <= b.Length; j++) {
			previous[j] = j;
		}
		for (int i = 1; i <= a.Length; i++) {
			current[0] = i;
			for (int j = 1; j <= b.Length; j++) {
				int cost = a[i - 1] == b[j - 1] ? 0 : 1;
				current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
			}
			(previous, current) = (current, previous);
		}
		return previous[b.Length];
	}
}
