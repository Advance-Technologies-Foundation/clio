using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver;

internal sealed class ClientUnitJsConflictMarkerFormatter : IAutomergeConflictFormatter
{
	private static readonly JsonSerializerOptions JsonWriteOptions = new()
	{
		WriteIndented = true
	};

	private static readonly JsonDocumentOptions JsonReadOptions = new()
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip
	};

	private static readonly SectionDefinition[] Sections =
	[
		new("viewConfigDiff", "SCHEMA_VIEW_CONFIG_DIFF", SectionKind.PatchArray),
		new("viewModelConfigDiff", "SCHEMA_VIEW_MODEL_CONFIG_DIFF", SectionKind.PatchArray),
		new("modelConfigDiff", "SCHEMA_MODEL_CONFIG_DIFF", SectionKind.PatchArray),
		new("handlers", "SCHEMA_HANDLERS", SectionKind.PatchArray),
		new("converters", "SCHEMA_CONVERTERS", SectionKind.DeepObject),
		new("validators", "SCHEMA_VALIDATORS", SectionKind.DeepObject)
	];

	public bool CanFormat(MergeRequest request, MergeResult result)
	{
		return request.FileType == ConflictFileType.ClientUnitJs &&
		       !string.IsNullOrWhiteSpace(result.MergedContent);
	}

	public string? TryFormat(MergeRequest request, MergeResult result, IReadOnlyCollection<string> conflictTokens)
	{
		if (string.IsNullOrWhiteSpace(result.MergedContent))
		{
			return null;
		}

		return TryFormatClientUnitJs(
			request.Base,
			request.Local,
			request.Remote,
			result.MergedContent!,
			conflictTokens);
	}

	private static string? TryFormatClientUnitJs(
		string baseContent,
		string localContent,
		string remoteContent,
		string mergedContent,
		IReadOnlyCollection<string> conflictTokens)
	{
		var source = mergedContent;
		var changed = false;
		var newline = DetectNewLine(source);

		foreach (var section in Sections)
		{
			var sectionConflicts = conflictTokens
				.Where(token => token.StartsWith(section.Name + ":", StringComparison.Ordinal))
				.ToArray();
			if (sectionConflicts.Length == 0)
			{
				continue;
			}

			var baseRaw = ClientUnitSectionLocator.TryExtract(baseContent, section.Marker, out var baseSlice)
				? baseSlice.Json
				: EmptySectionJson(section);
			var remoteRaw = ClientUnitSectionLocator.TryExtract(remoteContent, section.Marker, out var remoteSlice)
				? remoteSlice.Json
				: baseRaw;
			if (!ClientUnitSectionLocator.TryExtract(localContent, section.Marker, out var localSlice) ||
			    !ClientUnitSectionLocator.TryExtract(source, section.Marker, out var mergedSlice))
			{
				continue;
			}

			var formattedSection = FormatSection(
				section,
				baseRaw,
				localSlice.Json,
				remoteRaw,
				mergedSlice.Json,
				sectionConflicts,
				newline,
				GetLineIndent(source, mergedSlice.Start));
			if (string.IsNullOrWhiteSpace(formattedSection))
			{
				continue;
			}

				source = ReplaceRange(source, mergedSlice.Start, mergedSlice.Length, formattedSection!);
			changed = true;
		}

		return changed ? source : null;
	}

	private static string? FormatSection(
		SectionDefinition section,
		string baseRaw,
		string localRaw,
		string remoteRaw,
		string mergedRaw,
		IReadOnlyCollection<string> sectionConflicts,
		string newline,
		string lineIndent)
	{
		var hasRawConflict = sectionConflicts.Contains($"{section.Name}:raw", StringComparer.Ordinal);
		if (section.Kind == SectionKind.DeepObject || hasRawConflict)
		{
			return FormatWholeSection(localRaw, remoteRaw, newline, lineIndent);
		}

		if (!TryParseArray(baseRaw, out var baseArray) ||
		    !TryParseArray(localRaw, out var localArray) ||
		    !TryParseArray(remoteRaw, out var remoteArray) ||
		    !TryParseArray(mergedRaw, out var mergedArray))
		{
			return FormatWholeSection(localRaw, remoteRaw, newline, lineIndent);
		}

		var conflictTokens = sectionConflicts
			.Select(token => token.Substring(section.Name.Length + 1))
			.Where(static token => !string.Equals(token, "raw", StringComparison.Ordinal))
			.ToArray();

		var displayMergedArray = (JsonArray)mergedArray.DeepClone();
		var conflictPaths = new HashSet<string>(StringComparer.Ordinal);
		foreach (var conflictToken in conflictTokens)
		{
			foreach (var conflictPath in ExpandConflictToken(
						 conflictToken,
						 baseArray,
						 localArray,
						 remoteArray,
						 displayMergedArray))
			{
				conflictPaths.Add(conflictPath);
			}
		}

		if (conflictPaths.Count == 0)
		{
			return FormatWholeSection(localRaw, remoteRaw, newline, lineIndent);
		}

		var rendered = JsonConflictMarkerSerializer.Serialize(
			baseArray,
			displayMergedArray,
			localArray,
			remoteArray,
			conflictPaths.ToArray(),
			new JsonConflictRenderOptions(
				"  ",
				"\n",
				new JsonSerializerOptions(),
				ArrayItemKeyResolver: static (arrayPath, item) => string.Equals(arrayPath, "$", StringComparison.Ordinal)
					? BuildKey(item)
					: null));

		return ApplyEmbeddedIndent(ConvertLeadingSpacesToTabs(rendered), newline, lineIndent);
	}

	private static string EmptySectionJson(SectionDefinition section)
	{
		return section.Kind == SectionKind.DeepObject ? "{}" : "[]";
	}

	private static IEnumerable<string> ExpandConflictToken(
		string token,
		JsonArray baseArray,
		JsonArray localArray,
		JsonArray remoteArray,
		JsonArray mergedArray)
	{
		var (key, nestedPath) = SplitConflictToken(token);
		var itemPath = $"$[{key}]";
		var baseItem = FindArrayItemByKey(baseArray, key);
		var localItem = FindArrayItemByKey(localArray, key);
		var remoteItem = FindArrayItemByKey(remoteArray, key);
		var operation = GetString(localItem, "operation")
		                ?? GetString(remoteItem, "operation")
		                ?? GetString(baseItem, "operation");

		var analysis = string.Equals(operation, "merge", StringComparison.OrdinalIgnoreCase)
			? AnalyzeMergeNode(
				new ConflictNodeState(baseItem is not null, baseItem),
				new ConflictNodeState(localItem is not null, localItem),
				new ConflictNodeState(remoteItem is not null, remoteItem),
				itemPath)
			: AnalyzeOverlayNode(
				new ConflictNodeState(localItem is not null, localItem),
				new ConflictNodeState(remoteItem is not null, remoteItem),
				itemPath);

		if (analysis.MergedState.Exists)
		{
			TrySetArrayItemByKey(mergedArray, key, analysis.MergedState.Node);
		}

		if (!string.IsNullOrWhiteSpace(nestedPath))
		{
			yield return itemPath + nestedPath;
			yield break;
		}

		if (analysis.ConflictPaths.Count == 0)
		{
			yield return itemPath;
			yield break;
		}

		foreach (var conflictPath in analysis.ConflictPaths)
		{
			yield return conflictPath;
		}
	}

	private static (string Key, string? NestedPath) SplitConflictToken(string token)
	{
		var separatorIndex = token.IndexOf("::", StringComparison.Ordinal);
		if (separatorIndex < 0)
		{
			return (token, null);
		}

		var key = token.Substring(0, separatorIndex);
		var nestedPath = token.Substring(separatorIndex + 2);
		if (string.IsNullOrWhiteSpace(nestedPath))
		{
			return (key, null);
		}

		return (key, nestedPath.StartsWith(".", StringComparison.Ordinal) || nestedPath.StartsWith("[", StringComparison.Ordinal)
			? nestedPath
			: "." + nestedPath);
	}

	private static ConflictAnalysis AnalyzeMergeNode(
		ConflictNodeState baseState,
		ConflictNodeState localState,
		ConflictNodeState remoteState,
		string path)
	{
		if (NodesEqual(localState, remoteState))
		{
			return ConflictAnalysis.NoConflicts(CloneState(localState));
		}

		if (NodesEqual(localState, baseState))
		{
			return ConflictAnalysis.NoConflicts(CloneState(remoteState));
		}

		if (NodesEqual(remoteState, baseState))
		{
			return ConflictAnalysis.NoConflicts(CloneState(localState));
		}

		if (localState.Node is JsonObject localObject &&
		    remoteState.Node is JsonObject remoteObject)
		{
			var baseObject = baseState.Exists ? baseState.Node as JsonObject : null;
			var mergedObject = new JsonObject();
			var nestedConflicts = new HashSet<string>(StringComparer.Ordinal);

			foreach (var propertyName in EnumerateObjectKeys(baseObject, localObject, remoteObject))
			{
				JsonNode? baseValue = null;
				JsonNode? localValue = null;
				JsonNode? remoteValue = null;

				var hasBase = baseObject?.TryGetPropertyValue(propertyName, out baseValue) == true;
				var hasLocal = localObject.TryGetPropertyValue(propertyName, out localValue);
				var hasRemote = remoteObject.TryGetPropertyValue(propertyName, out remoteValue);

				var childAnalysis = AnalyzeMergeNode(
					new ConflictNodeState(hasBase, baseValue),
					new ConflictNodeState(hasLocal, localValue),
					new ConflictNodeState(hasRemote, remoteValue),
					$"{path}.{propertyName}");

				if (childAnalysis.MergedState.Exists)
				{
					mergedObject[propertyName] = childAnalysis.MergedState.Node?.DeepClone();
				}

				foreach (var conflictPath in childAnalysis.ConflictPaths)
				{
					nestedConflicts.Add(conflictPath);
				}
			}

			return new ConflictAnalysis(new ConflictNodeState(true, mergedObject), nestedConflicts.ToArray());
		}

		if (baseState.Exists)
		{
			if (localState.Exists && remoteState.Exists)
			{
				return ConflictAnalysis.WithConflict(CloneState(localState), path);
			}

			if (!localState.Exists && !remoteState.Exists)
			{
				return ConflictAnalysis.NoConflicts(ConflictNodeState.Missing);
			}

			if (localState.Exists)
			{
				return NodesEqual(localState, baseState)
					? ConflictAnalysis.NoConflicts(ConflictNodeState.Missing)
					: ConflictAnalysis.WithConflict(CloneState(localState), path);
			}

			return NodesEqual(remoteState, baseState)
				? ConflictAnalysis.NoConflicts(ConflictNodeState.Missing)
				: ConflictAnalysis.WithConflict(ConflictNodeState.Missing, path);
		}

		if (localState.Exists && remoteState.Exists)
		{
			return ConflictAnalysis.WithConflict(CloneState(localState), path);
		}

		if (localState.Exists)
		{
			return ConflictAnalysis.NoConflicts(CloneState(localState));
		}

		if (remoteState.Exists)
		{
			return ConflictAnalysis.NoConflicts(CloneState(remoteState));
		}

		return ConflictAnalysis.NoConflicts(ConflictNodeState.Missing);
	}

	private static ConflictAnalysis AnalyzeOverlayNode(
		ConflictNodeState localState,
		ConflictNodeState remoteState,
		string path)
	{
		if (NodesEqual(localState, remoteState))
		{
			return ConflictAnalysis.NoConflicts(CloneState(localState));
		}

		if (localState.Node is JsonObject localObject &&
		    remoteState.Node is JsonObject remoteObject)
		{
			var mergedObject = new JsonObject();
			var nestedConflicts = new HashSet<string>(StringComparer.Ordinal);

			foreach (var propertyName in EnumerateObjectKeys(localObject, remoteObject))
			{
				JsonNode? localValue = null;
				JsonNode? remoteValue = null;

				var hasLocal = localObject.TryGetPropertyValue(propertyName, out localValue);
				var hasRemote = remoteObject.TryGetPropertyValue(propertyName, out remoteValue);

				var childAnalysis = AnalyzeOverlayNode(
					new ConflictNodeState(hasLocal, localValue),
					new ConflictNodeState(hasRemote, remoteValue),
					$"{path}.{propertyName}");

				if (childAnalysis.MergedState.Exists)
				{
					mergedObject[propertyName] = childAnalysis.MergedState.Node?.DeepClone();
				}

				foreach (var conflictPath in childAnalysis.ConflictPaths)
				{
					nestedConflicts.Add(conflictPath);
				}
			}

			return new ConflictAnalysis(new ConflictNodeState(true, mergedObject), nestedConflicts.ToArray());
		}

		if (localState.Exists && remoteState.Exists)
		{
			return ConflictAnalysis.WithConflict(CloneState(localState), path);
		}

		if (localState.Exists)
		{
			return ConflictAnalysis.NoConflicts(CloneState(localState));
		}

		if (remoteState.Exists)
		{
			return ConflictAnalysis.NoConflicts(CloneState(remoteState));
		}

		return ConflictAnalysis.NoConflicts(ConflictNodeState.Missing);
	}

	private static bool NodesEqual(ConflictNodeState left, ConflictNodeState right)
	{
		if (left.Exists != right.Exists)
		{
			return false;
		}

		return !left.Exists || JsonNode.DeepEquals(left.Node, right.Node);
	}

	private static ConflictNodeState CloneState(ConflictNodeState state)
	{
		return state.Exists
			? new ConflictNodeState(true, state.Node?.DeepClone())
			: ConflictNodeState.Missing;
	}

	private static IEnumerable<string> EnumerateObjectKeys(params JsonObject?[] objects)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var obj in objects)
		{
			if (obj is null)
			{
				continue;
			}

			foreach (var property in obj)
			{
				if (seen.Add(property.Key))
				{
					yield return property.Key;
				}
			}
		}
	}

	private static string FormatWholeSection(string localRaw, string remoteRaw, string newline, string lineIndent)
	{
		var localText = ConvertLeadingSpacesToTabs(PrettyJsonOrRaw(localRaw));
		var remoteText = ConvertLeadingSpacesToTabs(PrettyJsonOrRaw(remoteRaw));
		var block = string.Join(
			"\n",
			"<<<<<<< Local",
			NormalizeLineEndings(localText),
			"=======",
			NormalizeLineEndings(remoteText),
			">>>>>>> Remote");

		return ApplyEmbeddedIndent(block, newline, lineIndent);
	}

	private static string PrettyJsonOrRaw(string raw)
	{
		if (!TryParse(raw, out var node) || node is null)
		{
			return raw;
		}

		return BoundedJsonSerializer.Serialize(node, JsonWriteOptions);
	}

	private static string ApplyEmbeddedIndent(string text, string newline, string lineIndent)
	{
		string normalizedText = NormalizeLineEndings(text);
		var lines = normalizedText.Split('\n');
		int indentCount = lines.Skip(1).Count(line => !IsConflictMarkerLine(line));
		OutputBudget.EnsureIndentedTextFits(
			normalizedText,
			newline,
			lines.Length - 1,
			lineIndent,
			indentCount);
		for (var i = 1; i < lines.Length; i++)
		{
			if (IsConflictMarkerLine(lines[i]))
			{
				continue;
			}

			lines[i] = lineIndent + lines[i];
		}

		return string.Join(newline, lines);
	}

	private static bool IsConflictMarkerLine(string line)
	{
		return string.Equals(line, "<<<<<<< Local", StringComparison.Ordinal) ||
		       string.Equals(line, "=======", StringComparison.Ordinal) ||
		       string.Equals(line, ">>>>>>> Remote", StringComparison.Ordinal);
	}

	private static bool TryParseArray(string json, out JsonArray array)
	{
		if (!TryParse(json, out var node) || node is not JsonArray parsedArray)
		{
			array = new JsonArray();
			return false;
		}

		array = parsedArray;
		return true;
	}

	private static bool TryParse(string json, out JsonNode? node)
	{
		try
		{
			node = JsonNode.Parse(json, documentOptions: JsonReadOptions);
			return node is not null;
		}
		catch (JsonException)
		{
			node = null;
			return false;
		}
	}

	private static string ConvertLeadingSpacesToTabs(string value)
	{
		var lines = NormalizeLineEndings(value).Split('\n');
		for (var i = 0; i < lines.Length; i++)
		{
			if (IsConflictMarkerLine(lines[i]))
			{
				continue;
			}

			var line = lines[i];
			var spaceCount = 0;
			while (spaceCount < line.Length && line[spaceCount] == ' ')
			{
				spaceCount++;
			}

			if (spaceCount == 0)
			{
				continue;
			}

			var tabCount = spaceCount / 2;
			var remainder = spaceCount % 2;
			lines[i] = new string('\t', tabCount) + new string(' ', remainder) + line.Substring(spaceCount);
		}

		return string.Join("\n", lines);
	}

	private static void TrySetArrayItemByKey(JsonArray array, string key, JsonNode? node)
	{
		for (var index = 0; index < array.Count; index++)
		{
			if (array[index] is JsonObject item &&
			    string.Equals(BuildKey(item), key, StringComparison.Ordinal))
			{
				array[index] = node?.DeepClone();
				return;
			}
		}
	}

	private static string NormalizeLineEndings(string value) =>
		value.Replace("\r\n", "\n", StringComparison.Ordinal);

	private static string DetectNewLine(string content) =>
		content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

	private static string GetLineIndent(string source, int index)
	{
		var lineStart = source.LastIndexOf('\n', Math.Max(0, index - 1));
		lineStart = lineStart < 0 ? 0 : lineStart + 1;

		var current = lineStart;
		while (current < source.Length)
		{
			var ch = source[current];
			if (ch is not (' ' or '\t'))
			{
				break;
			}

			current++;
		}

		return source.Substring(lineStart, current - lineStart);
	}

	private static string ReplaceRange(string source, int start, int length, string replacement)
	{
		var sb = new StringBuilder(source.Length - length + replacement.Length);
		sb.Append(source, 0, start);
		sb.Append(replacement);
		sb.Append(source, start + length, source.Length - start - length);
		return sb.ToString();
	}

	private static string BuildKey(JsonNode? node)
	{
		if (node is not JsonObject obj)
		{
			return "__raw__" + (node?.ToJsonString() ?? "null");
		}

		var op = GetString(obj, "operation");
		var path = GetPath(obj);
		if (!string.IsNullOrWhiteSpace(path))
		{
			return $"{(string.IsNullOrWhiteSpace(op) ? "_" : op)}|path:{path}";
		}

		var name = GetString(obj, "name");
		if (!string.IsNullOrWhiteSpace(name))
		{
				return string.IsNullOrWhiteSpace(op) ? name! : $"{op}|{name}";
		}

		var request = GetString(obj, "request");
		if (!string.IsNullOrWhiteSpace(request))
		{
			return $"{(string.IsNullOrWhiteSpace(op) ? "_" : op)}|request:{request}|name:{name ?? string.Empty}";
		}

		return "__raw__" + obj.ToJsonString();
	}

	private static JsonObject? FindArrayItemByKey(JsonArray array, string key)
	{
		foreach (var item in array.OfType<JsonObject>())
		{
			if (string.Equals(BuildKey(item), key, StringComparison.Ordinal))
			{
				return item;
			}
		}

		return null;
	}

	private static string? GetPath(JsonObject obj)
	{
		if (!obj.TryGetPropertyValue("path", out var pathNode) || pathNode is null)
		{
			return null;
		}

		if (pathNode is JsonValue value && value.TryGetValue<string>(out var str))
		{
			return str;
		}

		if (pathNode is not JsonArray array)
		{
			return null;
		}

		var parts = new List<string>(array.Count);
		foreach (var part in array)
		{
			if (part is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var current))
			{
				parts.Add(current);
			}
			else
			{
				parts.Add(part?.ToJsonString() ?? "null");
			}
		}

		return string.Join("/", parts);
	}

	private static string? GetString(JsonObject? obj, string propertyName)
	{
		if (obj is null || !obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue value)
		{
			return null;
		}

		return value.TryGetValue<string>(out var result) ? result : null;
	}

	private enum SectionKind
	{
		PatchArray,
		DeepObject
	}

	private readonly record struct SectionDefinition(string Name, string Marker, SectionKind Kind);
	private readonly record struct ConflictNodeState(bool Exists, JsonNode? Node)
	{
		public static ConflictNodeState Missing => new(false, null);
	}

	private readonly record struct ConflictAnalysis(
		ConflictNodeState MergedState,
		IReadOnlyCollection<string> ConflictPaths)
	{
		public static ConflictAnalysis NoConflicts(ConflictNodeState mergedState)
		{
			return new ConflictAnalysis(mergedState, Array.Empty<string>());
		}

		public static ConflictAnalysis WithConflict(ConflictNodeState mergedState, string path)
		{
			return new ConflictAnalysis(mergedState, [path]);
		}
	}
}
