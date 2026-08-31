using System.Text.Json;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver;

internal sealed record JsonConflictRenderOptions(
	string IndentChars,
	string NewLine,
	JsonSerializerOptions SerializerOptions,
	string ObjectArrayKeyPropertyName = "UId",
	Func<string, JsonObject, string?>? ArrayItemKeyResolver = null);

internal sealed class JsonConflictMarkerSerializer
{
	private readonly HashSet<string> _conflictPaths;
	private readonly JsonConflictRenderOptions _options;

	private JsonConflictMarkerSerializer(IEnumerable<string> conflictPaths, JsonConflictRenderOptions options)
	{
		_conflictPaths = conflictPaths
			.Where(static path => !string.IsNullOrWhiteSpace(path))
			.ToHashSet(StringComparer.Ordinal);
		_options = options;
	}

	public static string Serialize(
		JsonNode? baseNode,
		JsonNode? mergedNode,
		JsonNode? localNode,
		JsonNode? remoteNode,
		IEnumerable<string> conflictPaths,
		JsonConflictRenderOptions options,
		string rootPath = "$")
	{
		var serializer = new JsonConflictMarkerSerializer(conflictPaths, options);
		var lines = serializer.RenderValueLines(
			new NodeState(baseNode is not null, baseNode),
			new NodeState(mergedNode is not null, mergedNode),
			new NodeState(localNode is not null, localNode),
			new NodeState(remoteNode is not null, remoteNode),
			rootPath,
			0,
			allowMarkers: true);

		return string.Join(options.NewLine, lines);
	}

	private List<string> RenderValueLines(
		NodeState baseState,
		NodeState mergedState,
		NodeState localState,
		NodeState remoteState,
		string path,
		int indentLevel,
		bool allowMarkers)
	{
		if (!mergedState.Exists)
		{
			return [];
		}

		if (allowMarkers &&
		    _conflictPaths.Contains(path) &&
		    mergedState.Node is not JsonObject &&
		    mergedState.Node is not JsonArray)
		{
			return RenderValueConflictLines(
				baseState,
				localState,
				remoteState,
				path,
				indentLevel,
				trailingComma: false);
		}

		if (mergedState.Node is JsonObject mergedObject)
		{
			return RenderObjectLines(
				baseState.Node as JsonObject,
				mergedObject,
				localState.Node as JsonObject,
				remoteState.Node as JsonObject,
				path,
				indentLevel,
				allowMarkers);
		}

		if (mergedState.Node is JsonArray mergedArray)
		{
			return RenderArrayLines(
				baseState.Node as JsonArray,
				mergedArray,
				localState.Node as JsonArray,
				remoteState.Node as JsonArray,
				path,
				indentLevel,
				allowMarkers);
		}

		return [Indent(indentLevel) + SerializeNode(mergedState.Node)];
	}

	private List<string> RenderObjectLines(
		JsonObject? baseObject,
		JsonObject mergedObject,
		JsonObject? localObject,
		JsonObject? remoteObject,
		string path,
		int indentLevel,
		bool allowMarkers)
	{
		if (mergedObject.Count == 0 && !HasNestedConflictPath(path))
		{
			return [Indent(indentLevel) + "{}"];
		}

		var propertyNames = EnumerateObjectKeys(baseObject, remoteObject, localObject, mergedObject)
			.Where(propertyName =>
			{
				var propertyPath = BuildPropertyPath(path, propertyName);
				return mergedObject.ContainsKey(propertyName) || _conflictPaths.Contains(propertyPath);
			})
			.ToArray();

		var lines = new List<string>
		{
			Indent(indentLevel) + "{"
		};

		for (var index = 0; index < propertyNames.Length; index++)
		{
			var propertyName = propertyNames[index];
			var propertyPath = BuildPropertyPath(path, propertyName);
			var trailingComma = index < propertyNames.Length - 1;

			JsonNode? baseValue = null;
			JsonNode? mergedValue = null;
			JsonNode? localValue = null;
			JsonNode? remoteValue = null;
			baseObject?.TryGetPropertyValue(propertyName, out baseValue);
			var mergedExists = mergedObject.TryGetPropertyValue(propertyName, out mergedValue);
			var localExists = localObject?.TryGetPropertyValue(propertyName, out localValue) == true;
			var remoteExists = remoteObject?.TryGetPropertyValue(propertyName, out remoteValue) == true;

			List<string> propertyLines;
			if (allowMarkers && _conflictPaths.Contains(propertyPath))
			{
				propertyLines = RenderPropertyConflictLines(
					propertyName,
					new NodeState(baseValue is not null, baseValue),
					new NodeState(localExists, localValue),
					new NodeState(remoteExists, remoteValue),
					propertyPath,
					indentLevel + 1,
					trailingComma);
			}
			else if (mergedExists)
			{
				propertyLines = RenderNamedValueLines(
					propertyName,
					new NodeState(baseValue is not null, baseValue),
					new NodeState(true, mergedValue),
					new NodeState(localExists, localValue),
					new NodeState(remoteExists, remoteValue),
					propertyPath,
					indentLevel + 1,
					trailingComma,
					allowMarkers);
			}
			else
			{
				continue;
			}

			lines.AddRange(propertyLines);
		}

		lines.Add(Indent(indentLevel) + "}");
		return lines;
	}

