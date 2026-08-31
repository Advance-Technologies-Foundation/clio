using System.Text.Json;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver;

internal static class JsonAutomergeFormattingSupport
{
	public static bool TryParseJson(string content, out JsonNode? node)
	{
		try
		{
			node = JsonNode.Parse(content);
			return node is not null;
		}
		catch (JsonException)
		{
			node = null;
			return false;
		}
	}

	public static string[] NormalizeJsonConflictPaths(
		JsonNode? baseNode,
		JsonNode? mergedNode,
		JsonNode? localNode,
		JsonNode? remoteNode,
		IReadOnlyCollection<string> conflictPaths,
		JsonConflictRenderOptions options)
	{
		var normalizedPaths = new HashSet<string>(StringComparer.Ordinal);
		foreach (var conflictPath in conflictPaths)
		{
			CollectNormalizedJsonConflictPaths(
				ResolvePath(baseNode, conflictPath, options),
				ResolvePath(mergedNode, conflictPath, options),
				ResolvePath(localNode, conflictPath, options),
				ResolvePath(remoteNode, conflictPath, options),
				conflictPath,
				normalizedPaths,
				options);
		}

		return normalizedPaths.ToArray();
	}

	public static string DetectNewLine(string content)
	{
		return content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
	}

	private static void CollectNormalizedJsonConflictPaths(
		JsonPathState baseState,
		JsonPathState mergedState,
		JsonPathState localState,
		JsonPathState remoteState,
		string path,
		ISet<string> normalizedPaths,
		JsonConflictRenderOptions options)
	{
		if (JsonStatesEqual(localState, remoteState) ||
		    JsonStatesEqual(localState, baseState) ||
		    JsonStatesEqual(remoteState, baseState))
		{
			return;
		}

		if (!mergedState.Exists || !localState.Exists || !remoteState.Exists)
		{
			normalizedPaths.Add(path);
			return;
		}

		if (mergedState.Node is JsonObject mergedObject &&
		    localState.Node is JsonObject localObject &&
		    remoteState.Node is JsonObject remoteObject)
		{
			var baseObject = baseState.Exists ? baseState.Node as JsonObject : null;
			var beforeCount = normalizedPaths.Count;
			foreach (var propertyName in EnumerateObjectKeys(baseObject, mergedObject, localObject, remoteObject))
			{
				JsonNode? baseValue = null;
				JsonNode? mergedValue = null;
				JsonNode? localValue = null;
				JsonNode? remoteValue = null;

				var hasBase = baseObject?.TryGetPropertyValue(propertyName, out baseValue) == true;
				var hasMerged = mergedObject.TryGetPropertyValue(propertyName, out mergedValue);
				var hasLocal = localObject.TryGetPropertyValue(propertyName, out localValue);
				var hasRemote = remoteObject.TryGetPropertyValue(propertyName, out remoteValue);

				CollectNormalizedJsonConflictPaths(
					new JsonPathState(hasBase, baseValue),
					new JsonPathState(hasMerged, mergedValue),
					new JsonPathState(hasLocal, localValue),
					new JsonPathState(hasRemote, remoteValue),
					$"{path}.{propertyName}",
					normalizedPaths,
					options);
			}

			if (normalizedPaths.Count == beforeCount)
			{
				normalizedPaths.Add(path);
			}

			return;
		}

		if (mergedState.Node is JsonArray mergedArray &&
		    localState.Node is JsonArray localArray &&
		    remoteState.Node is JsonArray remoteArray)
		{
			if (TryCollectKeyedArrayConflictPaths(baseState, mergedArray, localArray, remoteArray, path, normalizedPaths, options))
			{
				return;
			}

			if (TryCollectIndexedArrayConflictPaths(baseState, mergedArray, localArray, remoteArray, path, normalizedPaths, options))
			{
				return;
			}

			normalizedPaths.Add(path);
			return;
		}

		normalizedPaths.Add(path);
	}

	private static bool TryCollectKeyedArrayConflictPaths(
		JsonPathState baseState,
		JsonArray mergedArray,
		JsonArray localArray,
		JsonArray remoteArray,
		string path,
		ISet<string> normalizedPaths,
		JsonConflictRenderOptions options)
	{
		var arrays = new[] { baseState.Exists ? baseState.Node as JsonArray : null, mergedArray, localArray, remoteArray };
		if (!arrays
			    .Where(static array => array is not null)
			    .SelectMany(static array => array!)
			    .All(static item => item is JsonObject))
		{
			return false;
		}

		var baseLookup = BuildArrayLookup(baseState.Exists ? baseState.Node as JsonArray : null, path, options);
		var mergedLookup = BuildArrayLookup(mergedArray, path, options);
		var localLookup = BuildArrayLookup(localArray, path, options);
		var remoteLookup = BuildArrayLookup(remoteArray, path, options);
		if (baseLookup is null || mergedLookup is null || localLookup is null || remoteLookup is null)
		{
			return false;
		}

		var beforeCount = normalizedPaths.Count;
		foreach (var key in EnumerateKeys(baseLookup.Keys, mergedLookup.Keys, localLookup.Keys, remoteLookup.Keys))
		{
			baseLookup.TryGetValue(key, out var baseItem);
			mergedLookup.TryGetValue(key, out var mergedItem);
			localLookup.TryGetValue(key, out var localItem);
			remoteLookup.TryGetValue(key, out var remoteItem);

			CollectNormalizedJsonConflictPaths(
				new JsonPathState(baseItem is not null, baseItem),
				new JsonPathState(mergedItem is not null, mergedItem),
				new JsonPathState(localItem is not null, localItem),
				new JsonPathState(remoteItem is not null, remoteItem),
				$"{path}[{key}]",
				normalizedPaths,
				options);
		}

		return normalizedPaths.Count > beforeCount;
	}

