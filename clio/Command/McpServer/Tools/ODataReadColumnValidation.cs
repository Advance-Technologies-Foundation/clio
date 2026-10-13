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
/// Explains an <c>odata-read</c> or <c>odata-read-to-file</c> query that Creatio rejected as invalid-query: checks the names it used
/// (select, filter fields, expand, order-by) against the entity's OData metadata and reports each unknown
/// name with the closest valid names, instead of the opaque "could not be resolved" answer.
/// </summary>
/// <remarks>
/// It runs only AFTER the server rejected the query. The metadata is the multi-megabyte service-root
/// document and odata-read runs in a per-call worker process, where no in-process cache survives between
/// calls: a pre-flight check would download and parse that document on every read.
/// Every rejected query costs exactly one fetch of that document (no cache, no retry), bounded by
/// <see cref="MetadataTimeoutMs"/>. OData v4 has no per-entity metadata endpoint, and Creatio answers 404
/// to <c>odata/{Entity}/$metadata</c>, so the service root is the only source.
/// The check must never become a new way to fail: when the metadata cannot be fetched or parsed,
/// <see cref="Validate"/> returns <see langword="null"/> and the server's own rejection is returned
/// unchanged. Names that differ from a valid one only by letter case count as known.
/// </remarks>
internal static class ODataReadColumnValidation {

	/// <summary>Most closest-name suggestions reported per unknown name.</summary>
	internal const int MaxSuggestions = 3;

	/// <summary>Most unknown names reported in one rejection.</summary>
	internal const int MaxReportedUnknown = 20;

	/// <summary>
	/// Timeout of the single metadata request. On expiry the server's rejection is returned unchanged, so this
	/// bounds how much longer an already-failed read can take; the measured service-root document (~2.8 MB on a
	/// .NET Framework stand) arrives in about a second.
	/// </summary>
	internal const int MetadataTimeoutMs = ODataFieldValidation.OptionalMetadataTimeoutMs;

	private const int MinContainmentLength = 4;

	private const int MinPrefixLength = 3;

	internal sealed record Reference(string Argument, string Path, bool NavigationOnly);

	/// <summary>What the metadata says about the names of a rejected query.</summary>
	/// <param name="Text">Locally authored sentence naming each unknown name and its closest valid names.</param>
	/// <param name="Members">The unknown names, at most <see cref="MaxReportedUnknown"/>.</param>
	/// <param name="ValidColumnCount">Number of columns the entity has.</param>
	internal sealed record Explanation(string Text, IReadOnlyList<ODataUnknownMember> Members, int ValidColumnCount);

	/// <summary>
	/// Adds the unknown-name explanation to an <c>invalid-query</c> rejection. The rejection's own message is
	/// kept and the explanation is APPENDED to it: the request can be rejected for another reason too (a
	/// binary column in select, mismatched operand types), and that reason and the list of names sent must
	/// survive. Any other response, and a rejection the metadata cannot explain, is returned unchanged.
	/// Shared by odata-read and odata-read-to-file, so both read paths explain a rejection the same way.
	/// </summary>
	internal static ODataReadResponse Explain(
		ODataReadResponse response,
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		ODataReadArgs args,
		IReadOnlyList<string>? selectColumns,
		IReadOnlyList<string>? expandColumns) {
		if (response.Success || response.ErrorCode != ODataReadErrorCodes.InvalidQuery) {
			return response;
		}
		Explanation? explanation = Validate(client, urlBuilder, args, selectColumns, expandColumns);
		if (explanation is null) {
			return response;
		}
		// An explanation without unknown names (only the plain-column correction) leaves both structured fields absent.
		bool hasUnknown = explanation.Members.Count > 0;
		return response with {
			Error = $"{response.Error} {explanation.Text}",
			UnknownMembers = hasUnknown ? explanation.Members : null,
			ValidColumnCount = hasUnknown ? explanation.ValidColumnCount : null
		};
	}

	/// <summary>
	/// Validates the names of a query the server rejected. Returns <see langword="null"/> when every name
	/// is known and none is a plain column the rejection's lookup hint would misrewrite, when there is nothing to
	/// check, or when the metadata is unavailable; otherwise the explanation that names the unknown members
	/// (with no members when only the plain-column correction applies).
	/// </summary>
	internal static Explanation? Validate(
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
		List<string> plainIdColumns = PlainIdColumns(shape, references);
		if (unknown.Count == 0 && plainIdColumns.Count == 0) {
			return null;
		}
		return BuildExplanation(shape, unknown, plainIdColumns, entity);
	}

