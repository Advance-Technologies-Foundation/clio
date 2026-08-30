using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver.Strategies;

internal sealed class ClientUnitJsMergeStrategy : IMergeStrategy
{
	private const int MaxJsonTokensPerSection = 25_000;
	private static readonly StringComparer KeyComparer = StringComparer.Ordinal;
	private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };
	private static readonly JsonDocumentOptions JsonReadOptions = new()
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip
	};

	private static readonly SectionDefinition[] Sections =
	[
		new("viewConfigDiff", "SCHEMA_VIEW_CONFIG_DIFF", SectionKind.PatchArray, true),
		new("viewModelConfigDiff", "SCHEMA_VIEW_MODEL_CONFIG_DIFF", SectionKind.PatchArray),
		new("modelConfigDiff", "SCHEMA_MODEL_CONFIG_DIFF", SectionKind.PatchArray),
		new("handlers", "SCHEMA_HANDLERS", SectionKind.PatchArray),
		new("converters", "SCHEMA_CONVERTERS", SectionKind.DeepObject),
		new("validators", "SCHEMA_VALIDATORS", SectionKind.DeepObject)
	];

	public bool CanHandle(ConflictFileType fileType) => fileType == ConflictFileType.ClientUnitJs;

	public MergeResult Merge(MergeRequest request)
	{
		var source = request.Local;
		var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

		var localAdd = new HashSet<string>(KeyComparer);
		var remoteAdd = new HashSet<string>(KeyComparer);
		var localDel = new HashSet<string>(KeyComparer);
		var remoteDel = new HashSet<string>(KeyComparer);
		var conflicts = new HashSet<string>(KeyComparer);

		foreach (var section in Sections)
		{
			var localHasMarker = ContainsMarker(request.Local, section.Marker);
			var remoteHasMarker = ContainsMarker(request.Remote, section.Marker);
			if ((ContainsMarker(request.Base, section.Marker) &&
				 !ClientUnitSectionLocator.TryExtract(request.Base, section.Marker, out _)) ||
				(localHasMarker && !ClientUnitSectionLocator.TryExtract(request.Local, section.Marker, out _)) ||
				(remoteHasMarker && !ClientUnitSectionLocator.TryExtract(request.Remote, section.Marker, out _)))
			{
				return MergeResultFactory.InvalidInput(
					"InvalidClientUnitSection",
					$"Marker '{section.Marker}' must contain exactly one complete expression pair in every stage where it appears.");
			}
			if (localHasMarker == remoteHasMarker)
			{
				continue;
			}

			return MergeResultFactory.UnresolvedConflict(
				"ClientUnitSectionPresenceConflict",
				$"Marker '{section.Marker}' was added or removed on one branch.",
				"js_section_presence_conflict",
				trueConflicts: [$"client-unit-section-presence:{section.Name}"],
				mergedContent: BuildWholeFileConflictMarkers(request.Local, request.Remote));
		}

		var found = false;
		foreach (var section in Sections)
		{
			if (!ClientUnitSectionLocator.TryExtract(request.Local, section.Marker, out var localOriginal))
			{
				if (ContainsMarker(request.Local, section.Marker))
				{
					return MergeResultFactory.InvalidInput(
						"InvalidClientUnitSection",
						$"Marker '{section.Marker}' does not contain one complete array or object expression.");
				}

				continue;
			}

			found = true;
			var baseRaw = ClientUnitSectionLocator.TryExtract(request.Base, section.Marker, out var b) ? b.Json : EmptySectionJson(section);
			var remoteRaw = ClientUnitSectionLocator.TryExtract(request.Remote, section.Marker, out var r) ? r.Json : baseRaw;

			if (!ClientUnitSectionLocator.TryExtract(source, section.Marker, out var localMutable))
			{
				return MergeResultFactory.InvalidInput("SectionNotFound", $"Cannot locate marker '{section.Marker}' in mutable source.");
			}

			var merged = MergeSection(
				section,
				baseRaw,
				localOriginal.Json,
				remoteRaw,
				newline,
				GetLineIndent(source, localMutable.Start),
				localAdd,
				remoteAdd,
				localDel,
				remoteDel,
				conflicts);

			if (!merged.Success)
			{
				return MergeResultFactory.InvalidInput("InvalidClientUnitSection", merged.Error ?? $"Cannot merge '{section.Name}'.");
			}

			source = ReplaceRange(source, localMutable.Start, localMutable.Length, merged.Text);
		}

		if (!found)
		{
			return MergeResultFactory.InvalidInput("ClientUnitMarkersMissing", "No supported SCHEMA_* markers found.");
		}

		if (source.Contains("<<<<<<<", StringComparison.Ordinal) ||
			source.Contains("=======", StringComparison.Ordinal) ||
			source.Contains(">>>>>>>", StringComparison.Ordinal))
		{
			return MergeResultFactory.UnresolvedConflict(
				"ConflictMarkersRemain",
				"Merged JS still contains git conflict markers.",
				"js_marker_json_3way",
				trueConflicts: conflicts,
				mergedContent: source,
				localAdditions: localAdd,
				remoteAdditions: remoteAdd,
				localDeletions: localDel,
				remoteDeletions: remoteDel,
				verificationPassed: false);
		}

		return MergeResultFactory.Resolved(
			source,
			"js_marker_json_3way",
			localAdditions: localAdd,
			remoteAdditions: remoteAdd,
			localDeletions: localDel,
			remoteDeletions: remoteDel,
			trueConflicts: conflicts,
			verificationPassed: true,
			winnerPolicy: "LOCAL");
	}

	private static bool ContainsMarker(string source, string marker) =>
		source.Contains($"/**{marker}*/", StringComparison.Ordinal);

	private static string BuildWholeFileConflictMarkers(string local, string remote) =>
		string.Join("\n", "<<<<<<< Local", local, "=======", remote, ">>>>>>> Remote");

	private static SectionMerge MergeSection(
		SectionDefinition section,
		string baseRaw,
		string localRaw,
		string remoteRaw,
		string newline,
		string lineIndent,
		ISet<string> localAdd,
		ISet<string> remoteAdd,
		ISet<string> localDel,
		ISet<string> remoteDel,
		ISet<string> conflicts)
	{
		if (!IsWithinJsonBudget(baseRaw) || !IsWithinJsonBudget(localRaw) || !IsWithinJsonBudget(remoteRaw))
		{
			return SectionMerge.Fail($"Section '{section.Name}' exceeds the JSON complexity limit.");
		}

		if (section.Kind == SectionKind.DeepObject)
		{
			if (!TryParseObj(baseRaw, out var b) || !TryParseObj(localRaw, out var l) || !TryParseObj(remoteRaw, out var r))
			{
				return SectionMerge.Ok(Raw3Way(section.Name, baseRaw, localRaw, remoteRaw, conflicts));
			}

			var merged = MergeNode(b, l, r);
			if (merged.Conflict)
			{
				conflicts.Add($"{section.Name}:object");
			}

			return SectionMerge.Ok(ToJson(merged.Node!, newline, lineIndent));
		}

		if (!TryParseArray(baseRaw, out var bArr) || !TryParseArray(localRaw, out var lArr) || !TryParseArray(remoteRaw, out var rArr))
		{
			return SectionMerge.Ok(Raw3Way(section.Name, baseRaw, localRaw, remoteRaw, conflicts));
		}

		if (HasDuplicateKeys(bArr) || HasDuplicateKeys(lArr) || HasDuplicateKeys(rArr))
		{
			return SectionMerge.Ok(Raw3Way(section.Name, baseRaw, localRaw, remoteRaw, conflicts));
		}

		var mergedArr = MergePatchArray(section, bArr, lArr, rArr, localAdd, remoteAdd, localDel, remoteDel, conflicts);
		return SectionMerge.Ok(ToJson(mergedArr, newline, lineIndent));
	}

	private static JsonArray MergePatchArray(
		SectionDefinition section,
		JsonArray baseArray,
		JsonArray localArray,
		JsonArray remoteArray,
		ISet<string> localAdd,
		ISet<string> remoteAdd,
		ISet<string> localDel,
		ISet<string> remoteDel,
		ISet<string> conflicts)
	{
		var b = BuildIndex(baseArray);
		var l = BuildIndex(localArray);
		var r = BuildIndex(remoteArray);

		var baseKeys = b.Order.ToHashSet(KeyComparer);
		var localKeys = l.Order.ToHashSet(KeyComparer);
		var remoteKeys = r.Order.ToHashSet(KeyComparer);

		AddReport(localAdd, section.Name, localKeys.Where(x => !baseKeys.Contains(x)));
		AddReport(remoteAdd, section.Name, remoteKeys.Where(x => !baseKeys.Contains(x)));
		AddReport(localDel, section.Name, baseKeys.Where(x => !localKeys.Contains(x)));
		AddReport(remoteDel, section.Name, baseKeys.Where(x => !remoteKeys.Contains(x)));

		var order = new List<string>();
		var seen = new HashSet<string>(KeyComparer);
		foreach (var key in r.Order.Concat(l.Order))
		{
			if (seen.Add(key))
			{
				order.Add(key);
			}
		}

		var result = new List<JsonNode?>();
		foreach (var key in order)
		{
			var hasBase = b.Map.TryGetValue(key, out var baseNode);
			var hasLocal = l.Map.TryGetValue(key, out var localNode);
			var hasRemote = r.Map.TryGetValue(key, out var remoteNode);

			var merged = MergePatchNode(section.Name, key, hasBase, baseNode, hasLocal, localNode, hasRemote, remoteNode, conflicts);
			if (merged is not null)
			{
				result.Add(merged);
			}
		}

		if (section.NormalizeInsertIndexes)
		{
			ReindexInserts(result);
		}

		var array = new JsonArray();
		foreach (var node in result)
		{
			array.Add(node);
		}

		return array;
	}

	private static JsonNode? MergePatchNode(
		string sectionName,
		string key,
		bool hasBase,
		JsonNode? baseNode,
		bool hasLocal,
		JsonNode? localNode,
		bool hasRemote,
		JsonNode? remoteNode,
		ISet<string> conflicts)
	{
		if (hasLocal && hasRemote)
		{
			if (JsonNode.DeepEquals(localNode, remoteNode))
			{
				return localNode?.DeepClone();
			}

			if (hasBase && JsonNode.DeepEquals(localNode, baseNode))
			{
				return remoteNode?.DeepClone();
			}

			if (hasBase && JsonNode.DeepEquals(remoteNode, baseNode))
			{
				return localNode?.DeepClone();
			}

			var operation = GetString(localNode as JsonObject, "operation")
							?? GetString(remoteNode as JsonObject, "operation")
							?? GetString(baseNode as JsonObject, "operation");

			if (string.Equals(operation, "merge", StringComparison.OrdinalIgnoreCase) &&
				localNode is JsonObject localObj &&
				remoteNode is JsonObject remoteObj)
			{
				var merged = MergeNode(baseNode, localObj, remoteObj);
				if (merged.Conflict)
				{
					conflicts.Add($"{sectionName}:{key}");
				}

				return merged.Node;
			}

			conflicts.Add($"{sectionName}:{key}");
			return localNode?.DeepClone();
		}

		if (hasLocal && !hasRemote)
		{
			if (!hasBase)
			{
				return localNode?.DeepClone();
			}

			if (JsonNode.DeepEquals(localNode, baseNode))
			{
				return null;
			}

			conflicts.Add($"{sectionName}:{key}");
			return localNode?.DeepClone();
		}

		if (!hasLocal && hasRemote)
		{
			if (!hasBase)
			{
				return remoteNode?.DeepClone();
			}

			if (JsonNode.DeepEquals(remoteNode, baseNode))
			{
				return null;
			}

			conflicts.Add($"{sectionName}:{key}");
			return null;
		}

		return null;
	}

	private static NodeMerge MergeNode(JsonNode? baseNode, JsonNode? localNode, JsonNode? remoteNode)
	{
		if (JsonNode.DeepEquals(localNode, remoteNode))
		{
			return new NodeMerge(localNode?.DeepClone(), false);
		}

		if (JsonNode.DeepEquals(localNode, baseNode))
		{
			return new NodeMerge(remoteNode?.DeepClone(), false);
		}

		if (JsonNode.DeepEquals(remoteNode, baseNode))
		{
			return new NodeMerge(localNode?.DeepClone(), false);
		}

		if (localNode is JsonObject lo && remoteNode is JsonObject ro)
		{
			var bo = baseNode as JsonObject;
			var merged = new JsonObject();
			var conflict = false;

			var keys = new List<string>();
			var seen = new HashSet<string>(KeyComparer);
			foreach (var key in lo.Select(static x => x.Key).Concat(bo?.Select(static x => x.Key) ?? []).Concat(ro.Select(static x => x.Key)))
			{
				if (seen.Add(key))
				{
					keys.Add(key);
				}
			}

			foreach (var key in keys)
			{
				JsonNode? bVal = null;
				JsonNode? lVal = null;
				JsonNode? rVal = null;
				var hasBase = bo is not null && bo.TryGetPropertyValue(key, out bVal);
				var hasLocal = lo.TryGetPropertyValue(key, out lVal);
				var hasRemote = ro.TryGetPropertyValue(key, out rVal);

				if (hasLocal && hasRemote)
				{
					var child = MergeNode(hasBase ? bVal : null, lVal, rVal);
					merged[key] = child.Node;
					conflict |= child.Conflict;
					continue;
				}

				if (hasLocal && !hasRemote)
				{
					if (hasBase && JsonNode.DeepEquals(lVal, bVal))
					{
						continue;
					}

					if (hasBase && !JsonNode.DeepEquals(lVal, bVal))
					{
						conflict = true;
					}

					merged[key] = lVal?.DeepClone();
					continue;
				}

				if (!hasLocal && hasRemote)
				{
					if (!hasBase)
					{
						merged[key] = rVal?.DeepClone();
						continue;
					}

					if (JsonNode.DeepEquals(rVal, bVal))
					{
						continue;
					}

					conflict = true;
				}
			}

			return new NodeMerge(merged, conflict);
		}

		return new NodeMerge(localNode?.DeepClone(), true);
	}

	private static void ReindexInserts(List<JsonNode?> entries)
	{
		var groups = new Dictionary<string, List<InsertEntry>>(KeyComparer);

		for (var i = 0; i < entries.Count; i++)
		{
			if (entries[i] is not JsonObject obj)
			{
				continue;
			}

			if (!string.Equals(GetString(obj, "operation"), "insert", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var parent = GetString(obj, "parentName");
			if (string.IsNullOrWhiteSpace(parent))
			{
				continue;
			}

			var index = GetInt(obj, "index") ?? int.MaxValue;
				if (!groups.TryGetValue(parent!, out var group))
			{
				group = [];
				groups[parent!] = group;
			}

			group.Add(new InsertEntry(obj, index, i));
		}

		foreach (var group in groups.Values)
		{
			var sorted = group
				.OrderBy(static x => x.Position)
				.ToArray();

			var usedIndexes = new HashSet<int>();
			var occupiedRectangles = new SortedSet<LayoutRectangle>(LayoutRectangleComparer.Instance);
			foreach (var insert in sorted)
			{
				var targetIndex = insert.Index == int.MaxValue
					? FindNextFreeIndex(usedIndexes, 0)
					: Math.Max(insert.Index, 0);

				while (usedIndexes.Contains(targetIndex))
				{
					targetIndex++;
				}

				insert.Entry["index"] = JsonValue.Create(targetIndex);
				usedIndexes.Add(targetIndex);
				NormalizeLayoutRow(insert.Entry, occupiedRectangles);
			}
		}
	}

	private static int FindNextFreeIndex(ISet<int> usedIndexes, int startAt)
	{
		var index = Math.Max(0, startAt);
		while (usedIndexes.Contains(index))
		{
			index++;
		}

		return index;
	}

	private static void NormalizeLayoutRow(JsonObject entry, ISet<LayoutRectangle> occupiedRectangles)
	{
		if (entry["values"] is not JsonObject values ||
			values["layoutConfig"] is not JsonObject layoutConfig)
		{
			return;
		}

		var column = GetInt(layoutConfig, "column");
		var row = GetInt(layoutConfig, "row");
		if (column is null || row is null)
		{
			return;
		}

		var colSpan = GetInt(layoutConfig, "colSpan") ?? 1;
		var rowSpan = GetInt(layoutConfig, "rowSpan") ?? 1;
		if (colSpan < 1)
		{
			colSpan = 1;
		}

		if (rowSpan < 1)
		{
			rowSpan = 1;
		}

		long targetRow = row.Value;
		foreach (var occupied in occupiedRectangles)
		{
			if (occupied.EndRow <= targetRow)
			{
				continue;
			}

			var candidate = LayoutRectangle.Create(column.Value, targetRow, colSpan, rowSpan);
			if (candidate.Overlaps(occupied))
			{
				targetRow = occupied.EndRow;
			}
		}

		if (targetRow != row.Value)
		{
			layoutConfig["row"] = JsonValue.Create(targetRow);
		}

		occupiedRectangles.Add(LayoutRectangle.Create(column.Value, targetRow, colSpan, rowSpan));
	}

	private static IndexedArray BuildIndex(JsonArray array)
	{
		var map = new Dictionary<string, JsonNode?>(KeyComparer);
		var order = new List<string>();

		foreach (var node in array)
		{
			var key = BuildKey(node);
			if (map.ContainsKey(key))
			{
				map[key] = node?.DeepClone();
				continue;
			}

			map[key] = node?.DeepClone();
			order.Add(key);
		}

		return new IndexedArray(map, order);
	}

	private static bool HasDuplicateKeys(JsonArray array)
	{
		var keys = new HashSet<string>(KeyComparer);
		return array.Any(node => !keys.Add(BuildKey(node)));
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
			if (part is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var s))
			{
				parts.Add(s);
			}
			else
			{
				parts.Add(part?.ToJsonString() ?? "null");
			}
		}

		return string.Join("/", parts);
	}

	private static void AddReport(ISet<string> target, string section, IEnumerable<string> keys)
	{
		foreach (var key in keys.Where(static x => !x.StartsWith("__raw__", StringComparison.Ordinal)))
		{
			target.Add($"{section}:{key}");
		}
	}

	private static string Raw3Way(string section, string baseRaw, string localRaw, string remoteRaw, ISet<string> conflicts)
	{
		if (string.Equals(localRaw, remoteRaw, StringComparison.Ordinal))
		{
			return localRaw;
		}

		if (string.Equals(localRaw, baseRaw, StringComparison.Ordinal))
		{
			return remoteRaw;
		}

		if (string.Equals(remoteRaw, baseRaw, StringComparison.Ordinal))
		{
			return localRaw;
		}

		conflicts.Add($"{section}:raw");
		return localRaw;
	}

	private static string EmptySectionJson(SectionDefinition section)
	{
		return section.Kind == SectionKind.DeepObject ? "{}" : "[]";
	}

	private static bool TryParseArray(string json, out JsonArray array)
	{
		if (!TryParse(json, out var node) || node is not JsonArray arr)
		{
			array = new JsonArray();
			return false;
		}

		array = arr;
		return true;
	}

	private static bool TryParseObj(string json, out JsonObject obj)
	{
		if (!TryParse(json, out var node) || node is not JsonObject o)
		{
			obj = new JsonObject();
			return false;
		}

		obj = o;
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

	private static bool IsWithinJsonBudget(string json)
	{
		try
		{
			var reader = new Utf8JsonReader(
				Encoding.UTF8.GetBytes(json),
				new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 });
			var tokenCount = 0;
			while (reader.Read())
			{
				if (++tokenCount > MaxJsonTokensPerSection)
				{
					return false;
				}
			}

			return true;
		}
		catch (JsonException)
		{
			// ClientUnit sections may contain JavaScript expressions rather than strict JSON.
			// The normal parser will route those through the raw three-way fallback.
			return true;
		}
	}

	private static string ToJson(JsonNode node, string newline, string lineIndent)
	{
		var json = BoundedJsonSerializer
			.Serialize(node, JsonWriteOptions)
			.Replace("\r\n", "\n", StringComparison.Ordinal);

		json = ConvertLeadingSpacesToTabs(json);
		int newlineCount = json.Count(static character => character == '\n');
		OutputBudget.EnsureIndentedTextFits(json, newline, newlineCount, lineIndent, newlineCount);
		if (!string.IsNullOrEmpty(lineIndent))
		{
			json = json.Replace("\n", "\n" + lineIndent, StringComparison.Ordinal);
		}

		return json.Replace("\n", newline, StringComparison.Ordinal);
	}

	private static string ConvertLeadingSpacesToTabs(string value)
	{
		var lines = value.Split('\n');
		for (var i = 0; i < lines.Length; i++)
		{
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

	private static string? GetString(JsonObject? obj, string prop)
	{
		if (obj is null || !obj.TryGetPropertyValue(prop, out var node) || node is not JsonValue value)
		{
			return null;
		}

		return value.TryGetValue<string>(out var result) ? result : null;
	}

	private static int? GetInt(JsonObject? obj, string prop)
	{
		if (obj is null || !obj.TryGetPropertyValue(prop, out var node) || node is not JsonValue value)
		{
			return null;
		}

		if (value.TryGetValue<int>(out var intVal))
		{
			return intVal;
		}

		if (value.TryGetValue<long>(out var longVal) && longVal >= int.MinValue && longVal <= int.MaxValue)
		{
			return (int)longVal;
		}

		return null;
	}

	private enum SectionKind { PatchArray, DeepObject }

	private readonly record struct SectionDefinition(string Name, string Marker, SectionKind Kind, bool NormalizeInsertIndexes = false);
	private readonly record struct SectionMerge(bool Success, string Text, string? Error)
	{
		public static SectionMerge Ok(string text) => new(true, text, null);
		public static SectionMerge Fail(string error) => new(false, string.Empty, error);
	}

	private readonly record struct IndexedArray(IReadOnlyDictionary<string, JsonNode?> Map, IReadOnlyList<string> Order);
	private readonly record struct NodeMerge(JsonNode? Node, bool Conflict);
	private readonly record struct InsertEntry(JsonObject Entry, int Index, int Position);
	private readonly record struct LayoutRectangle(long StartColumn, long StartRow, long EndColumn, long EndRow)
	{
		public static LayoutRectangle Create(long startColumn, long startRow, int colSpan, int rowSpan) =>
			new(startColumn, startRow, startColumn + colSpan, startRow + rowSpan);

		public bool Overlaps(LayoutRectangle other) =>
			StartColumn < other.EndColumn && other.StartColumn < EndColumn &&
			StartRow < other.EndRow && other.StartRow < EndRow;
	}

	private sealed class LayoutRectangleComparer : IComparer<LayoutRectangle>
	{
		public static LayoutRectangleComparer Instance { get; } = new();

		public int Compare(LayoutRectangle left, LayoutRectangle right)
		{
			int comparison = left.StartRow.CompareTo(right.StartRow);
			if (comparison != 0) return comparison;
			comparison = left.StartColumn.CompareTo(right.StartColumn);
			if (comparison != 0) return comparison;
			comparison = left.EndRow.CompareTo(right.EndRow);
			return comparison != 0 ? comparison : left.EndColumn.CompareTo(right.EndColumn);
		}
	}
}