	private static bool TryCollectIndexedArrayConflictPaths(
		JsonPathState baseState,
		JsonArray mergedArray,
		JsonArray localArray,
		JsonArray remoteArray,
		string path,
		ISet<string> normalizedPaths,
		JsonConflictRenderOptions options)
	{
		var baseArray = baseState.Exists ? baseState.Node as JsonArray : null;
		if (baseArray is not null &&
		    (baseArray.Count != mergedArray.Count ||
		     baseArray.Count != localArray.Count ||
		     baseArray.Count != remoteArray.Count))
		{
			return false;
		}

		if (baseArray is null && mergedArray.Count != localArray.Count && mergedArray.Count != remoteArray.Count)
		{
			return false;
		}

		var beforeCount = normalizedPaths.Count;
		var maxCount = Math.Max(Math.Max(mergedArray.Count, localArray.Count), Math.Max(remoteArray.Count, baseArray?.Count ?? 0));
		for (var index = 0; index < maxCount; index++)
		{
			CollectNormalizedJsonConflictPaths(
				new JsonPathState(baseArray is not null && index < baseArray.Count, baseArray is not null && index < baseArray.Count ? baseArray[index] : null),
				new JsonPathState(index < mergedArray.Count, index < mergedArray.Count ? mergedArray[index] : null),
				new JsonPathState(index < localArray.Count, index < localArray.Count ? localArray[index] : null),
				new JsonPathState(index < remoteArray.Count, index < remoteArray.Count ? remoteArray[index] : null),
				$"{path}[{index}]",
				normalizedPaths,
				options);
		}

		return normalizedPaths.Count > beforeCount;
	}

	private static IReadOnlyDictionary<string, JsonNode?>? BuildArrayLookup(
		JsonArray? array,
		string arrayPath,
		JsonConflictRenderOptions options)
	{
		if (array is null)
		{
			return new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
		}

		var lookup = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
		foreach (var item in array.OfType<JsonObject>())
		{
			if (!TryGetKey(arrayPath, item, options, out var key))
			{
				return null;
			}

			lookup[key] = item;
		}

		return lookup;
	}

	private static IEnumerable<string> EnumerateKeys(params IEnumerable<string>[] keyCollections)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var keys in keyCollections)
		{
			foreach (var key in keys)
			{
				if (seen.Add(key))
				{
					yield return key;
				}
			}
		}
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

	private static JsonPathState ResolvePath(JsonNode? root, string path, JsonConflictRenderOptions options)
	{
		if (root is null || string.IsNullOrWhiteSpace(path) || !path.StartsWith("$", StringComparison.Ordinal))
		{
			return new JsonPathState(false, null);
		}

		var current = root;
		var currentPath = "$";
		var index = 1;
		while (index < path.Length)
		{
			switch (path[index])
			{
				case '.':
					index++;
					var propertyStart = index;
					while (index < path.Length && path[index] is not '.' and not '[')
					{
						index++;
					}

					var propertyName = path.Substring(propertyStart, index - propertyStart);
					if (current is not JsonObject obj || !obj.TryGetPropertyValue(propertyName, out current))
					{
						return new JsonPathState(false, null);
					}

					currentPath = $"{currentPath}.{propertyName}";
					break;

				case '[':
					index++;
					var keyStart = index;
					while (index < path.Length && path[index] != ']')
					{
						index++;
					}

					if (index >= path.Length || current is not JsonArray array)
					{
						return new JsonPathState(false, null);
					}

					var token = path.Substring(keyStart, index - keyStart);
					index++;
					if (int.TryParse(token, out var arrayIndex))
					{
						if (arrayIndex < 0 || arrayIndex >= array.Count)
						{
							return new JsonPathState(false, null);
						}

						current = array[arrayIndex];
						currentPath = $"{currentPath}[{arrayIndex}]";
						break;
					}

					current = FindArrayItemByKey(array, currentPath, token, options);
					if (current is null)
					{
						return new JsonPathState(false, null);
					}

					currentPath = $"{currentPath}[{token}]";
					break;

				default:
					return new JsonPathState(false, null);
			}
		}

		return new JsonPathState(true, current);
	}

	private static JsonObject? FindArrayItemByKey(
		JsonArray array,
		string arrayPath,
		string key,
		JsonConflictRenderOptions options)
	{
		return array
			.OfType<JsonObject>()
			.FirstOrDefault(item => TryGetKey(arrayPath, item, options, out var itemKey) &&
				string.Equals(itemKey, key, StringComparison.Ordinal));
	}

	private static bool TryGetKey(
		string arrayPath,
		JsonObject item,
		JsonConflictRenderOptions options,
		out string key)
	{
		key = string.Empty;
		var customKey = options.ArrayItemKeyResolver?.Invoke(arrayPath, item);
		if (!string.IsNullOrWhiteSpace(customKey))
		{
			key = customKey!;
			return true;
		}

		if (!item.TryGetPropertyValue(options.ObjectArrayKeyPropertyName, out var uidNode) || uidNode is not JsonValue uidValue)
		{
			return false;
		}

		if (!uidValue.TryGetValue<string>(out var parsedKey) || string.IsNullOrWhiteSpace(parsedKey))
		{
			return false;
		}

		key = parsedKey;
		return true;
	}

	private static bool JsonStatesEqual(JsonPathState left, JsonPathState right)
	{
		if (left.Exists != right.Exists)
		{
			return false;
		}

		return !left.Exists || JsonNode.DeepEquals(left.Node, right.Node);
	}

	private readonly record struct JsonPathState(bool Exists, JsonNode? Node);
}