	/// <summary>
	/// Filter names that end in Id but are ordinary columns of the entity (no navigation of the
	/// same stem, so <c>ExternalId</c> rather than <c>CreatedById</c>). The rejection's name-only lookup hint
	/// tells the caller to rewrite each of them as <c>External/Id</c>; the metadata proves that wrong, and the
	/// explanation says so, because it is appended to that hint rather than replacing it.
	/// </summary>
	private static List<string> PlainIdColumns(ODataFieldValidation.EntityShape shape, List<Reference> references) =>
		references
			//Filters only: the rejection's lookup hint is built from filter fields, never from order-by.
			.Where(reference => reference.Argument == "filters"
				&& !reference.Path.Contains('/', StringComparison.Ordinal)
				&& reference.Path.Length > 2
				&& ODataKeyFormatter.IsIdish(reference.Path)
				&& shape.Root.Properties.Contains(reference.Path)
				&& !NeedsNavigationPath(shape.Root, reference.Path))
			.Select(reference => reference.Path)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

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
	/// not checked. A name that differs from a valid one only by case is treated as known. In a filter or
	/// order-by the last checked segment must also be usable there: a lookup's scalar column
	/// (<c>ActivityCategoryId</c>) or a bare navigation (<c>ActivityCategory</c>) is rejected by Creatio in
	/// those places, where only the navigation path <c>ActivityCategory/Id</c> works.
	/// </summary>
	private static bool IsKnown(ODataFieldValidation.EntityShape shape, Reference reference) =>
		ExistsOnType(shape, reference) && !NeedsNavigationPath(shape, reference);

