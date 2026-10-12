using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Clio.Common;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// Accepts the unambiguous argument shapes agents reach for on the process tools
/// (<c>validate-process-graph</c>, <c>describe-business-process</c>, <c>create-business-process</c>) and lets the
/// call run, reporting the canonical form in a warning (ENG-103025).
/// </summary>
/// <remarks>
/// 83 of 163 failed calls in 35 CAADT runs were refused on argument shape alone, most of them with a shape that
/// had exactly one possible meaning. The rule here is the same one <c>ODataWriteArgumentAliases</c> follows: a
/// synonym is accepted only when the call is otherwise unambiguous. The canonical argument supplied next to a
/// synonym, two synonyms of one argument, a synonym of an unrecognised kind, or a document that is not what the
/// canonical argument takes are all REJECTED, never resolved by guessing, and nothing runs. Anything left over
/// still goes through the tool's ordinary unknown-argument check, so this never widens what is silently ignored.
/// The shared <see cref="McpToolArgumentSupport.BuildLegacyAliasError"/> is deliberately untouched: its
/// rename-only behaviour is pinned for every other tool.
/// </remarks>
internal static class ProcessToolArgumentAliases {

	private const string ProcessNameKey = "process-name";
	private const string ProcessUidKey = "process-uid";
	private const string ProcessCaptionKey = "process-caption";
	private const string ElementsKey = "elements";
	private const string FlowsKey = "flows";

	private static readonly JsonSerializerOptions NestedOptions = new() { PropertyNameCaseInsensitive = true };

