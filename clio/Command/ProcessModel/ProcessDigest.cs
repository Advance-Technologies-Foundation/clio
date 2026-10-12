using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Builds the compact views of a described process (ENG-103023): the <c>summary</c> a describe returns when the
/// whole graph would not fit one inline tool reply, the single-element and version-list views, the content
/// checksum that lets a caller tell two reads apart without comparing them, and the change list a write reports
/// so the caller does not have to read the process back to confirm it.
/// </summary>
/// <remarks>
/// Pure functions over <see cref="DescribeProcessResult"/>; nothing here reads an environment. Every view is a
/// <see cref="JsonObject"/> written with <see cref="DescribeProcessCommand.OutputOptions"/>, so it is compact and
/// uses the same relaxed encoder as the full graph.
/// </remarks>
internal static class ProcessDigest {

	/// <summary>
	/// Above this many characters of compact graph JSON a describe without an explicit mode answers with the
	/// summary. Copilot CLI writes a tool result over ~20 KB to a file, and the graph travels as a STRING inside
	/// the result, so every quote costs a second character; 15 000 keeps the whole reply under that line.
	/// </summary>
	internal const int AutoSummaryThresholdChars = 15_000;

	/// <summary>The size the summary aims to stay within; above it captions go first, then list tails.</summary>
	internal const int SummaryBudgetChars = 3_072;

	/// <summary>Mode names, as the <c>mode</c> argument and the reply's <c>mode</c> member spell them.</summary>
	internal const string SummaryMode = "summary";

	/// <inheritdoc cref="SummaryMode"/>
	internal const string FullMode = "full";

	/// <inheritdoc cref="SummaryMode"/>
	internal const string VersionsMode = "versions";

	/// <inheritdoc cref="SummaryMode"/>
	internal const string ElementMode = "element";

	private const string SchemaUIdMember = "schemaUId";
	private const string SequenceKind = "sequence";
	private const string ConditionalKind = "conditional";

	// Version-family facts describe the FAMILY, not this schema's content, and change when a sibling is saved.
	private static readonly string[] VersionFactMembers = [
		"version", "isActiveVersion", "activeVersionSchemaUId", "activeVersionName", "versionRootSchemaUId",
		"activeVersionSource", "versions", "versionsTruncatedAt", "versionReadWarning"
	];

	// Tracing is a platform user property on the version family's root, which the platform's nightly job switches
	// off by itself: family state, like the version facts, that changes while this schema's content does not.
	private static readonly string[] FamilyStateMembers = ["tracing"];

	// The server re-derives the whole layout on every save, so the diagram members would mark every element
	// of a process as changed by an edit that touched one of them.
	private static readonly string[] ElementLayoutMembers = ["position", "size"];
	private static readonly string[] FlowLayoutMembers = ["geometry"];