	private static bool ExistsOnType(ODataFieldValidation.EntityShape shape, Reference reference) {
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

	/// <summary>
	/// Whether an existing name is used in a filter or order-by in a form Creatio rejects there (a lookup's
	/// scalar column or a bare navigation), so it has to be sent as <c>X/Id</c>.
	/// </summary>
	/// <remarks>
	/// A scalar column counts as a lookup only when the type ALSO declares the navigation named without the
	/// Id suffix (<c>CreatedById</c> beside <c>CreatedBy</c>); a declared <c>*Id</c> column without one
	/// (<c>ExternalId</c>) is an ordinary column and is never reported. Measured on a .NET Framework stand:
	/// <c>$select=Id,CreatedById</c> is answered 200, while <c>$filter=CreatedById eq ...</c> and
	/// <c>$orderby=CreatedById</c> are both rejected ("Column by path CreatedById not found in schema
	/// Activity") and <c>$filter=CreatedBy/Id eq ...</c> is answered 200 - so the metadata declaring the
	/// scalar column as a Property does not make it usable in a filter or order-by
	/// (docs/knowledge/platform/odata-filter-addresses-lookup-columns-by-navigation-path.md).
	/// </remarks>
	private static bool NeedsNavigationPath(ODataFieldValidation.EntityShape shape, Reference reference) {
		if (!IsPathOnlyContext(reference)) {
			return false;
		}
		string[] segments = reference.Path.Split('/');
		if (segments.Length == 1) {
			return NeedsNavigationPath(shape.Root, segments[0]);
		}
		return segments.Length == 2
			&& shape.NavigationTargets.TryGetValue(segments[0], out ODataFieldValidation.TypeShape? target)
			&& NeedsNavigationPath(target, segments[1]);
	}

	private static bool NeedsNavigationPath(ODataFieldValidation.TypeShape type, string name) =>
		!string.Equals(PathOnlyForm(type, name), name, StringComparison.OrdinalIgnoreCase);

	/// <summary>Filters and order-by address a lookup only through its navigation path (<c>X/Id</c>).</summary>
	private static bool IsPathOnlyContext(Reference reference) =>
		reference.Argument is "filters" or "order-by";

	/// <summary>
	/// The form of a member that a filter or order-by accepts: <c>ActivityCategory/Id</c> for the lookup
	/// column <c>ActivityCategoryId</c> and for the bare navigation <c>ActivityCategory</c>; any other name
	/// unchanged. Names are returned as the metadata spells them.
	/// </summary>
	private static string PathOnlyForm(ODataFieldValidation.TypeShape type, string name) {
		if (name.Length > 2 && name.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
			&& type.Properties.Contains(name)
			&& type.Navigations.TryGetValue(name[..^2], out string? lookup)) {
			return lookup + "/Id";
		}
		return !type.Properties.Contains(name) && type.Navigations.TryGetValue(name, out string? navigation)
			? navigation + "/Id"
			: name;
	}

	private static Explanation BuildExplanation(ODataFieldValidation.EntityShape shape,
		List<(Reference Reference, string Name)> unknown, List<string> plainIdColumns, string entity) {
		List<(ODataUnknownMember Member, bool WrongForm)> items = unknown
			.Take(MaxReportedUnknown)
			.Select(item => (new ODataUnknownMember(
				item.Reference.Argument,
				McpToolArgumentSupport.DescribeCallerKey(item.Name),
				Suggest(shape, item.Reference, entity)), ExistsOnType(shape, item.Reference)))
			.ToList();
		string detail = string.Join("; ", items.Select(item => Describe(item.Member, item.WrongForm)));
		string more = unknown.Count > items.Count ? $" and {unknown.Count - items.Count} more" : string.Empty;
		int columns = shape.Root.Properties.Count;
		string text = $"Checked against the OData metadata of {entity}:";
		if (unknown.Count > 0) {
			text += $" {unknown.Count} name(s) cannot be used as sent{more}: {detail}. " +
				$"{entity} has {columns} columns. Use one of the suggested names (a lookup is filtered and ordered through its " +
				"navigation path, for example AccountId -> Account/Id).";
		}
		if (plainIdColumns.Count > 0) {
			string names = string.Join(", ", plainIdColumns.Select(name => $"'{McpToolArgumentSupport.DescribeCallerKey(name)}'"));
			text += $" {names} {(plainIdColumns.Count == 1 ? "is an ordinary column" : "are ordinary columns")} of {entity}, " +
				"not a lookup: keep the name as sent and ignore the navigation-path rewrite suggested for it above" +
				(unknown.Count == 0 ? "; the rejection has another cause." : ".");
		}
		return new Explanation(text, items.Select(item => item.Member).ToList(), columns);
	}

	private static string Describe(ODataUnknownMember member, bool wrongForm) {
		if (wrongForm) {
			string use = member.Suggestions.Count > 0 ? member.Suggestions[0] : "its navigation path";
			return $"{member.Argument} '{member.Name}' (a lookup on the type, but not usable in {member.Argument}: use {use})";
		}
		return member.Suggestions.Count > 0
			? $"{member.Argument} '{member.Name}' (not on the type; closest: {string.Join(", ", member.Suggestions)})"
			: $"{member.Argument} '{member.Name}' (not on the type; no close match)";
	}

	/// <summary>
	/// Closest valid names for an unknown one, best first, at most <see cref="MaxSuggestions"/>. In a filter
	/// or order-by a lookup is suggested as its navigation path (<c>ActivityCategory/Id</c>), never as the
	/// scalar column or the bare navigation, because those are what Creatio rejects there.
	/// </summary>
	internal static IReadOnlyList<string> Suggest(ODataFieldValidation.EntityShape shape, Reference reference,
		string entity) {
		string[] segments = reference.Path.Split('/');
		List<string> result = [];
		if (segments.Length > 1 && shape.Root.Navigations.TryGetValue(segments[0], out string? navigation)
			&& shape.NavigationTargets.TryGetValue(navigation, out ODataFieldValidation.TypeShape? target)) {
			SuggestBehindNavigation(navigation, target, segments, reference, entity, result);
		} else if (segments.Length > 1) {
			SuggestForUnknownNavigation(shape, segments, reference, entity, result);
		} else {
			SuggestAtRoot(shape, segments[0], reference, entity, result);
		}
		return result;
	}

	/// <summary>A member misspelled behind a known navigation; the navigation is spelled as the metadata spells it.</summary>
	private static void SuggestBehindNavigation(string navigation, ODataFieldValidation.TypeShape target,
		string[] segments, Reference reference, string entity, List<string> result) {
		bool pathOnly = IsPathOnlyContext(reference) && segments.Length == 2;
		AddRanked(result, RankCandidates(MembersOf(target, reference.NavigationOnly), segments[1], entity),
			name => navigation + "/" + (pathOnly ? PathOnlyForm(target, name) : name));
	}

	/// <summary>
	/// The first segment is not a navigation: correct it and keep the rest of the path, so Acount/Name becomes
	/// Account/Name rather than the bare Account.
	/// </summary>
	private static void SuggestForUnknownNavigation(ODataFieldValidation.EntityShape shape, string[] segments,
		Reference reference, string entity, List<string> result) {
		bool pathOnly = IsPathOnlyContext(reference);
		if (segments.Length == 2 && !pathOnly && !reference.NavigationOnly) {
			AddScalarFormOfIdPath(shape, segments, result);
		}
		string tail = string.Join("/", segments[1..]);
		string bareFallback = pathOnly ? "/Id" : string.Empty;
		AddRanked(result, RankCandidates([.. shape.Root.Navigations], segments[0], entity),
			name => CorrectTail(shape, name, tail, reference.NavigationOnly, pathOnly && segments.Length == 2)
				is { } corrected
				? name + "/" + corrected
				: name + bareFallback);
	}

	/// <summary>A single unknown name: in a filter or order-by a lookup becomes its navigation path.</summary>
	private static void SuggestAtRoot(ODataFieldValidation.EntityShape shape, string subject, Reference reference,
		string entity, List<string> result) {
		bool pathOnly = IsPathOnlyContext(reference);
		if (pathOnly && subject.Length > 2 && subject.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
			&& shape.Root.Navigations.TryGetValue(subject[..^2], out string? stem)) {
			result.Add(stem + "/Id");
		}
		AddRanked(result, RankCandidates(MembersOf(shape.Root, reference.NavigationOnly), subject, entity),
			name => pathOnly ? PathOnlyForm(shape.Root, name) : name);
	}

	private static HashSet<string> MembersOf(ODataFieldValidation.TypeShape type, bool navigationOnly) =>
		navigationOnly ? [.. type.Navigations] : [.. type.Properties, .. type.Navigations];

	private static IEnumerable<string> Ordered(List<(int Tier, int Distance, string Name)> ranked) =>
		ranked.OrderBy(r => r.Tier).ThenBy(r => r.Distance).ThenBy(r => r.Name.Length)
			.ThenBy(r => r.Name, StringComparer.Ordinal).Select(r => r.Name);

	/// <summary>Adds formatted candidates best first until <see cref="MaxSuggestions"/> distinct names are held.</summary>
	private static void AddRanked(List<string> result, List<(int Tier, int Distance, string Name)> ranked,
		Func<string, string> format) {
		foreach (string name in Ordered(ranked)) {
			if (result.Count >= MaxSuggestions) {
				return;
			}
			string full = format(name);
			if (!result.Contains(full, StringComparer.Ordinal)) {
				result.Add(full);
			}
		}
	}

	/// <summary>
	/// The path after a corrected navigation: its first member as the target spells it, or the closest
	/// member when it is misspelled too. The caller's text when the target is unknown; <see langword="null"/>
	/// when the target is known and nothing in it is close, so no invalid path is suggested.
	/// </summary>
	private static string? CorrectTail(ODataFieldValidation.EntityShape shape, string navigation, string tail,
		bool navigationOnly, bool pathOnly) {
		if (!shape.NavigationTargets.TryGetValue(navigation, out ODataFieldValidation.TypeShape? target)) {
			return tail;
		}
		string[] parts = tail.Split('/');
		string? best = Ordered(RankCandidates(MembersOf(target, navigationOnly), parts[0], string.Empty))
			.FirstOrDefault();
		if (best is null) {
			return null;
		}
		parts[0] = pathOnly ? PathOnlyForm(target, best) : best;
		return string.Join("/", parts);
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

	/// <summary>In a select, <c>Name/Id</c> on an entity without that navigation suggests the column <c>NameId</c>.</summary>
	private static void AddScalarFormOfIdPath(ODataFieldValidation.EntityShape shape, string[] segments,
		List<string> result) {
		if (string.Equals(segments[1], "Id", StringComparison.OrdinalIgnoreCase)
			&& shape.Root.Properties.TryGetValue(segments[0] + "Id", out string? column)) {
			result.Add(column);
		}
	}

	private static string? StripEntityPrefix(string name, string entity) =>
		entity.Length > 0 && name.Length > entity.Length && name.StartsWith(entity, StringComparison.OrdinalIgnoreCase)
			? name[entity.Length..]
			: null;

	/// <summary>
	/// Ranks a candidate against the unknown name: tier 0 differs by case only, tier 1 the candidate starts
	/// with the name (at least <see cref="MinPrefixLength"/> characters, so Acc finds Account) or one
	/// contains it (the shorter at least <see cref="MinContainmentLength"/> characters), tier 2 a small edit
	/// distance. <see langword="null"/> means not close enough to suggest.
	/// </summary>
	private static (int, int)? Score(string name, string candidate) {
		string left = name.ToLowerInvariant();
		string right = candidate.ToLowerInvariant();
		if (left == right) {
			return (0, 0);
		}
		(string shorter, string longer) = left.Length <= right.Length ? (left, right) : (right, left);
		// The prefix rule is one-way: a short candidate such as Web must not outrank WebPage for WebPagee.
		if ((left.Length >= MinPrefixLength && right.StartsWith(left, StringComparison.Ordinal))
			|| (shorter.Length >= MinContainmentLength && longer.Contains(shorter, StringComparison.Ordinal))) {
			return (1, longer.Length - shorter.Length);
		}
		int distance = McpToolArgumentSupport.LevenshteinDistance(left, right);
		return distance <= Math.Max(2, left.Length / 4) ? (2, distance) : null;
	}
}
