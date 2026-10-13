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
/// <para>
/// One rule for the whole process family, so a shape that works on one tool means the same on its neighbour:
/// </para>
/// <list type="bullet">
/// <item>A synonym whose meaning does not depend on its value (<c>processName</c>, <c>code</c>, <c>uid</c>,
/// ...) is accepted on every tool that addresses an existing process: <c>describe-business-process</c>,
/// <c>modify-business-process</c> and <c>modify-business-process-as-new-version</c>.</item>
/// <item>A generic key read by the SHAPE of its value (<c>name</c>, or <c>process</c> as text) is accepted only
/// on the read-only <c>describe-business-process</c>: a write never runs on a guessed reading.</item>
/// <item><c>process</c> holding TEXT is a process identity; <c>process</c> holding an OBJECT is the process
/// document - the descriptor on <c>create-business-process</c>, the graph on <c>validate-process-graph</c>.
/// Text where a document is expected is refused, naming the tools that take an identity; an object where an
/// identity is expected is not a usable identity, so it stays an unknown argument and is refused by name.</item>
/// <item>A document spread flat over the top level is wrapped only when it carries <c>elements</c>, and never
/// next to a top-level argument that would override one of its fields.</item>
/// </list>
/// </remarks>
internal static class ProcessToolArgumentAliases {

	private const string ProcessNameKey = "process-name";
	private const string ProcessUidKey = "process-uid";
	private const string ProcessCaptionKey = "process-caption";
	private const string CaptionKey = "caption";
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