	private List<string> RenderArrayLines(
		JsonArray? baseArray,
		JsonArray mergedArray,
		JsonArray? localArray,
		JsonArray? remoteArray,
		string path,
		int indentLevel,
		bool allowMarkers)
	{
		if (mergedArray.Count == 0 && !HasNestedConflictPath(path))
		{
			return [Indent(indentLevel) + "[]"];
		}

		var lines = new List<string>
		{
			Indent(indentLevel) + "["
		};

		if (IsKeyedObjectArray(path, baseArray, mergedArray, localArray, remoteArray))
		{
			var orderedKeys = EnumerateArrayKeys(path, baseArray, remoteArray, localArray, mergedArray)
				.Where(key =>
				{
					var itemPath = BuildArrayItemPath(path, key);
					return FindArrayItemByKey(path, mergedArray, key) is not null ||
					       _conflictPaths.Contains(itemPath);
				})
				.ToArray();

			for (var index = 0; index < orderedKeys.Length; index++)
			{
				var key = orderedKeys[index];
				var itemPath = BuildArrayItemPath(path, key);
				var trailingComma = index < orderedKeys.Length - 1;

				var baseItem = FindArrayItemByKey(path, baseArray, key);
				var mergedItem = FindArrayItemByKey(path, mergedArray, key);
				var localItem = FindArrayItemByKey(path, localArray, key);
				var remoteItem = FindArrayItemByKey(path, remoteArray, key);

				List<string> itemLines;
				if (allowMarkers && _conflictPaths.Contains(itemPath))
				{
					itemLines = RenderValueConflictLines(
						new NodeState(baseItem is not null, baseItem),
						new NodeState(localItem is not null, localItem),
						new NodeState(remoteItem is not null, remoteItem),
						itemPath,
						indentLevel + 1,
						trailingComma);
				}
				else if (mergedItem is not null)
				{
					itemLines = RenderArrayItemLines(
						new NodeState(baseItem is not null, baseItem),
						new NodeState(true, mergedItem),
						new NodeState(localItem is not null, localItem),
						new NodeState(remoteItem is not null, remoteItem),
						itemPath,
						indentLevel + 1,
						trailingComma,
						allowMarkers);
				}
				else
				{
					continue;
				}

				lines.AddRange(itemLines);
			}
		}
		else
		{
			for (var index = 0; index < mergedArray.Count; index++)
			{
				var trailingComma = index < mergedArray.Count - 1;
				var itemPath = $"{path}[{index}]";
				lines.AddRange(RenderArrayItemLines(
					new NodeState(baseArray is not null && index < baseArray.Count, baseArray is not null && index < baseArray.Count ? baseArray[index] : null),
					new NodeState(true, mergedArray[index]),
					new NodeState(localArray is not null && index < localArray.Count, localArray is not null && index < localArray.Count ? localArray[index] : null),
					new NodeState(remoteArray is not null && index < remoteArray.Count, remoteArray is not null && index < remoteArray.Count ? remoteArray[index] : null),
					itemPath,
					indentLevel + 1,
					trailingComma,
					allowMarkers));
			}
		}

		lines.Add(Indent(indentLevel) + "]");
		return lines;
	}

	private List<string> RenderNamedValueLines(
		string propertyName,
		NodeState baseState,
		NodeState mergedState,
		NodeState localState,
		NodeState remoteState,
		string path,
		int indentLevel,
		bool trailingComma,
		bool allowMarkers)
	{
		var valueLines = RenderValueLines(baseState, mergedState, localState, remoteState, path, indentLevel, allowMarkers);
		if (valueLines.Count == 0)
		{
			return [];
		}

		valueLines[0] = $"{Indent(indentLevel)}{SerializePropertyName(propertyName)}: {valueLines[0].TrimStart()}";
		if (trailingComma)
		{
			valueLines[valueLines.Count - 1] += ",";
		}

		return valueLines;
	}

	private List<string> RenderArrayItemLines(
		NodeState baseState,
		NodeState mergedState,
		NodeState localState,
		NodeState remoteState,
		string path,
		int indentLevel,
		bool trailingComma,
		bool allowMarkers)
	{
		var valueLines = RenderValueLines(baseState, mergedState, localState, remoteState, path, indentLevel, allowMarkers);
		if (valueLines.Count == 0)
		{
			return [];
		}

		if (trailingComma)
		{
			valueLines[valueLines.Count - 1] += ",";
		}

		return valueLines;
	}

	private List<string> RenderPropertyConflictLines(
		string propertyName,
		NodeState baseState,
		NodeState localState,
		NodeState remoteState,
		string path,
		int indentLevel,
		bool trailingComma)
	{
		var localLines = localState.Exists
			? RenderNamedValueLines(
				propertyName,
				baseState,
				new NodeState(true, localState.Node),
				new NodeState(false, null),
				new NodeState(false, null),
				path,
				indentLevel,
				trailingComma,
				allowMarkers: false)
			: [];

		var remoteLines = remoteState.Exists
			? RenderNamedValueLines(
				propertyName,
				baseState,
				new NodeState(true, remoteState.Node),
				new NodeState(false, null),
				new NodeState(false, null),
				path,
				indentLevel,
				trailingComma,
				allowMarkers: false)
			: [];

		return BuildConflictBlock(localLines, remoteLines);
	}