	private static readonly Regex GuidPattern = new(
		@"^\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?$",
		RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

	private static readonly Regex IdentifierPattern = new(
		@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

	/// <summary>
	/// Lower-cases a key and drops <c>-</c> and <c>_</c>, so <c>process-name</c>, <c>process_name</c> and
	/// <c>processName</c> are one spelling. Used for matching only; messages echo the caller's own spelling.
	/// </summary>
	internal static string Normalize(string key) =>
		(key ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal)
			.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

	/// <summary>Appends <paramref name="warnings"/> to the result as warning messages, ahead of the command's own output.</summary>
	internal static CommandExecutionResult WithWarnings(CommandExecutionResult result, IReadOnlyList<string> warnings) {
		if (warnings is null || warnings.Count == 0) {
			return result;
		}
		IEnumerable<LogMessage> added = warnings.Select(text => (LogMessage)new WarningMessage(text));
		return result with { Output = added.Concat(result.Output ?? []).ToList() };
	}

	private static string AcceptedNote(string callerKey, string canonical) =>
		$"Argument '{McpToolArgumentSupport.DescribeCallerKey(callerKey)}' was accepted as '{canonical}'; "
		+ $"use '{canonical}' in future calls.";

	// ---------------------------------------------------------------------------------------------------
	// describe-business-process
	// ---------------------------------------------------------------------------------------------------

	/// <summary>Outcome of resolving the identity synonyms of <c>describe-business-process</c>.</summary>
	internal sealed record DescribeResolution(DescribeProcessArgs Args, List<string> Warnings, string? Error);

	// Synonyms whose meaning does not depend on the value. Keys are Normalize()d.
	private static readonly Dictionary<string, string> DescribeSpecificAliases = new(StringComparer.Ordinal) {
		["processname"] = ProcessNameKey,
		["processcode"] = ProcessNameKey,
		["schemaname"] = ProcessNameKey,
		["code"] = ProcessNameKey,
		["processuid"] = ProcessUidKey,
		["uid"] = ProcessUidKey,
		["processid"] = ProcessUidKey,
		["schemauid"] = ProcessUidKey,
		["id"] = ProcessUidKey,
		["processcaption"] = ProcessCaptionKey,
		["processtitle"] = ProcessCaptionKey,
		["title"] = ProcessCaptionKey,
		["caption"] = ProcessCaptionKey,
		["displayname"] = ProcessCaptionKey
	};

	// Generic keys: which identity they mean is decided by the VALUE (a GUID, an identifier, or text).
	private static readonly HashSet<string> DescribeGenericAliases = new(StringComparer.Ordinal) {
		"name", "process"
	};

	private static string TargetByShape(string text) {
		if (GuidPattern.IsMatch(text)) {
			return ProcessUidKey;
		}
		return IdentifierPattern.IsMatch(text) ? ProcessNameKey : ProcessCaptionKey;
	}

	/// <summary>Resolves <c>name</c>-style synonyms of the process identity for <c>describe-business-process</c>.</summary>
	/// <param name="args">The bound arguments; its overflow bag is what is inspected.</param>
	internal static DescribeResolution ResolveDescribe(DescribeProcessArgs args) {
		List<string> warnings = [];
		Dictionary<string, JsonElement>? bag = args.ExtensionData;
		if (bag is null || bag.Count == 0) {
			return new DescribeResolution(args, warnings, null);
		}
		// target canonical name -> (caller key, value, was it decided by the value's shape)
		Dictionary<string, List<(string Key, string Value, bool ByShape)>> found = new(StringComparer.Ordinal);
		foreach ((string key, JsonElement value) in bag) {
			string normalized = Normalize(key);
			bool generic = DescribeGenericAliases.Contains(normalized);
			if (!generic && !DescribeSpecificAliases.ContainsKey(normalized)) {
				continue;
			}
			string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
			if (string.IsNullOrWhiteSpace(text)) {
				// Not a usable identity: leave it in the bag so the ordinary unknown-argument refusal names it.
				continue;
			}
			string target = generic ? TargetByShape(text.Trim()) : DescribeSpecificAliases[normalized];
			if (!found.TryGetValue(target, out List<(string, string, bool)>? list)) {
				list = [];
				found[target] = list;
			}
			list.Add((key, text.Trim(), generic));
		}
		if (found.Count == 0) {
			return new DescribeResolution(args, warnings, null);
		}
		foreach ((string target, List<(string Key, string Value, bool ByShape)> list) in found) {
			bool canonicalSupplied = target switch {
				ProcessNameKey => !string.IsNullOrWhiteSpace(args.ProcessName),
				ProcessUidKey => !string.IsNullOrWhiteSpace(args.ProcessUid),
				_ => !string.IsNullOrWhiteSpace(args.ProcessCaption)
			};
			if (canonicalSupplied || list.Count > 1) {
				string shown = string.Join(" and ", list.Select(item => $"'{McpToolArgumentSupport.DescribeCallerKey(item.Key)}'"));
				return new DescribeResolution(args, warnings,
					$"Ambiguous process identity: {shown} and '{target}' all name the process. "
					+ $"Send the identity once, as '{target}'. Nothing was read.");
			}
		}
		HashSet<string> consumed = new(StringComparer.Ordinal);
		DescribeProcessArgs resolved = args;
		foreach ((string target, List<(string Key, string Value, bool ByShape)> list) in found) {
			(string key, string value, bool byShape) = list[0];
			consumed.Add(key);
			resolved = target switch {
				ProcessNameKey => resolved with { ProcessName = value },
				ProcessUidKey => resolved with { ProcessUid = value },
				_ => resolved with { ProcessCaption = value }
			};
			warnings.Add(byShape
				? $"Argument '{McpToolArgumentSupport.DescribeCallerKey(key)}' was read as '{target}' because of its value; "
					+ "send 'process-name', 'process-uid' or 'process-caption' explicitly in future calls."
				: AcceptedNote(key, target));
		}
		Dictionary<string, JsonElement> remaining = bag.Where(pair => !consumed.Contains(pair.Key))
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
		return new DescribeResolution(resolved with { ExtensionData = remaining.Count == 0 ? null : remaining },
			warnings, null);
	}

	// ---------------------------------------------------------------------------------------------------
	// create-business-process
	// ---------------------------------------------------------------------------------------------------

	/// <summary>Outcome of resolving the descriptor shapes of <c>create-business-process</c>.</summary>
	internal sealed record CreateResolution(CreateBusinessProcessArgs Args, List<string> Warnings, string? Error);

	// Wrapper synonyms of 'descriptor' (Normalize()d).
	private static readonly HashSet<string> DescriptorWrapperAliases = new(StringComparer.Ordinal) {
		"process", "definition", "processdescriptor", "processdefinition", "descriptorjson"
	};

	// The top-level fields of a process descriptor, canonical spelling, keyed by Normalize()d form.
	private static readonly Dictionary<string, string> DescriptorFields = new(StringComparer.Ordinal) {
		["name"] = "name",
		["caption"] = "caption",
		["packagename"] = "packageName",
		[ElementsKey] = ElementsKey,
		[FlowsKey] = FlowsKey,
		["parameters"] = "parameters",
		["mappings"] = "mappings",
		["usings"] = "usings",
		["methods"] = "methods",
		["istracing"] = "isTracing"
	};

	/// <summary>
	/// Resolves a descriptor sent under a synonym (<c>process</c>, <c>definition</c>, ...) or spread across the top
	/// level as flat descriptor fields, for <c>create-business-process</c>.
	/// </summary>
	/// <param name="args">The bound arguments; its overflow bag is what is inspected.</param>
	internal static CreateResolution ResolveCreate(CreateBusinessProcessArgs args) {
		List<string> warnings = [];
		Dictionary<string, JsonElement>? bag = args.ExtensionData;
		if (bag is null || bag.Count == 0) {
			return new CreateResolution(args, warnings, null);
		}
		bool descriptorSupplied = args.Descriptor.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
		List<string> wrappers = bag.Keys.Where(key => DescriptorWrapperAliases.Contains(Normalize(key))).ToList();
		List<string> flat = bag.Keys.Where(key => DescriptorFields.ContainsKey(Normalize(key))).ToList();
		bool flatIsDescriptor = flat.Any(key => Normalize(key) == ElementsKey);
		if (wrappers.Count == 0 && !(flatIsDescriptor && !descriptorSupplied)) {
			if (flatIsDescriptor) {
				return new CreateResolution(args, warnings, AmbiguousDescriptor(flat));
			}
			return new CreateResolution(args, warnings, null);
		}
		if (descriptorSupplied || wrappers.Count > 1 || (wrappers.Count == 1 && flat.Count > 0)) {
			List<string> clash = wrappers.Concat(flat).ToList();
			return new CreateResolution(args, warnings, AmbiguousDescriptor(clash));
		}
		JsonElement descriptor;
		HashSet<string> consumed;
		if (wrappers.Count == 1) {
			string key = wrappers[0];
			descriptor = bag[key];
			consumed = [key];
			warnings.Add(AcceptedNote(key, "descriptor"));
		} else {
			JsonObject wrapped = [];
			consumed = [];
			foreach (string key in flat) {
				wrapped[DescriptorFields[Normalize(key)]] = JsonNode.Parse(bag[key].GetRawText());
				consumed.Add(key);
			}
			descriptor = JsonDocument.Parse(wrapped.ToJsonString()).RootElement.Clone();
			warnings.Add("Descriptor fields (" + string.Join(", ", flat.Select(key =>
					$"'{McpToolArgumentSupport.DescribeCallerKey(key)}'")) + ") were wrapped into 'descriptor'; "
				+ "send them inside 'descriptor' in future calls.");
		}
		Dictionary<string, JsonElement> remaining = bag.Where(pair => !consumed.Contains(pair.Key))
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
		return new CreateResolution(
			args with { Descriptor = descriptor, ExtensionData = remaining.Count == 0 ? null : remaining },
			warnings, null);
	}

	private static string AmbiguousDescriptor(IReadOnlyList<string> keys) =>
		"Ambiguous descriptor: " + string.Join(", ", keys.Select(key => $"'{McpToolArgumentSupport.DescribeCallerKey(key)}'"))
		+ " were supplied together with, or in place of, 'descriptor', and they cannot all be the process. "
		+ "Send the process once, as the 'descriptor' object. Nothing was built.";

	// ---------------------------------------------------------------------------------------------------
	// validate-process-graph
	// ---------------------------------------------------------------------------------------------------

	/// <summary>Outcome of resolving the graph shapes of <c>validate-process-graph</c>.</summary>
	internal sealed record GraphResolution(ValidateProcessGraphArgs Args, List<string> Warnings, string? Error);

	private static readonly HashSet<string> GraphWrapperAliases = new(StringComparer.Ordinal) {
		"graph", "descriptor", "process", "processgraph", "definition"
	};

	// Descriptor fields that carry no graph information; tolerated inside a wrapper so a create-business-process
	// descriptor can be validated as it is.
	private static readonly HashSet<string> NonGraphDescriptorFields = new(StringComparer.Ordinal) {
		"name", "caption", "packagename", "parameters", "mappings", "usings", "methods", "istracing", "description"
	};

	/// <summary>
	/// Resolves a graph sent as <c>graph</c>/<c>descriptor</c>/<c>process</c> (an object holding <c>nodes</c> and
	/// <c>edges</c>, or the <c>elements</c> and <c>flows</c> of a create-business-process descriptor), or flat
	/// <c>elements</c>/<c>flows</c> at the top level.
	/// </summary>
	/// <param name="args">The bound arguments; its overflow bag is what is inspected.</param>
	internal static GraphResolution ResolveGraph(ValidateProcessGraphArgs args) {
		List<string> warnings = [];
		Dictionary<string, JsonElement>? bag = args.ExtensionData;
		if (bag is null || bag.Count == 0) {
			return new GraphResolution(args, warnings, null);
		}
		List<string> wrappers = bag.Keys.Where(key => GraphWrapperAliases.Contains(Normalize(key))).ToList();
		List<string> flat = bag.Keys.Where(key => Normalize(key) is ElementsKey or FlowsKey).ToList();
		if (wrappers.Count == 0 && flat.Count == 0) {
			return new GraphResolution(args, warnings, null);
		}
		bool canonicalSupplied = args.Nodes is { Count: > 0 } || args.Edges is { Count: > 0 };
		List<string> clash = wrappers.Concat(flat).ToList();
		if (canonicalSupplied || wrappers.Count > 1 || (wrappers.Count == 1 && flat.Count > 0)) {
			return Refuse(args, warnings,
				"Ambiguous graph: " + string.Join(", ", clash.Select(key => $"'{McpToolArgumentSupport.DescribeCallerKey(key)}'"))
				+ (canonicalSupplied ? " and 'nodes'/'edges'" : string.Empty)
				+ " all carry the graph, and they cannot all be it. Send the graph once, as 'nodes' and 'edges'.");
		}

		Dictionary<string, JsonElement> source = [];
		HashSet<string> consumed = [];
		string origin;
		if (wrappers.Count == 1) {
			string key = wrappers[0];
			origin = McpToolArgumentSupport.DescribeCallerKey(key);
			JsonElement value = bag[key];
			if (value.ValueKind == JsonValueKind.String) {
				try {
					using JsonDocument parsed = JsonDocument.Parse(value.GetString() ?? string.Empty);
					value = parsed.RootElement.Clone();
				} catch (JsonException) {
					return Refuse(args, warnings, $"'{origin}' is a string that is not valid JSON.");
				}
			}
			if (value.ValueKind != JsonValueKind.Object) {
				return Refuse(args, warnings, $"'{origin}' must be a JSON object holding 'nodes' and 'edges'.");
			}
			List<string> foreign = [];
			foreach (JsonProperty property in value.EnumerateObject()) {
				string normalized = Normalize(property.Name);
				if (normalized is "nodes" or ElementsKey or "edges" or FlowsKey) {
					if (!source.TryAdd(normalized, property.Value)) {
						return Refuse(args, warnings, $"'{origin}' repeats '{property.Name}'.");
					}
				} else if (!NonGraphDescriptorFields.Contains(normalized)) {
					foreign.Add($"'{McpToolArgumentSupport.DescribeCallerKey(property.Name)}'");
				}
			}
			if (foreign.Count > 0) {
				return Refuse(args, warnings,
					$"Unknown keys inside '{origin}': {McpToolArgumentSupport.JoinCallerKeys(foreign)}. "
					+ "Valid: nodes, edges (or the elements and flows of a descriptor).");
			}
			consumed.Add(key);
			warnings.Add($"Argument '{origin}' was accepted as the graph; send 'nodes' and 'edges' at the top level "
				+ "in future calls.");
		} else {
			origin = string.Join(" and ", flat.Select(key => $"'{McpToolArgumentSupport.DescribeCallerKey(key)}'"));
			foreach (string key in flat) {
				string normalized = Normalize(key);
				if (!source.TryAdd(normalized, bag[key])) {
					return Refuse(args, warnings, $"{origin} repeats '{key}'.");
				}
				consumed.Add(key);
			}
			warnings.Add($"{origin} were accepted as 'nodes' and 'edges'; send 'nodes' and 'edges' in future calls.");
		}

		if (source.ContainsKey("nodes") && source.ContainsKey(ElementsKey)
			|| source.ContainsKey("edges") && source.ContainsKey(FlowsKey)) {
			return Refuse(args, warnings, $"{origin} carries both 'nodes' and 'elements' (or 'edges' and 'flows'); send one.");
		}
		try {
			List<ProcessGraphNodeArg>? nodes = ReadNodes(source);
			List<ProcessGraphEdgeArg>? edges = ReadEdges(source, out string? edgeError);
			if (edgeError is not null) {
				return Refuse(args, warnings, edgeError);
			}
			Dictionary<string, JsonElement> remaining = bag.Where(pair => !consumed.Contains(pair.Key))
				.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
			return new GraphResolution(
				args with { Nodes = nodes, Edges = edges, ExtensionData = remaining.Count == 0 ? null : remaining },
				warnings, null);
		} catch (JsonException exception) {
			return Refuse(args, warnings, $"{origin} is not a valid graph: {SensitiveErrorTextRedactor.Redact(exception.Message)}");
		}
	}

	private static GraphResolution Refuse(ValidateProcessGraphArgs args, List<string> warnings, string error) =>
		new(args, warnings, error);

	private static List<ProcessGraphNodeArg>? ReadNodes(Dictionary<string, JsonElement> source) {
		if (source.TryGetValue("nodes", out JsonElement nodes)) {
			// Strict: a nested key the node does not take lands in the entry's overflow bag, and the tool's
			// ordinary entry check names it.
			return nodes.ValueKind == JsonValueKind.Array
				? JsonSerializer.Deserialize<List<ProcessGraphNodeArg>>(nodes.GetRawText(), NestedOptions)
				: throw new JsonException("'nodes' must be an array.");
		}
		if (!source.TryGetValue(ElementsKey, out JsonElement elements)) {
			return null;
		}
		if (elements.ValueKind != JsonValueKind.Array) {
			throw new JsonException("'elements' must be an array.");
		}
		// A descriptor element carries far more than the graph needs (caption, block configuration, ...), and the
		// validator accepts both the canvas data-id and the build token as a type, so only name and type are read.
		return elements.EnumerateArray()
			.Select(element => element.ValueKind == JsonValueKind.Object
				? new ProcessGraphNodeArg(ReadString(element, "name"), ReadString(element, "type"))
				: null)
			.ToList();
	}

	private static List<ProcessGraphEdgeArg>? ReadEdges(Dictionary<string, JsonElement> source, out string? error) {
		error = null;
		if (source.TryGetValue("edges", out JsonElement edges)) {
			return edges.ValueKind == JsonValueKind.Array
				? JsonSerializer.Deserialize<List<ProcessGraphEdgeArg>>(edges.GetRawText(), NestedOptions)
				: throw new JsonException("'edges' must be an array.");
		}
		if (!source.TryGetValue(FlowsKey, out JsonElement flows)) {
			return null;
		}
		if (flows.ValueKind != JsonValueKind.Array) {
			throw new JsonException("'flows' must be an array.");
		}
		List<ProcessGraphEdgeArg> result = [];
		foreach (JsonElement flow in flows.EnumerateArray()) {
			if (flow.ValueKind != JsonValueKind.Object) {
				result.Add(null);
				continue;
			}
			string[]? results = null;
			if (flow.TryGetProperty("results", out JsonElement resultsElement)) {
				if (resultsElement.ValueKind != JsonValueKind.Array
					|| resultsElement.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String)) {
					error = "A flow's 'results' must be an array of strings.";
					return null;
				}
				results = resultsElement.EnumerateArray().Select(item => item.GetString()).ToArray();
			}
			result.Add(new ProcessGraphEdgeArg(
				ReadString(flow, "source"),
				ReadString(flow, "target"),
				ReadString(flow, "kind") ?? ReadString(flow, "flow-kind"),
				ReadString(flow, "condition"),
				results));
		}
		return result;
	}

	private static string? ReadString(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