	/// <summary>
	/// A short content checksum of the graph: 12 hex characters of SHA-256 over the compact JSON of everything
	/// except the version-family facts and tracing state, the schema's own name and UId, every member named <c>uid</c> (element
	/// and parameter identities), and the diagram layout.
	/// </summary>
	/// <remarks>
	/// Identities are left out because they say which schema was read, not what it does. The value is
	/// comparable only between reads taken through clio in the same culture: it is a hash of clio's own
	/// projection, captions included, and a clio that reports a new member changes it.
	/// </remarks>
	/// <param name="result">The described process.</param>
	/// <returns>The checksum, lower-case hex.</returns>
	internal static string Checksum(DescribeProcessResult result) {
		JsonObject graph = ToNode(result);
		foreach (string member in VersionFactMembers.Concat(FamilyStateMembers).Concat(["name", SchemaUIdMember])) {
			graph.Remove(member);
		}
		StripMembers(graph["elements"] as JsonArray, ElementLayoutMembers);
		StripMembers(graph["flows"] as JsonArray, FlowLayoutMembers);
		StripIdentities(graph);
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(graph.ToJsonString(DescribeProcessCommand.OutputOptions)));
		return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
	}

	/// <summary>
	/// The summary: elements as name + type + caption, flows as <c>from -&gt; to</c> with their kind and
	/// whether a condition is set, start signals, process parameters with their types, the version standing
	/// and the checksum - within <see cref="SummaryBudgetChars"/> where the process allows.
	/// </summary>
	/// <param name="result">The described process.</param>
	/// <param name="checksum">Its <see cref="Checksum"/>.</param>
	/// <returns>The summary object; the caller adds <c>mode</c> and any pointer to more.</returns>
	internal static JsonObject BuildSummary(DescribeProcessResult result, string checksum) {
		List<DescribedElement> elements = result.Elements?.Where(element => element is not null).ToList() ?? [];
		List<string> flows = result.Flows?.Where(flow => flow is not null).Select(FlowLine).ToList() ?? [];
		List<DescribedParameter> parameters = result.Parameters?.Where(parameter => parameter is not null).ToList() ?? [];
		JsonObject summary = BuildSummary(result, checksum, elements, flows, parameters, withCaptions: true);
		if (Length(summary) <= SummaryBudgetChars) {
			return summary;
		}
		summary = BuildSummary(result, checksum, elements, flows, parameters, withCaptions: false);
		if (Length(summary) <= SummaryBudgetChars) {
			return summary;
		}
		// Still over: keep the same leading share of every list - elements, flows and parameters alike, so no
		// one list is emptied to save the others - and say how much was left out. The share is found by
		// bisection, a dozen rebuilds whatever the process size. The graph stays one call away (mode=full,
		// element=<name>), so a long process loses detail here, never the way to reach it.
		JsonObject Cut(double share) {
			int Keep(int count) => (int)Math.Floor(count * share);
			JsonObject cut = BuildSummary(result, checksum, elements.Take(Keep(elements.Count)).ToList(),
				flows.Take(Keep(flows.Count)).ToList(), parameters.Take(Keep(parameters.Count)).ToList(),
				withCaptions: false);
			cut["omitted"] = new JsonObject {
				["elements"] = elements.Count - Keep(elements.Count),
				["flows"] = flows.Count - Keep(flows.Count),
				["parameters"] = parameters.Count - Keep(parameters.Count)
			};
			return cut;
		}
		double low = 0, high = 1;
		JsonObject best = Cut(0);
		for (int step = 0; step < 12; step++) {
			double middle = (low + high) / 2;
			JsonObject candidate = Cut(middle);
			if (Length(candidate) <= SummaryBudgetChars) {
				best = candidate;
				low = middle;
			} else {
				high = middle;
			}
		}
		return best;
	}

	/// <summary>
	/// One element in full - every member describe reports for it, mappings, filters and formulas included -
	/// with the flows that enter and leave it, or an empty object when the process has no element by that name.
	/// </summary>
	/// <param name="result">The described process.</param>
	/// <param name="elementName">The element's name (case-insensitive).</param>
	/// <param name="checksum">The process's <see cref="Checksum"/>.</param>
	/// <returns>The element view, or an empty object.</returns>
	internal static JsonObject BuildElement(DescribeProcessResult result, string elementName, string checksum) {
		string wanted = elementName?.Trim();
		// An exact match first, so two elements whose names differ only in case resolve to the one asked for.
		DescribedElement element = result.Elements?.FirstOrDefault(candidate =>
				string.Equals(candidate?.Name, wanted, StringComparison.Ordinal))
			?? result.Elements?.FirstOrDefault(candidate =>
				string.Equals(candidate?.Name, wanted, StringComparison.OrdinalIgnoreCase));
		if (element is null) {
			return new JsonObject();
		}
		List<DescribedFlow> flows = result.Flows ?? [];
		return new JsonObject {
			["mode"] = ElementMode,
			["name"] = result.Name,
			[SchemaUIdMember] = result.SchemaUId,
			["checksum"] = checksum,
			["element"] = JsonSerializer.SerializeToNode(element, DescribeProcessCommand.OutputOptions),
			["flowsIn"] = ToArray(flows.Where(flow => IsNamed(flow?.Target, element.Name))),
			["flowsOut"] = ToArray(flows.Where(flow => IsNamed(flow?.Source, element.Name)))
		};
	}

	/// <summary>
	/// The version standing of the described schema and its family list, each member with its modification
	/// date where the process library reports one, plus the checksum of the schema that was read.
	/// </summary>
	/// <param name="result">The described process.</param>
	/// <param name="checksum">Its <see cref="Checksum"/>.</param>
	/// <returns>The versions view.</returns>
	internal static JsonObject BuildVersions(DescribeProcessResult result, string checksum) {
		JsonObject view = new() {
			["mode"] = VersionsMode,
			["name"] = result.Name,
			[SchemaUIdMember] = result.SchemaUId,
			["checksum"] = checksum
		};
		JsonObject graph = ToNode(result);
		foreach (string member in VersionFactMembers) {
			if (graph[member] is { } value) {
				view[member] = value.DeepClone();
			}
		}
		return view;
	}

	/// <summary>
	/// What differs between two reads of a process: elements, flows and process parameters added, removed and
	/// changed. Layout is ignored, for the reason <see cref="Checksum"/> ignores it. An empty object means
	/// nothing differs.
	/// </summary>
	/// <param name="before">The read taken before the write.</param>
	/// <param name="after">The read taken after it.</param>
	/// <returns>The change lists; a list with nothing in it is left out.</returns>
	internal static JsonObject BuildChanges(DescribeProcessResult before, DescribeProcessResult after) {
		JsonObject changes = new();
		Diff(changes, "elements",
			Keyed(before.Elements, element => element.Name, element => Canonical(element, ElementLayoutMembers)),
			Keyed(after.Elements, element => element.Name, element => Canonical(element, ElementLayoutMembers)));
		(List<DescribedFlow> flowsBefore, List<DescribedFlow> flowsAfter) = WithoutUnchangedFlows(before.Flows, after.Flows);
		Diff(changes, "flows", KeyedFlows(flowsBefore), KeyedFlows(flowsAfter));
		Diff(changes, "parameters",
			Keyed(before.Parameters, parameter => parameter.Name, parameter => Canonical(parameter, [])),
			Keyed(after.Parameters, parameter => parameter.Name, parameter => Canonical(parameter, [])));
		return changes;
	}

	/// <summary>
	/// The element's type as a caller writes it: the user-task schema name for a user task, otherwise the
	/// build type (falling back to the runtime class name on a server that reports neither).
	/// </summary>
	internal static string ElementType(DescribedElement element) =>
		!string.IsNullOrWhiteSpace(element.UserTaskName)
		&& string.Equals(element.BuildType, "usertask", StringComparison.OrdinalIgnoreCase)
			? element.UserTaskName
			: element.BuildType ?? element.Type;

	/// <summary>
	/// A flow as one line: <c>Source -&gt; Target</c>, plus <c>[kind]</c> for anything but a plain sequence flow,
	/// and for a conditional flow whether it decides on a condition, on activity results, or on NOTHING - the
	/// last being a branch that is never taken by its own test.
	/// </summary>
	internal static string FlowLine(DescribedFlow flow) {
		string line = $"{flow.Source} -> {flow.Target}";
		string kind = string.IsNullOrWhiteSpace(flow.Kind) ? SequenceKind : flow.Kind;
		bool hasCondition = !string.IsNullOrWhiteSpace(flow.Condition);
		if (string.Equals(kind, ConditionalKind, StringComparison.OrdinalIgnoreCase)) {
			string decidedBy = hasCondition ? "condition" : "NO condition";
			if (flow.BranchesOnActivityResult == true) {
				decidedBy = "results";
			}
			return $"{line} [{kind}, {decidedBy}]";
		}
		if (string.Equals(kind, SequenceKind, StringComparison.OrdinalIgnoreCase)) {
			return hasCondition ? $"{line} [condition text, not evaluated]" : line;
		}
		return hasCondition ? $"{line} [{kind}, condition text, not evaluated]" : $"{line} [{kind}]";
	}

	/// <summary>Length of a view as it is written.</summary>
	internal static int Length(JsonNode node) => node.ToJsonString(DescribeProcessCommand.OutputOptions).Length;

	/// <summary>Writes a view as compact JSON.</summary>
	internal static string Write(JsonNode node) => node.ToJsonString(DescribeProcessCommand.OutputOptions);

	private static JsonObject BuildSummary(DescribeProcessResult result, string checksum,
		IReadOnlyList<DescribedElement> elements, IReadOnlyList<string> flows,
		IReadOnlyList<DescribedParameter> parameters, bool withCaptions) {
		JsonObject summary = new() {
			["name"] = result.Name,
			["caption"] = result.Caption,
			[SchemaUIdMember] = result.SchemaUId
		};
		AddIfPresent(summary, "version", result.Version);
		AddIfPresent(summary, "isActiveVersion", result.IsActiveVersion);
		if (result.IsActiveVersion == false) {
			// The one version fact a summary reader acts on: the graph read is not the one that runs.
			AddIfPresent(summary, "activeVersionName", result.ActiveVersionName);
			AddIfPresent(summary, "activeVersionSchemaUId", result.ActiveVersionSchemaUId);
		}
		AddIfPresent(summary, "versionCount", result.Versions?.Count);
		AddIfPresent(summary, "versionReadWarning", result.VersionReadWarning);
		summary["checksum"] = checksum;
		summary["elements"] = new JsonArray(elements.Select(element => (JsonNode)ElementEntry(element, withCaptions))
			.ToArray());
		summary["flows"] = new JsonArray(flows.Select(flow => (JsonNode)JsonValue.Create(flow)).ToArray());
		JsonArray signals = new(elements.Where(element => element.Signal is not null)
			.Select(element => (JsonNode)SignalEntry(element)).ToArray());
		if (signals.Count > 0) {
			summary["startSignals"] = signals;
		}
		summary["parameters"] = new JsonArray(parameters.Select(parameter => (JsonNode)ParameterEntry(parameter))
			.ToArray());
		return summary;
	}

	private static JsonObject ElementEntry(DescribedElement element, bool withCaption) {
		JsonObject entry = new() {
			["name"] = element.Name,
			["type"] = ElementType(element)
		};
		if (withCaption && !string.IsNullOrWhiteSpace(element.Caption)
			&& !string.Equals(element.Caption, element.Name, StringComparison.Ordinal)) {
			entry["caption"] = element.Caption;
		}
		return entry;
	}

	private static JsonObject SignalEntry(DescribedElement element) {
		JsonObject entry = new() {
			["element"] = element.Name,
			["entity"] = element.Signal.Entity,
			["on"] = element.Signal.On
		};
		if (element.Signal.ChangedColumns is { Count: > 0 } columns) {
			entry["changedColumns"] = new JsonArray(columns.Select(column => (JsonNode)JsonValue.Create(column))
				.ToArray());
		}
		return entry;
	}

	private static JsonObject ParameterEntry(DescribedParameter parameter) {
		string type = string.IsNullOrWhiteSpace(parameter.ReferenceSchema)
			? parameter.Type
			: $"{parameter.Type}({parameter.ReferenceSchema})";
		JsonObject entry = new() {
			["name"] = parameter.Name,
			["type"] = type
		};
		AddIfPresent(entry, "direction", parameter.Direction);
		return entry;
	}

	private static void AddIfPresent<T>(JsonObject target, string member, T value) {
		if (value is not null) {
			target[member] = JsonValue.Create(value);
		}
	}

	private static JsonArray ToArray(IEnumerable<DescribedFlow> flows) =>
		new(flows.Select(flow => JsonSerializer.SerializeToNode(flow, DescribeProcessCommand.OutputOptions))
			.ToArray());

	// Flow endpoints carry the element's name verbatim, so they are matched exactly to the element resolved above.
	private static bool IsNamed(string value, string name) =>
		string.Equals(value, name, StringComparison.Ordinal);

	private static JsonObject ToNode(DescribeProcessResult result) =>
		JsonSerializer.SerializeToNode(result, DescribeProcessCommand.OutputOptions) as JsonObject ?? new JsonObject();

	private static void StripMembers(JsonArray items, string[] members) {
		foreach (JsonObject item in items?.OfType<JsonObject>() ?? []) {
			foreach (string member in members) {
				item.Remove(member);
			}
		}
	}

	private static string Canonical<T>(T value, string[] ignoredMembers) {
		JsonNode node = JsonSerializer.SerializeToNode(value, DescribeProcessCommand.OutputOptions);
		if (node is JsonObject obj) {
			foreach (string member in ignoredMembers) {
				obj.Remove(member);
			}
		}
		StripIdentities(node);
		return node?.ToJsonString(DescribeProcessCommand.OutputOptions) ?? string.Empty;
	}

	// A copy saved as a new version may carry fresh element and parameter UIds; they name the schema read, not
	// what it does, so comparing them would mark every element of a new version as changed.
	private static void StripIdentities(JsonNode node) {
		switch (node) {
			case JsonObject obj:
				obj.Remove("uid");
				foreach (KeyValuePair<string, JsonNode> member in obj.ToList()) {
					StripIdentities(member.Value);
				}
				break;
			case JsonArray array:
				foreach (JsonNode item in array) {
					StripIdentities(item);
				}
				break;
		}
	}

	// Keyed by name; a name that repeats (it should not) keeps its first entry, so the diff stays defined.
	private static Dictionary<string, (string Label, string Content)> Keyed<T>(IEnumerable<T> items,
		Func<T, string> key, Func<T, string> content) where T : class {
		Dictionary<string, (string, string)> keyed = new(StringComparer.Ordinal);
		foreach (T item in items ?? []) {
			string name = item is null ? null : key(item);
			if (name is not null) {
				keyed.TryAdd(name, (name, content(item)));
			}
		}
		return keyed;
	}

	// Pairs off flows whose content is identical in both reads before the rest are keyed by endpoint pair, so that
	// removing one of two parallel flows between the same elements reports that flow as removed, rather than the
	// survivor as changed and the second ordinal as removed.
	private static (List<DescribedFlow> Before, List<DescribedFlow> After) WithoutUnchangedFlows(
		IEnumerable<DescribedFlow> before, IEnumerable<DescribedFlow> after) {
		List<(DescribedFlow Flow, string Content)> remaining = (after ?? []).Where(flow => flow is not null)
			.Select(flow => (flow, Canonical(flow, FlowLayoutMembers))).ToList();
		List<DescribedFlow> changedBefore = [];
		foreach (DescribedFlow flow in (before ?? []).Where(flow => flow is not null)) {
			string content = Canonical(flow, FlowLayoutMembers);
			int match = remaining.FindIndex(candidate => string.Equals(candidate.Content, content, StringComparison.Ordinal));
			if (match >= 0) {
				remaining.RemoveAt(match);
			} else {
				changedBefore.Add(flow);
			}
		}
		return (changedBefore, remaining.Select(candidate => candidate.Flow).ToList());
	}

	// A flow has no address of its own on the write path - it is named by its endpoint pair - so it is keyed
	// the same way, with an ordinal for the second flow between the same two elements.
	private static Dictionary<string, (string Label, string Content)> KeyedFlows(IEnumerable<DescribedFlow> flows) {
		Dictionary<string, (string, string)> keyed = new(StringComparer.Ordinal);
		Dictionary<string, int> seen = new(StringComparer.Ordinal);
		foreach (DescribedFlow flow in flows ?? []) {
			if (flow is null) {
				continue;
			}
			string pair = $"{flow.Source} -> {flow.Target}";
			int ordinal = seen.TryGetValue(pair, out int count) ? count + 1 : 0;
			seen[pair] = ordinal;
			keyed[ordinal == 0 ? pair : $"{pair} #{ordinal + 1}"] = (FlowLine(flow), Canonical(flow, FlowLayoutMembers));
		}
		return keyed;
	}

	private static void Diff(JsonObject changes, string family,
		Dictionary<string, (string Label, string Content)> before,
		Dictionary<string, (string Label, string Content)> after) {
		AddList(changes, family + "Added", after.Where(entry => !before.ContainsKey(entry.Key))
			.Select(entry => entry.Value.Label));
		AddList(changes, family + "Removed", before.Where(entry => !after.ContainsKey(entry.Key))
			.Select(entry => entry.Value.Label));
		AddList(changes, family + "Changed", after.Where(entry =>
				before.TryGetValue(entry.Key, out (string Label, string Content) old)
				&& !string.Equals(old.Content, entry.Value.Content, StringComparison.Ordinal))
			.Select(entry => entry.Value.Label));
	}

	private static void AddList(JsonObject target, string member, IEnumerable<string> values) {
		JsonArray array = new(values.Select(value => (JsonNode)JsonValue.Create(value)).ToArray());
		if (array.Count > 0) {
			target[member] = array;
		}
	}
}
