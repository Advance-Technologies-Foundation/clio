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
	/// Above this many bytes of tool reply - the result text the agent reads, as
	/// <see cref="Clio.Command.McpServer.McpResultSize.OfCommandReply"/> measures it - a describe without an explicit
	/// mode answers with the summary. Copilot CLI writes a tool result over ~20 KB to a file, and the graph travels
	/// as an escaped string inside that result, so it costs more bytes than it has characters (ENG-103023 QA: the
	/// old 15,000-character rule let a quote-heavy graph through). Measured in reply bytes, not graph characters;
	/// 18,000 leaves ~2 KB under the line.
	/// </summary>
	internal const int AutoSummaryThresholdBytes = 18_000;

	/// <summary>
	/// The UTF-8 size the whole summary REPLY aims to stay within - the <c>mode</c>, <c>fullChars</c> and <c>more</c>
	/// envelope included; above it captions go first, then list tails, then the header's free text.
	/// </summary>
	internal const int SummaryBudgetBytes = 3_072;

	/// <summary>Most entries one post-write change list carries; the rest are counted in <c>&lt;list&gt;Omitted</c>.</summary>
	internal const int MaxChangeEntries = 25;

	/// <summary>Most element names an element-not-found refusal lists.</summary>
	internal const int MaxListedElementNames = 30;

	// Rebuilds the bisection of a too-large summary may spend; the share it finds is within 1/4096 of the best.
	private const int BisectionSteps = 12;

	// Bytes of SHA-256 the checksum keeps: 12 hex characters.
	private const int ChecksumBytes = 6;

	// The longest versionReadWarning a summary that is still over budget keeps.
	private const int CompactWarningChars = 160;

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

	// Text the server resolves in the READ's culture. A describe defaults to en-US while the post-write reads use the
	// profile culture (the flow-label guard needs it), so hashing these would make the two checksums of one unchanged
	// process differ on every non-en-US profile: captions, descriptions, flow labels, display values, the
	// localizable texts of pages and emails (recommendation, hint, subject), a branch's activity-result captions
	// (results), and every member named ...Caption or ...Display (processCaption, recordIdDisplay, templateDisplay,
	// senderDisplay, roleDisplay, approver...Display) - lookup and library names the server resolves per culture.
	private static readonly HashSet<string> LocalizedMembers = new(StringComparer.Ordinal) {
		"caption", "description", "label", "displayValue", "valueDisplay", "recommendation", "hint", "subject",
		"results"
	};

	private static bool IsLocalized(string member) =>
		LocalizedMembers.Contains(member)
		|| member.EndsWith("Caption", StringComparison.Ordinal)
		|| member.EndsWith("Display", StringComparison.Ordinal);

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
	/// and parameter identities), the diagram layout, and the culture-resolved text clio reports as its own members
	/// (captions, descriptions, flow labels, display values, result captions, page and email texts - see
	/// <c>LocalizedMembers</c>). A constant inside an element parameter's <c>value</c> is still hashed as read.
	/// </summary>
	/// <remarks>
	/// Identities are left out because they say which schema was read, not what it does; localized text because
	/// it depends on the culture of the READ, so leaving it in would make a write digest (profile culture) and a
	/// default describe (en-US) disagree about an unchanged process. The price: an edit that changes only a
	/// caption or a label keeps the checksum - the write digest's change list still reports it. The value is a
	/// hash of clio's own projection, so a clio that reports a new member changes it.
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
		StripLocalized(graph);
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(graph.ToJsonString(DescribeProcessCommand.OutputOptions)));
		return Convert.ToHexString(hash, 0, ChecksumBytes).ToLowerInvariant();
	}

	/// <summary>
	/// The summary: elements as name + type + caption, flows as <c>from -&gt; to</c> with their kind and
	/// whether a condition is set, start signals, process parameters with their types, the version standing
	/// and the checksum - within <see cref="SummaryBudgetBytes"/> where the process allows.
	/// </summary>
	/// <param name="result">The described process.</param>
	/// <param name="checksum">Its <see cref="Checksum"/>.</param>
	/// <returns>The summary object, without a reply envelope.</returns>
	internal static JsonObject BuildSummary(DescribeProcessResult result, string checksum) =>
		FitSummary(result, checksum, envelope: null);

	/// <summary>
	/// The summary REPLY of a describe: <c>mode</c> first, the summary, then <c>fullChars</c> and the <c>more</c>
	/// pointer - the whole object fitted to <see cref="SummaryBudgetBytes"/>.
	/// </summary>
	/// <param name="result">The described process.</param>
	/// <param name="checksum">Its <see cref="Checksum"/>.</param>
	/// <param name="fullChars">Length of the full graph, reported so the caller sees what mode=full costs.</param>
	/// <returns>The reply object.</returns>
	internal static JsonObject BuildSummaryReply(DescribeProcessResult result, string checksum, int fullChars) {
		JsonObject envelope = new() {
			["fullChars"] = fullChars,
			["more"] = $"mode=full returns the whole graph ({fullChars} chars); element=<name> returns one element "
				+ "in full (mappings, filters, formulas, its flows); mode=versions returns the version list."
		};
		return FitSummary(result, checksum, envelope);
	}

	private static JsonObject FitSummary(DescribeProcessResult result, string checksum, JsonObject envelope) {
		List<DescribedElement> elements = result.Elements?.Where(element => element is not null).ToList() ?? [];
		List<string> flows = result.Flows?.Where(flow => flow is not null).Select(FlowLine).ToList() ?? [];
		List<DescribedParameter> parameters = result.Parameters?.Where(parameter => parameter is not null).ToList() ?? [];
		JsonObject Build(IReadOnlyList<DescribedElement> keptElements, IReadOnlyList<string> keptFlows,
			IReadOnlyList<DescribedParameter> keptParameters, bool withCaptions, bool compactHeader) =>
			Enveloped(ComposeSummary(result, checksum, keptElements, keptFlows, keptParameters, withCaptions,
				compactHeader), envelope);
		JsonObject summary = Build(elements, flows, parameters, withCaptions: true, compactHeader: false);
		if (Size(summary) <= SummaryBudgetBytes) {
			return summary;
		}
		summary = Build(elements, flows, parameters, withCaptions: false, compactHeader: false);
		if (Size(summary) <= SummaryBudgetBytes) {
			return summary;
		}
		// Still over: keep the same leading share of every list - elements, flows and parameters alike, so no
		// one list is emptied to save the others - and say how much was left out. The share is found by
		// bisection, a dozen rebuilds whatever the process size. The graph stays one call away (mode=full,
		// element=<name>), so a long process loses detail here, never the way to reach it. When even an empty
		// share is over (a long caption or version warning), the header's free text is shortened too.
		bool compact = false;
		JsonObject Cut(double share) {
			int Keep(int count) => (int)Math.Floor(count * share);
			JsonObject cut = Build(elements.Take(Keep(elements.Count)).ToList(),
				flows.Take(Keep(flows.Count)).ToList(), parameters.Take(Keep(parameters.Count)).ToList(),
				withCaptions: false, compactHeader: compact);
			cut["omitted"] = new JsonObject {
				["elements"] = elements.Count - Keep(elements.Count),
				["flows"] = flows.Count - Keep(flows.Count),
				["parameters"] = parameters.Count - Keep(parameters.Count)
			};
			return cut;
		}
		double low = 0, high = 1;
		JsonObject best = Cut(0);
		if (Size(best) > SummaryBudgetBytes) {
			compact = true;
			best = Cut(0);
		}
		for (int step = 0; step < BisectionSteps; step++) {
			double middle = (low + high) / 2;
			JsonObject candidate = Cut(middle);
			if (Size(candidate) <= SummaryBudgetBytes) {
				best = candidate;
				low = middle;
			} else {
				high = middle;
			}
		}
		return best;
	}

	// mode first, as every reply names its mode up front; the envelope's pointers last.
	private static JsonObject Enveloped(JsonObject summary, JsonObject envelope) {
		if (envelope is null) {
			return summary;
		}
		JsonObject reply = new() { ["mode"] = SummaryMode };
		foreach (KeyValuePair<string, JsonNode> member in summary.ToList()) {
			summary.Remove(member.Key);
			reply[member.Key] = member.Value;
		}
		foreach (KeyValuePair<string, JsonNode> member in envelope) {
			reply[member.Key] = member.Value?.DeepClone();
		}
		return reply;
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

	/// <summary>Size of a view as it is written, in UTF-8 bytes - the unit a client's spill line is measured in.</summary>
	internal static int Size(JsonNode node) => Encoding.UTF8.GetByteCount(Write(node));

	/// <summary>Writes a view as compact JSON.</summary>
	internal static string Write(JsonNode node) => node.ToJsonString(DescribeProcessCommand.OutputOptions);

	private static JsonObject ComposeSummary(DescribeProcessResult result, string checksum,
		IReadOnlyList<DescribedElement> elements, IReadOnlyList<string> flows,
		IReadOnlyList<DescribedParameter> parameters, bool withCaptions, bool compactHeader) {
		JsonObject summary = new() { ["name"] = result.Name };
		if (!compactHeader) {
			summary["caption"] = result.Caption;
		}
		summary[SchemaUIdMember] = result.SchemaUId;
		AddIfPresent(summary, "version", result.Version);
		AddIfPresent(summary, "isActiveVersion", result.IsActiveVersion);
		if (result.IsActiveVersion == false) {
			// The one version fact a summary reader acts on: the graph read is not the one that runs.
			AddIfPresent(summary, "activeVersionName", result.ActiveVersionName);
			AddIfPresent(summary, "activeVersionSchemaUId", result.ActiveVersionSchemaUId);
		}
		AddIfPresent(summary, "versionCount", result.Versions?.Count);
		string warning = result.VersionReadWarning;
		if (compactHeader && warning is { Length: > CompactWarningChars }) {
			warning = warning[..CompactWarningChars] + "...";
		}
		AddIfPresent(summary, "versionReadWarning", warning);
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

	private static void StripLocalized(JsonNode node) {
		switch (node) {
			case JsonObject obj:
				foreach (string member in obj.Select(entry => entry.Key).Where(IsLocalized).ToList()) {
					obj.Remove(member);
				}
				foreach (KeyValuePair<string, JsonNode> member in obj.ToList()) {
					StripLocalized(member.Value);
				}
				break;
			case JsonArray array:
				foreach (JsonNode item in array) {
					StripLocalized(item);
				}
				break;
		}
	}


	// Identities name the schema read, not what it does. On the measured stand a version keeps the element and
	// parameter UIds of its source (and the [#...[Parameter:{uid}]#] macros in mapped values with them), so this
	// is defensive: a server that re-minted them would otherwise mark every element of a new version as changed.
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

	// Capped like PageWriteDigest.MaxComponents: an activation between two very different versions would
	// otherwise list every element and spill the write reply by itself.
	private static void AddList(JsonObject target, string member, IEnumerable<string> values) {
		List<string> all = values.ToList();
		if (all.Count == 0) {
			return;
		}
		target[member] = new JsonArray(all.Take(MaxChangeEntries).Select(value => (JsonNode)JsonValue.Create(value))
			.ToArray());
		if (all.Count > MaxChangeEntries) {
			target[member + "Omitted"] = all.Count - MaxChangeEntries;
		}
	}
}