	/// <summary>
	/// Outcome of resolving the identity synonyms of a tool that addresses an existing process: the canonical
	/// identity after the synonyms were applied, the overflow bag without the keys that were consumed, and either
	/// the warnings to report or the refusal.
	/// </summary>
	internal sealed record IdentityResolution(
		string? ProcessName, string? ProcessUid, string? ProcessCaption,
		Dictionary<string, JsonElement>? Remaining, List<string> Warnings, string? Error);

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
		[CaptionKey] = ProcessCaptionKey,
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
		IdentityResolution identity = ResolveIdentity(args.ExtensionData, args.ProcessName, args.ProcessUid,
			args.ProcessCaption, readOnly: true);
		if (identity.Error is not null || identity.Warnings.Count == 0) {
			return new DescribeResolution(args, identity.Warnings, identity.Error);
		}
		return new DescribeResolution(args with {
			ProcessName = identity.ProcessName,
			ProcessUid = identity.ProcessUid,
			ProcessCaption = identity.ProcessCaption,
			ExtensionData = identity.Remaining
		}, identity.Warnings, null);
	}

	/// <summary>
	/// Resolves the identity synonyms of a tool that WRITES to an existing process (<c>modify-business-process</c>,
	/// <c>modify-business-process-as-new-version</c>). Only the synonyms whose meaning does not depend on the value
	/// are read (<c>processName</c>, <c>code</c>, <c>uid</c>, ...): a write never runs on a reading guessed from
	/// the value's shape, so <c>name</c> and <c>process</c> stay unknown arguments here, and so does a caption,
	/// which these tools cannot address a process by.
	/// </summary>
	/// <param name="bag">The args record's overflow bag.</param>
	/// <param name="processName">The canonical <c>process-name</c>, when supplied.</param>
	/// <param name="processUid">The canonical <c>process-uid</c>, when supplied.</param>
	internal static IdentityResolution ResolveWriteTarget(
		Dictionary<string, JsonElement>? bag, string? processName, string? processUid) =>
		ResolveIdentity(bag, processName, processUid, null, readOnly: false);

	/// <summary>
	/// The one identity rule of the process family. A value-independent synonym is read as its canonical argument
	/// on every tool that addresses an existing process; a generic key (<c>name</c>, <c>process</c>) is read by
	/// the shape of its value only on the read-only tool, and a caption only where the tool takes one. A synonym
	/// next to its canonical argument, or two synonyms of one argument, is refused.
	/// </summary>
	private static IdentityResolution ResolveIdentity(Dictionary<string, JsonElement>? bag,
		string? processName, string? processUid, string? processCaption, bool readOnly) {
		List<string> warnings = [];
		if (bag is null || bag.Count == 0) {
			return new IdentityResolution(processName, processUid, processCaption, bag, warnings, null);
		}
		// target canonical name -> (caller key, value, was it decided by the value's shape)
		Dictionary<string, List<(string Key, string Value, bool ByShape)>> found = new(StringComparer.Ordinal);
		foreach ((string key, JsonElement value) in bag) {
			string? target = IdentityTarget(Normalize(key), value, readOnly, out bool byShape, out string? text);
			if (target is null) {
				continue;
			}
			if (!found.TryGetValue(target, out List<(string, string, bool)>? list)) {
				list = [];
				found[target] = list;
			}
			list.Add((key, text!, byShape));
		}
		if (found.Count == 0) {
			return new IdentityResolution(processName, processUid, processCaption, bag, warnings, null);
		}
		string nothingRan = readOnly ? "Nothing was read." : "Nothing was changed.";
		foreach ((string target, List<(string Key, string Value, bool ByShape)> list) in found) {
			string? canonical = target switch {
				ProcessNameKey => processName,
				ProcessUidKey => processUid,
				_ => processCaption
			};
			if (!string.IsNullOrWhiteSpace(canonical) || list.Count > 1) {
				string shown = string.Join(" and ", list.Select(item => $"'{McpToolArgumentSupport.DescribeCallerKey(item.Key)}'"));
				return new IdentityResolution(processName, processUid, processCaption, bag, warnings,
					$"Ambiguous process identity: {shown} and '{target}' all name the process. "
					+ $"Send the identity once, as '{target}'. {nothingRan}");
			}
		}
		HashSet<string> consumed = new(StringComparer.Ordinal);
		foreach ((string target, List<(string Key, string Value, bool ByShape)> list) in found) {
			(string key, string value, bool byShape) = list[0];
			consumed.Add(key);
			switch (target) {
				case ProcessNameKey: processName = value; break;
				case ProcessUidKey: processUid = value; break;
				default: processCaption = value; break;
			}
			warnings.Add(byShape
				? $"Argument '{McpToolArgumentSupport.DescribeCallerKey(key)}' was read as '{target}' because of its value; "
					+ "send 'process-name', 'process-uid' or 'process-caption' explicitly in future calls."
				: AcceptedNote(key, target));
		}
		return new IdentityResolution(processName, processUid, processCaption, RemainingBag(bag, consumed),
			warnings, null);
	}

	/// <summary>The canonical identity argument a key names on this kind of tool, or null when it names none.</summary>
	private static string? IdentityTarget(string normalized, JsonElement value, bool readOnly, out bool byShape,
		out string? text) {
		byShape = readOnly && DescribeGenericAliases.Contains(normalized);
		text = null;
		string? target = byShape ? null : DescribeSpecificAliases.GetValueOrDefault(normalized);
		if (!byShape && (target is null || (!readOnly && target == ProcessCaptionKey))) {
			return null;
		}
		string? raw = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
		if (string.IsNullOrWhiteSpace(raw)) {
			// Not a usable identity: left in the bag so the ordinary unknown-argument refusal names it.
			return null;
		}
		text = raw.Trim();
		return target ?? TargetByShape(text);
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
		[CaptionKey] = CaptionKey,
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
		CreateResolution resolved = ResolveCreateWrappers(args);
		return resolved.Error is null ? UnwrapNestedDescriptor(resolved) : resolved;
	}

	private const int MaxDescriptorNesting = 3;

	/// <summary>
	/// A descriptor wrapped twice - <c>{"descriptor":{"descriptor":{...}}}</c>, or under a wrapper synonym such as
	/// <c>process</c> - is unwrapped when the wrapper is the descriptor's only member, because the process then has
	/// exactly one reading. A wrapper next to other fields has none, and is refused.
	/// </summary>
	private static CreateResolution UnwrapNestedDescriptor(CreateResolution resolved) {
		JsonElement descriptor = resolved.Args.Descriptor;
		if (descriptor.ValueKind == JsonValueKind.String && !TryParseObject(descriptor, out descriptor)) {
			return resolved;
		}
		List<string> wrappers = [];
		// wrappers.Count is the nesting depth reached so far; the loop ends when an object holds no wrapper.
		while (descriptor.ValueKind == JsonValueKind.Object) {
			int depth = wrappers.Count;
			List<JsonProperty> members = descriptor.EnumerateObject().ToList();
			List<JsonProperty> nested = members.Where(member => IsDescriptorWrapper(member.Name)).ToList();
			if (nested.Count == 0) {
				break;
			}
			string shown = $"'{McpToolArgumentSupport.DescribeCallerKey(nested[0].Name)}'";
			if (depth >= MaxDescriptorNesting) {
				return resolved with { Error = $"'descriptor' is nested more than {MaxDescriptorNesting} levels deep, so it is "
					+ "not clear which object is the process. Send the process fields directly inside 'descriptor'. "
					+ "Nothing was built." };
			}
			if (members.Count > 1) {
				return resolved with { Error = $"Ambiguous descriptor: 'descriptor' holds {shown} next to other fields, so "
					+ "it is not clear which object is the process. Send the process fields (name, caption, packageName, "
					+ "elements, flows, ...) directly inside 'descriptor'. Nothing was built." };
			}
			JsonElement inner = nested[0].Value;
			if (inner.ValueKind == JsonValueKind.String && !TryParseObject(inner, out inner)) {
				inner = nested[0].Value;
			}
			if (inner.ValueKind != JsonValueKind.Object) {
				return resolved with { Error = $"'descriptor' holds only {shown}, which is not a process object. Send the "
					+ "process fields directly inside 'descriptor'. Nothing was built." };
			}
			if (!inner.EnumerateObject().Any()) {
				// {"descriptor":{"descriptor":{}}}: unwrapping it would hand the build an empty process.
				return resolved with { Error = $"'descriptor' holds only {shown}, and it is empty: there is no process "
					+ "to build. Nothing was built. " + ToolMiniContract.ForCreateBusinessProcess(["descriptor"]) };
			}
			descriptor = inner;
			wrappers.Add(shown);
		}
		if (wrappers.Count == 0) {
			return resolved;
		}
		List<string> warnings = [.. resolved.Warnings,
			$"'descriptor' held only {string.Join(" inside ", wrappers)}; the inner object was used as the process. "
			+ "Send the process fields directly inside 'descriptor' in future calls."];
		return new CreateResolution(resolved.Args with { Descriptor = descriptor.Clone() }, warnings, null);
	}

	private static bool IsDescriptorWrapper(string name) {
		string key = Normalize(name);
		return key == "descriptor" || DescriptorWrapperAliases.Contains(key);
	}

	/// <summary>Reads a string holding a JSON object; false for anything else, including a string that is not valid text.</summary>
	private static bool TryParseObject(JsonElement value, out JsonElement element) {
		element = default;
		try {
			string? text = value.GetString();
			if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('{')) {
				return false;
			}
			using JsonDocument document = JsonDocument.Parse(text);
			element = document.RootElement.Clone();
			return true;
		} catch (Exception exception) when (exception is JsonException or InvalidOperationException) {
			// InvalidOperationException: a lone surrogate cannot be read as text; the ordinary refusal names it.
			return false;
		}
	}

	private static CreateResolution ResolveCreateWrappers(CreateBusinessProcessArgs args) {
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
		// Two spellings of one descriptor field (packageName and package_name, elements and Elements) have no single
		// reading: wrapping both would let one silently overwrite the other.
		List<string> repeated = flat.GroupBy(Normalize).Where(group => group.Count() > 1).SelectMany(group => group).ToList();
		if (repeated.Count > 0) {
			return new CreateResolution(args, warnings, AmbiguousDescriptor(repeated));
		}
		if (wrappers.Count == 1 && IsIdentityText(bag[wrappers[0]])) {
			return new CreateResolution(args, warnings,
				$"'{McpToolArgumentSupport.DescribeCallerKey(wrappers[0])}' holds text, not a process object. On "
				+ "create-business-process the process is the 'descriptor' object; a process NAME is what "
				+ "describe-business-process and modify-business-process take (process-name). Nothing was built.");
		}
		// A flat packageName next to the top-level package-name override: in a canonical call the override is
		// meant to win, but in a flat call both plainly mean "the package", so applying the override could put the
		// process in the other one. This is a write, so it is refused rather than resolved.
		if (wrappers.Count == 0 && !string.IsNullOrWhiteSpace(args.PackageName)
			&& flat.FirstOrDefault(key => Normalize(key) == "packagename") is { } flatPackage) {
			return new CreateResolution(args, warnings,
				$"Ambiguous package: '{McpToolArgumentSupport.DescribeCallerKey(flatPackage)}' and 'package-name' both "
				+ "name the package of a descriptor sent as flat fields. Send the process as the 'descriptor' object "
				+ "with its packageName, and 'package-name' only to override it. Nothing was built.");
		}
		(JsonElement descriptor, HashSet<string> consumed) = wrappers.Count == 1
			? TakeWrappedDescriptor(bag, wrappers[0], warnings)
			: WrapFlatDescriptor(bag, flat, warnings);
		return new CreateResolution(
			args with { Descriptor = descriptor, ExtensionData = RemainingBag(bag, consumed) },
			warnings, null);
	}

	/// <summary>
	/// True for a string that is not JSON at all - a process name or caption, not a document. A string that starts
	/// like JSON but does not parse is a broken document, and keeps the ordinary parse refusal.
	/// </summary>
	private static bool IsIdentityText(JsonElement value) =>
		value.ValueKind == JsonValueKind.String && !LooksLikeJson(value) && !TryParseObject(value, out _);

	private static bool LooksLikeJson(JsonElement value) {
		try {
			string text = (value.GetString() ?? string.Empty).TrimStart();
			return text.StartsWith('{') || text.StartsWith('[');
		} catch (InvalidOperationException) {
			return false;
		}
	}

	private static (JsonElement Descriptor, HashSet<string> Consumed) TakeWrappedDescriptor(
		Dictionary<string, JsonElement> bag, string key, List<string> warnings) {
		warnings.Add(AcceptedNote(key, "descriptor"));
		return (bag[key], [key]);
	}

	private static (JsonElement Descriptor, HashSet<string> Consumed) WrapFlatDescriptor(
		Dictionary<string, JsonElement> bag, List<string> flat, List<string> warnings) {
		JsonObject wrapped = [];
		HashSet<string> consumed = [];
		foreach (string key in flat) {
			wrapped[DescriptorFields[Normalize(key)]] = JsonNode.Parse(bag[key].GetRawText());
			consumed.Add(key);
		}
		warnings.Add("Descriptor fields (" + string.Join(", ", flat.Select(key =>
				$"'{McpToolArgumentSupport.DescribeCallerKey(key)}'")) + ") were wrapped into 'descriptor'; "
			+ "send them inside 'descriptor' in future calls.");
		return (JsonDocument.Parse(wrapped.ToJsonString()).RootElement.Clone(), consumed);
	}

	private static Dictionary<string, JsonElement>? RemainingBag(
		Dictionary<string, JsonElement> bag, HashSet<string> consumed) {
		Dictionary<string, JsonElement> remaining = bag.Where(pair => !consumed.Contains(pair.Key))
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
		return remaining.Count == 0 ? null : remaining;
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
		"name", CaptionKey, "packagename", "parameters", "mappings", "usings", "methods", "istracing", "description"
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
		string? ambiguity = DescribeGraphAmbiguity(args, wrappers, flat);
		if (ambiguity is not null) {
			return Refuse(args, warnings, ambiguity);
		}

		Dictionary<string, JsonElement> source = [];
		string? error = wrappers.Count == 1
			? ReadWrapperGraph(bag, wrappers[0], source, out string origin)
			: ReadFlatGraph(bag, flat, source, out origin);
		if (error is not null) {
			return Refuse(args, warnings, error);
		}
		warnings.Add(wrappers.Count == 1
			? $"Argument '{origin}' was accepted as the graph; send 'nodes' and 'edges' at the top level in future calls."
			: $"{origin} were accepted as 'nodes' and 'edges'; send 'nodes' and 'edges' in future calls.");
		if (source.ContainsKey(ElementsKey) || source.ContainsKey(FlowsKey)) {
			warnings.Add(DescriptorOnlyGraphCheckedNote);
		}
		if (source.ContainsKey("nodes") && source.ContainsKey(ElementsKey)
			|| source.ContainsKey("edges") && source.ContainsKey(FlowsKey)) {
			return Refuse(args, warnings, $"{origin} carries both 'nodes' and 'elements' (or 'edges' and 'flows'); send one.");
		}
		return BuildGraphArgs(args, bag, source, wrappers.Count == 1 ? wrappers : flat, origin, warnings);
	}

	/// <summary>
	/// Said whenever the graph was read from a descriptor's elements/flows: a passing graph is not a passing
	/// descriptor, and the natural next step after "accepted" is to build it.
	/// </summary>
	internal const string DescriptorOnlyGraphCheckedNote =
		"Only the graph shape was checked: element name and type, and flow source, target, kind, condition and "
		+ "results. Element configuration (captions, parameters, mappings, filters, ...) was NOT checked; "
		+ "create-business-process checks it when it builds.";

	private static string? DescribeGraphAmbiguity(ValidateProcessGraphArgs args, List<string> wrappers, List<string> flat) {
		bool canonicalSupplied = args.Nodes is { Count: > 0 } || args.Edges is { Count: > 0 };
		if (!canonicalSupplied && wrappers.Count <= 1 && !(wrappers.Count == 1 && flat.Count > 0)) {
			return null;
		}
		string clash = string.Join(", ", wrappers.Concat(flat).Select(key => $"'{McpToolArgumentSupport.DescribeCallerKey(key)}'"));
		string canonical = canonicalSupplied ? " and 'nodes'/'edges'" : string.Empty;
		return $"Ambiguous graph: {clash}{canonical} all carry the graph, and they cannot all be it. "
			+ "Send the graph once, as 'nodes' and 'edges'.";
	}

	private static GraphResolution BuildGraphArgs(
		ValidateProcessGraphArgs args, Dictionary<string, JsonElement> bag, Dictionary<string, JsonElement> source,
		List<string> consumedKeys, string origin, List<string> warnings) {
		try {
			List<ProcessGraphNodeArg>? nodes = ReadNodes(source);
			List<ProcessGraphEdgeArg>? edges = ReadEdges(source, out string? edgeError);
			if (edgeError is not null) {
				return Refuse(args, warnings, edgeError);
			}
			return new GraphResolution(
				args with { Nodes = nodes, Edges = edges, ExtensionData = RemainingBag(bag, [.. consumedKeys]) },
				warnings, null);
		} catch (JsonException exception) {
			return Refuse(args, warnings, $"{origin} is not a valid graph: {SensitiveErrorTextRedactor.Redact(exception.Message)}");
		}
	}

	/// <summary>Reads the graph members of a wrapper (<c>graph</c>, <c>descriptor</c>, ...) into <paramref name="source"/>; returns a refusal or null.</summary>
	private static string? ReadWrapperGraph(
		Dictionary<string, JsonElement> bag, string key, Dictionary<string, JsonElement> source, out string origin) {
		origin = McpToolArgumentSupport.DescribeCallerKey(key);
		JsonElement value = bag[key];
		if (value.ValueKind == JsonValueKind.String) {
			if (!TryParseObject(value, out JsonElement parsed)) {
				if (LooksLikeJson(value)) {
					return $"'{origin}' is a string that is not valid JSON.";
				}
				return $"'{origin}' holds text, not a graph object. On validate-process-graph it can only be the graph "
					+ "('nodes' and 'edges', or a descriptor's 'elements' and 'flows'); this tool never reads a process "
					+ "from the environment - describe-business-process does, by process-name.";
			}
			value = parsed;
		}
		if (value.ValueKind != JsonValueKind.Object) {
			return $"'{origin}' must be a JSON object holding 'nodes' and 'edges'.";
		}
		List<JsonProperty> properties = value.EnumerateObject().ToList();
		List<JsonProperty> members = properties.Where(item => IsGraphMember(Normalize(item.Name))).ToList();
		IGrouping<string, JsonProperty>? repeated = members.GroupBy(item => Normalize(item.Name)).FirstOrDefault(group => group.Count() > 1);
		if (repeated is not null) {
			return $"'{origin}' repeats '{repeated.ElementAt(1).Name}'.";
		}
		members.ForEach(item => source[Normalize(item.Name)] = item.Value);
		List<string> foreign = properties
			.Where(item => !IsGraphMember(Normalize(item.Name)) && !NonGraphDescriptorFields.Contains(Normalize(item.Name)))
			.Select(item => $"'{McpToolArgumentSupport.DescribeCallerKey(item.Name)}'")
			.ToList();
		return foreign.Count == 0
			? null
			: $"Unknown keys inside '{origin}': {McpToolArgumentSupport.JoinCallerKeys(foreign)}. "
				+ "Valid: nodes, edges (or the elements and flows of a descriptor).";
	}

	private static bool IsGraphMember(string normalized) => normalized is "nodes" or ElementsKey or "edges" or FlowsKey;

	/// <summary>Reads flat top-level <c>elements</c>/<c>flows</c> into <paramref name="source"/>; returns a refusal or null.</summary>
	private static string? ReadFlatGraph(
		Dictionary<string, JsonElement> bag, List<string> flat, Dictionary<string, JsonElement> source, out string origin) {
		origin = string.Join(" and ", flat.Select(key => $"'{McpToolArgumentSupport.DescribeCallerKey(key)}'"));
		IGrouping<string, string>? repeated = flat.GroupBy(Normalize).FirstOrDefault(group => group.Count() > 1);
		if (repeated is not null) {
			return $"{origin} repeats '{repeated.ElementAt(1)}'.";
		}
		flat.ForEach(key => source[Normalize(key)] = bag[key]);
		return null;
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