	private List<string> RenderValueConflictLines(
		NodeState baseState,
		NodeState localState,
		NodeState remoteState,
		string path,
		int indentLevel,
		bool trailingComma)
	{
		var localLines = localState.Exists
			? RenderValueLines(
				baseState,
				new NodeState(true, localState.Node),
				new NodeState(false, null),
				new NodeState(false, null),
				path,
				indentLevel,
				allowMarkers: false)
			: [];

		var remoteLines = remoteState.Exists
			? RenderValueLines(
				baseState,
				new NodeState(true, remoteState.Node),
				new NodeState(false, null),
				new NodeState(false, null),
				path,
				indentLevel,
				allowMarkers: false)
			: [];

		if (trailingComma)
		{
			if (localLines.Count > 0)
			{
				localLines[localLines.Count - 1] += ",";
			}

			if (remoteLines.Count > 0)
			{
				remoteLines[remoteLines.Count - 1] += ",";
			}
		}

		return BuildConflictBlock(localLines, remoteLines);
	}

	private List<string> BuildConflictBlock(
		IReadOnlyList<string> localLines,
		IReadOnlyList<string> remoteLines)
	{
		var lines = new List<string>
		{
			"<<<<<<< Local"
		};

		lines.AddRange(localLines);
		lines.Add("=======");
		lines.AddRange(remoteLines);
		lines.Add(">>>>>>> Remote");
		return lines;
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

	private IEnumerable<string> EnumerateArrayKeys(
		string arrayPath,
		JsonArray? baseArray,
		JsonArray? remoteArray,
		JsonArray? localArray,
		JsonArray? mergedArray)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var array in new[] { baseArray, remoteArray, localArray, mergedArray })
		{
			if (array is null)
			{
				continue;
			}

			foreach (var item in array.OfType<JsonObject>())
			{
				if (TryGetKey(arrayPath, item, out var key) && seen.Add(key))
				{
					yield return key;
				}
			}
		}
	}

	private bool IsKeyedObjectArray(
		string arrayPath,
		JsonArray? baseArray,
		JsonArray? mergedArray,
		JsonArray? localArray,
		JsonArray? remoteArray)
	{
		var hasAny = false;
		foreach (var array in new[] { baseArray, mergedArray, localArray, remoteArray })
		{
			if (array is null)
			{
				continue;
			}

			foreach (var item in array)
			{
				if (item is null)
				{
					continue;
				}

				hasAny = true;
				if (item is not JsonObject obj || !TryGetKey(arrayPath, obj, out _))
				{
					return false;
				}
			}
		}

		return hasAny;
	}

	private JsonNode? FindArrayItemByKey(string arrayPath, JsonArray? array, string key)
	{
		if (array is null)
		{
			return null;
		}

		foreach (var item in array.OfType<JsonObject>())
		{
			if (TryGetKey(arrayPath, item, out var itemKey) &&
			    string.Equals(itemKey, key, StringComparison.Ordinal))
			{
				return item;
			}
		}

		return null;
	}

	private bool TryGetKey(string arrayPath, JsonObject obj, out string key)
	{
		key = string.Empty;
		var customKey = _options.ArrayItemKeyResolver?.Invoke(arrayPath, obj);
		if (!string.IsNullOrWhiteSpace(customKey))
		{
			key = customKey!;
			return true;
		}

		if (!obj.TryGetPropertyValue(_options.ObjectArrayKeyPropertyName, out var node) || node is not JsonValue value)
		{
			return false;
		}

		if (!value.TryGetValue<string>(out var parsedKey) || string.IsNullOrWhiteSpace(parsedKey))
		{
			return false;
		}

		key = parsedKey;
		return true;
	}

	private static string BuildPropertyPath(string path, string propertyName) => $"{path}.{propertyName}";

	private static string BuildArrayItemPath(string path, string key) => $"{path}[{key}]";

	private bool HasNestedConflictPath(string path)
	{
		if (_conflictPaths.Contains(path))
		{
			return true;
		}

		var propertyPrefix = path + ".";
		var arrayPrefix = path + "[";
		return _conflictPaths.Any(conflictPath =>
			conflictPath.StartsWith(propertyPrefix, StringComparison.Ordinal) ||
			conflictPath.StartsWith(arrayPrefix, StringComparison.Ordinal));
	}

	private string Indent(int indentLevel) => string.Concat(Enumerable.Repeat(_options.IndentChars, indentLevel));

	private string SerializeNode(JsonNode? node) => node?.ToJsonString(_options.SerializerOptions) ?? "null";

	private static string SerializePropertyName(string propertyName) => JsonSerializer.Serialize(propertyName);

	private readonly record struct NodeState(bool Exists, JsonNode? Node);
}
