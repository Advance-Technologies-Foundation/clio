using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Creatio.ConflictResolver.Strategies;

internal sealed class DescriptorMergeStrategy : IMergeStrategy
{
	private static readonly Regex TimestampRegex = new(
		"^/Date\\((\\d+)\\)/$",
		RegexOptions.CultureInvariant | RegexOptions.Compiled);

	private static readonly JsonSerializerOptions PrettyJsonOptions = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public bool CanHandle(ConflictFileType fileType) => fileType == ConflictFileType.DescriptorJson;

	public MergeResult Merge(MergeRequest request)
	{
		if (!TryParseDescriptorDocument(request.Base, out var baseRoot, out var baseDescriptor, out var baseRootPropertyName, out var baseError))
		{
			return MergeResultFactory.InvalidInput("InvalidBaseJson", baseError);
		}

		if (!TryParseDescriptorDocument(request.Local, out var localRoot, out var localDescriptor, out var localRootPropertyName, out var localError))
		{
			return MergeResultFactory.InvalidInput("InvalidLocalJson", localError);
		}

		if (!TryParseDescriptorDocument(request.Remote, out var remoteRoot, out var remoteDescriptor, out var remoteRootPropertyName, out var remoteError))
		{
			return MergeResultFactory.InvalidInput("InvalidRemoteJson", remoteError);
		}

		if (JsonSemanticKeyValidator.TryFindDuplicate(baseRoot, "UId", out var baseDuplicate))
		{
			return MergeResultFactory.InvalidInput("DuplicateBaseSemanticKey", $"Base descriptor contains a duplicate semantic key at {baseDuplicate}.");
		}
		if (JsonSemanticKeyValidator.TryFindDuplicate(localRoot, "UId", out var localDuplicate))
		{
			return MergeResultFactory.InvalidInput("DuplicateLocalSemanticKey", $"Local descriptor contains a duplicate semantic key at {localDuplicate}.");
		}
		if (JsonSemanticKeyValidator.TryFindDuplicate(remoteRoot, "UId", out var remoteDuplicate))
		{
			return MergeResultFactory.InvalidInput("DuplicateRemoteSemanticKey", $"Remote descriptor contains a duplicate semantic key at {remoteDuplicate}.");
		}

		if (!string.Equals(baseRootPropertyName, localRootPropertyName, StringComparison.Ordinal) ||
			!string.Equals(baseRootPropertyName, remoteRootPropertyName, StringComparison.Ordinal))
		{
			return MergeResultFactory.UnresolvedConflict(
				"IdentityDrift",
				"Descriptor root object name drift detected.",
				"timestamp_selection_identity_check",
				trueConflicts: new[] { "RootObject" },
				verificationPassed: false);
		}

		var identityIssues = ValidateIdentity(baseDescriptor, localDescriptor, remoteDescriptor);
		if (identityIssues.Count > 0)
		{
			return MergeResultFactory.UnresolvedConflict(
				"IdentityDrift",
				$"Descriptor identity drift: {string.Join(", ", identityIssues)}",
				"timestamp_selection_identity_check",
				trueConflicts: identityIssues,
				verificationPassed: false);
		}

		if (!TryGetTimestamp(localDescriptor, out var localTimestamp))
		{
			return MergeResultFactory.InvalidInput(
				"InvalidLocalTimestamp",
				"Descriptor.ModifiedOnUtc in Local has invalid format.");
		}

		if (!TryGetTimestamp(remoteDescriptor, out var remoteTimestamp))
		{
			return MergeResultFactory.InvalidInput(
				"InvalidRemoteTimestamp",
				"Descriptor.ModifiedOnUtc in Remote has invalid format.");
		}

		if (IsDataBindingDescriptor(request.FilePath))
		{
			var dataBindingIssues = ValidateDataBindingDescriptor(localDescriptor, remoteDescriptor);
			if (dataBindingIssues.Count > 0)
			{
				return MergeResultFactory.UnresolvedConflict(
					"DataBindingDescriptorConflict",
					$"Data binding descriptor conflict: {string.Join(", ", dataBindingIssues)}",
					"timestamp_selection_data_binding_check",
					trueConflicts: dataBindingIssues,
					verificationPassed: false);
			}

			var useLocal = localTimestamp >= remoteTimestamp;
			var mergedRoot = MergeDataBindingDescriptor(baseRoot!, localRoot!, remoteRoot!, baseRootPropertyName!, useLocal);
			var mergedContent = BoundedJsonSerializer.Serialize(mergedRoot, PrettyJsonOptions);
			return MergeResultFactory.Resolved(
				mergedContent,
				"timestamp_selection",
				trueConflicts: Array.Empty<string>(),
				verificationPassed: true,
				winnerPolicy: useLocal ? "LOCAL" : "REMOTE");
		}

		var useLocalWinner = localTimestamp >= remoteTimestamp;
		var winnerRoot = useLocalWinner ? localRoot! : remoteRoot!;
		var winnerContent = useLocalWinner ? request.Local : request.Remote;
		if (!ContainsMergeableObjectArray(baseDescriptor, localDescriptor, remoteDescriptor))
		{
			var winnerPolicy = useLocalWinner ? "LOCAL" : "REMOTE";
			return MergeResultFactory.Resolved(
				winnerContent,
				"timestamp_selection",
				trueConflicts: Array.Empty<string>(),
				verificationPassed: true,
				winnerPolicy: winnerPolicy);
		}
		var mergedDescriptor = MergeDescriptorObject(baseDescriptor, localDescriptor, remoteDescriptor, useLocalWinner, "$" + "." + baseRootPropertyName);
		var mergedRootObject = new JsonObject
		{
			[baseRootPropertyName!] = mergedDescriptor
		};
		var mergedContentResult = JsonNode.DeepEquals(mergedRootObject, winnerRoot)
			? winnerContent
			: BoundedJsonSerializer.Serialize(mergedRootObject, PrettyJsonOptions);
		var winner = useLocalWinner ? "LOCAL" : "REMOTE";

		return MergeResultFactory.Resolved(
			mergedContentResult,
			"timestamp_selection",
			trueConflicts: Array.Empty<string>(),
			verificationPassed: true,
			winnerPolicy: winner);
	}

	private static bool TryParseDescriptorDocument(string content, out JsonObject? root, out JsonObject descriptor, out string? rootPropertyName, out string error)
	{
		root = null;
		rootPropertyName = null;
		try
		{
			root = JsonNode.Parse(content) as JsonObject;
			if (root is null)
			{
				descriptor = new JsonObject();
				error = "JSON root is not an object.";
				return false;
			}

			var descriptorNode = root["Descriptor"] as JsonObject;
			if (descriptorNode is not null)
			{
				descriptor = descriptorNode;
				rootPropertyName = "Descriptor";
				error = string.Empty;
				return true;
			}

			var objectProperties = root
				.Where(static x => x.Value is JsonObject)
				.Select(static x => (x.Key, Value: (JsonObject)x.Value!))
				.ToArray();
			if (objectProperties.Length == 1)
			{
				rootPropertyName = objectProperties[0].Key;
				descriptor = objectProperties[0].Value;
				error = string.Empty;
				return true;
			}

			descriptor = new JsonObject();
			error = "JSON does not contain a descriptor-like root object.";
			return false;
		}
		catch (JsonException ex)
		{
			descriptor = new JsonObject();
			error = ex.Message;
			return false;
		}
	}

	private static List<string> ValidateIdentity(JsonObject baseDescriptor, JsonObject localDescriptor, JsonObject remoteDescriptor)
	{
		var fields = new[] { "UId", "Name" };
		var issues = new List<string>();

		foreach (var field in fields)
		{
			var baseValue = GetString(baseDescriptor, field);
			var localValue = GetString(localDescriptor, field);
			var remoteValue = GetString(remoteDescriptor, field);

			if (!string.Equals(localValue, remoteValue, StringComparison.Ordinal) ||
				!string.Equals(baseValue, localValue, StringComparison.Ordinal) ||
				!string.Equals(baseValue, remoteValue, StringComparison.Ordinal))
			{
				issues.Add(field);
			}
		}

		var baseManagerName = GetString(baseDescriptor, "ManagerName");
		var localManagerName = GetString(localDescriptor, "ManagerName");
		var remoteManagerName = GetString(remoteDescriptor, "ManagerName");
		var managerNames = new[] { baseManagerName, localManagerName, remoteManagerName }
			.Where(static x => !string.IsNullOrWhiteSpace(x))
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		if (managerNames.Length > 1)
		{
			issues.Add("ManagerName");
		}

		return issues;
	}

	private static bool TryGetTimestamp(JsonObject descriptor, out long timestamp)
	{
		timestamp = 0;
		var raw = GetString(descriptor, "ModifiedOnUtc");
		if (string.IsNullOrWhiteSpace(raw))
		{
			return false;
		}

		var match = TimestampRegex.Match(raw);
		if (!match.Success)
		{
			return false;
		}

		return long.TryParse(match.Groups[1].Value, out timestamp);
	}

	private static string? GetString(JsonObject obj, string propertyName)
	{
		if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue value)
		{
			return null;
		}

		return value.TryGetValue<string>(out var result) ? result : null;
	}

	private static bool IsDataBindingDescriptor(string? filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return false;
		}

			var trimmedPath = filePath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (!string.Equals(Path.GetFileName(trimmedPath), "descriptor.json", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var directory = Path.GetDirectoryName(trimmedPath);
		if (string.IsNullOrWhiteSpace(directory))
		{
			return false;
		}

		var segments = directory.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
		return segments.Any(part => string.Equals(part, "Data", StringComparison.OrdinalIgnoreCase));
	}

	private static List<string> ValidateDataBindingDescriptor(JsonObject localDescriptor, JsonObject remoteDescriptor)
	{
		var issues = new List<string>();

		var localSchemaUid = GetNestedString(localDescriptor, "Schema", "UId");
		var remoteSchemaUid = GetNestedString(remoteDescriptor, "Schema", "UId");
		if (!string.Equals(localSchemaUid, remoteSchemaUid, StringComparison.Ordinal))
		{
			issues.Add("Schema.UId");
		}

		var localColumns = GetColumns(localDescriptor);
		var remoteColumns = GetColumns(remoteDescriptor);
		if (localColumns.Count != remoteColumns.Count)
		{
			issues.Add("Columns.Count");
		}

		var localByUid = IndexColumnsByUid(localColumns);
		var remoteByUid = IndexColumnsByUid(remoteColumns);

		var localUids = localByUid.Keys.ToHashSet(StringComparer.Ordinal);
		var remoteUids = remoteByUid.Keys.ToHashSet(StringComparer.Ordinal);
		if (!localUids.SetEquals(remoteUids))
		{
			issues.Add("Columns.UId");
		}

		foreach (var uid in localUids.Intersect(remoteUids, StringComparer.Ordinal))
		{
			var localColumn = localByUid[uid];
			var remoteColumn = remoteByUid[uid];

			var localIsKey = GetBoolean(localColumn, "IsKey");
			var remoteIsKey = GetBoolean(remoteColumn, "IsKey");
			if (localIsKey != remoteIsKey)
			{
				issues.Add($"Columns[{uid}].IsKey");
			}

			var localDataType = GetString(localColumn, "DataTypeValueUId");
			var remoteDataType = GetString(remoteColumn, "DataTypeValueUId");
			if (!string.Equals(localDataType, remoteDataType, StringComparison.Ordinal))
			{
				issues.Add($"Columns[{uid}].DataTypeValueUId");
			}
		}

		return issues;
	}

	private static JsonObject MergeDataBindingDescriptor(JsonObject baseRoot, JsonObject localRoot, JsonObject remoteRoot, string rootPropertyName, bool useLocal)
	{
		var winnerRoot = (JsonObject)(useLocal ? localRoot : remoteRoot).DeepClone();
		if (winnerRoot[rootPropertyName] is not JsonObject winnerDescriptor)
		{
			return winnerRoot;
		}

		var baseDescriptor = baseRoot[rootPropertyName] as JsonObject;
		var localDescriptor = localRoot[rootPropertyName] as JsonObject;
		var remoteDescriptor = remoteRoot[rootPropertyName] as JsonObject;
		if (localDescriptor is null || remoteDescriptor is null)
		{
			return winnerRoot;
		}

		var mergedColumns = MergeColumns(baseDescriptor, localDescriptor, remoteDescriptor, useLocal);
		winnerDescriptor["Columns"] = mergedColumns;
		return winnerRoot;
	}

	private static JsonArray MergeColumns(JsonObject? baseDescriptor, JsonObject localDescriptor, JsonObject remoteDescriptor, bool useLocal)
	{
		var baseColumns = GetColumns(baseDescriptor ?? new JsonObject());
		var localColumns = GetColumns(localDescriptor);
		var remoteColumns = GetColumns(remoteDescriptor);

		var baseByUid = IndexColumnsByUid(baseColumns);
		var localByUid = IndexColumnsByUid(localColumns);
		var remoteByUid = IndexColumnsByUid(remoteColumns);

		var orderedUids = (useLocal ? localColumns : remoteColumns)
			.Select(static c => GetString(c, "ColumnUId") ?? GetString(c, "UId"))
			.Where(static x => !string.IsNullOrWhiteSpace(x))
			.Select(static x => x!)
			.ToArray();

		var merged = new JsonArray();
		foreach (var uid in orderedUids)
		{
			if (!localByUid.TryGetValue(uid, out var localColumn) || !remoteByUid.TryGetValue(uid, out var remoteColumn))
			{
				continue;
			}

			baseByUid.TryGetValue(uid, out var baseColumn);
			merged.Add(MergeColumn(baseColumn, localColumn, remoteColumn, useLocal));
		}

		return merged;
	}

	private static JsonObject MergeColumn(JsonObject? baseColumn, JsonObject localColumn, JsonObject remoteColumn, bool useLocal)
	{
		var winner = (JsonObject)(useLocal ? localColumn : remoteColumn).DeepClone();
		var baseObj = baseColumn ?? new JsonObject();

		var keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var p in baseObj)
		{
			keys.Add(p.Key);
		}
		foreach (var p in localColumn)
		{
			keys.Add(p.Key);
		}
		foreach (var p in remoteColumn)
		{
			keys.Add(p.Key);
		}

		foreach (var key in keys)
		{
			if (IsProtectedColumnProperty(key))
			{
				continue;
			}

			var hasBase = baseObj.TryGetPropertyValue(key, out var baseValue);
			var hasLocal = localColumn.TryGetPropertyValue(key, out var localValue);
			var hasRemote = remoteColumn.TryGetPropertyValue(key, out var remoteValue);

			if (hasLocal && hasRemote)
			{
				if (JsonNode.DeepEquals(localValue, remoteValue))
				{
					winner[key] = localValue?.DeepClone();
				}
				else if (hasBase && JsonNode.DeepEquals(localValue, baseValue))
				{
					winner[key] = remoteValue?.DeepClone();
				}
				else if (hasBase && JsonNode.DeepEquals(remoteValue, baseValue))
				{
					winner[key] = localValue?.DeepClone();
				}
				else
				{
					winner[key] = (useLocal ? localValue : remoteValue)?.DeepClone();
				}
				continue;
			}

			if (hasLocal && !hasRemote)
			{
				if (!hasBase || !JsonNode.DeepEquals(localValue, baseValue))
				{
					winner[key] = localValue?.DeepClone();
				}
				else
				{
					winner.Remove(key);
				}
				continue;
			}

			if (!hasLocal && hasRemote)
			{
				if (!hasBase || !JsonNode.DeepEquals(remoteValue, baseValue))
				{
					winner[key] = remoteValue?.DeepClone();
				}
				else
				{
					winner.Remove(key);
				}
				continue;
			}

			winner.Remove(key);
		}

		return winner;
	}

	private static JsonObject MergeDescriptorObject(JsonObject baseObject, JsonObject localObject, JsonObject remoteObject, bool useLocal, string path)
	{
		var winner = (JsonObject)(useLocal ? localObject : remoteObject).DeepClone();
		var keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var p in baseObject)
		{
			keys.Add(p.Key);
		}
		foreach (var p in localObject)
		{
			keys.Add(p.Key);
		}
		foreach (var p in remoteObject)
		{
			keys.Add(p.Key);
		}

		foreach (var key in keys)
		{
			var propertyPath = path + "." + key;
			var hasBase = baseObject.TryGetPropertyValue(key, out var baseValue);
			var hasLocal = localObject.TryGetPropertyValue(key, out var localValue);
			var hasRemote = remoteObject.TryGetPropertyValue(key, out var remoteValue);

			if (hasLocal && hasRemote && localValue is JsonArray localArray && remoteValue is JsonArray remoteArray &&
				(!hasBase || baseValue is JsonArray))
			{
				var baseArray = baseValue as JsonArray ?? new JsonArray();
				if (IsUidObjectArray(baseArray, localArray, remoteArray))
				{
					winner[key] = MergeObjectArrayByUid(baseArray, localArray, remoteArray, useLocal, propertyPath);
					continue;
				}
			}

			if (hasLocal && hasRemote && localValue is JsonObject localChild && remoteValue is JsonObject remoteChild &&
				(!hasBase || baseValue is JsonObject))
			{
				var baseChild = baseValue as JsonObject ?? new JsonObject();
				winner[key] = MergeDescriptorObject(baseChild, localChild, remoteChild, useLocal, propertyPath);
				continue;
			}

			if (hasLocal && hasRemote)
			{
				if (JsonNode.DeepEquals(localValue, remoteValue))
				{
					winner[key] = localValue?.DeepClone();
				}
				else if (hasBase && JsonNode.DeepEquals(localValue, baseValue))
				{
					winner[key] = remoteValue?.DeepClone();
				}
				else if (hasBase && JsonNode.DeepEquals(remoteValue, baseValue))
				{
					winner[key] = localValue?.DeepClone();
				}
				else
				{
					winner[key] = (useLocal ? localValue : remoteValue)?.DeepClone();
				}
				continue;
			}

			if (hasLocal && !hasRemote)
			{
				if (!hasBase || !JsonNode.DeepEquals(localValue, baseValue))
				{
					winner[key] = localValue?.DeepClone();
				}
				else
				{
					winner.Remove(key);
				}
				continue;
			}

			if (!hasLocal && hasRemote)
			{
				if (!hasBase || !JsonNode.DeepEquals(remoteValue, baseValue))
				{
					winner[key] = remoteValue?.DeepClone();
				}
				else
				{
					winner.Remove(key);
				}
				continue;
			}

			winner.Remove(key);
		}

		return winner;
	}

	private static bool ContainsMergeableObjectArray(params JsonObject[] roots)
	{
		return roots.Any(ContainsMergeableObjectArray);
	}

	private static bool ContainsMergeableObjectArray(JsonNode? node)
	{
		switch (node)
		{
			case JsonArray array:
				if (IsUidObjectArray(array))
				{
					return true;
				}

				foreach (var item in array)
				{
					if (ContainsMergeableObjectArray(item))
					{
						return true;
					}
				}
				return false;

			case JsonObject obj:
				foreach (var property in obj)
				{
					if (ContainsMergeableObjectArray(property.Value))
					{
						return true;
					}
				}
				return false;

			default:
				return false;
		}
	}

	private static bool IsUidObjectArray(params JsonArray[] arrays)
	{
		var hasAny = false;
		foreach (var array in arrays)
		{
			foreach (var item in array)
			{
				hasAny = true;
				if (item is not JsonObject obj)
				{
					return false;
				}

				if (!TryGetUid(obj, out _))
				{
					return false;
				}
			}
		}

		return hasAny;
	}

	private static JsonArray MergeObjectArrayByUid(JsonArray baseArray, JsonArray localArray, JsonArray remoteArray, bool useLocal, string path)
	{
		var baseIndex = BuildObjectArrayIndex(baseArray);
		var localIndex = BuildObjectArrayIndex(localArray);
		var remoteIndex = BuildObjectArrayIndex(remoteArray);
		var orderedUids = EnumerateKeys(baseIndex.Order, remoteIndex.Order, localIndex.Order);
		var result = new JsonArray();

		foreach (var uid in orderedUids)
		{
			var hasBase = baseIndex.Values.TryGetValue(uid, out var baseObject);
			var hasLocal = localIndex.Values.TryGetValue(uid, out var localObject);
			var hasRemote = remoteIndex.Values.TryGetValue(uid, out var remoteObject);
			var uidPath = $"{path}[{uid}]";

			if (hasBase)
			{
				if (!hasLocal && !hasRemote)
				{
					continue;
				}

				if (hasLocal && !hasRemote)
				{
					if (JsonNode.DeepEquals(localObject, baseObject))
					{
						continue;
					}

					if (useLocal)
					{
						result.Add((JsonObject)localObject!.DeepClone());
					}
					continue;
				}

				if (!hasLocal && hasRemote)
				{
					if (JsonNode.DeepEquals(remoteObject, baseObject))
					{
						continue;
					}

					if (!useLocal)
					{
						result.Add((JsonObject)remoteObject!.DeepClone());
					}
					continue;
				}

				result.Add(MergeDescriptorObject(baseObject!, localObject!, remoteObject!, useLocal, uidPath));
				continue;
			}

			if (!hasLocal && !hasRemote)
			{
				continue;
			}

			if (hasLocal && !hasRemote)
			{
				result.Add((JsonObject)localObject!.DeepClone());
				continue;
			}

			if (!hasLocal && hasRemote)
			{
				result.Add((JsonObject)remoteObject!.DeepClone());
				continue;
			}

			result.Add(MergeDescriptorObject(new JsonObject(), localObject!, remoteObject!, useLocal, uidPath));
		}

		return result;
	}

	private static ObjectArrayIndex BuildObjectArrayIndex(JsonArray array)
	{
		var order = new List<string>();
		var values = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
		for (var index = 0; index < array.Count; index++)
		{
			if (array[index] is not JsonObject element)
			{
				continue;
			}

			if (!TryGetUid(element, out var uid))
			{
				continue;
			}

			if (!values.ContainsKey(uid))
			{
				order.Add(uid);
			}

			values[uid] = element;
		}

		return new ObjectArrayIndex(order, values);
	}

	private static bool TryGetUid(JsonObject element, out string uid)
	{
		uid = string.Empty;
		if (!element.TryGetPropertyValue("UId", out var uidNode))
		{
			return false;
		}

		if (uidNode is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var parsedUid))
		{
			return false;
		}

		if (string.IsNullOrWhiteSpace(parsedUid))
		{
			return false;
		}

		uid = parsedUid;
		return true;
	}

	private static IEnumerable<string> EnumerateKeys(params IReadOnlyList<string>[] keyCollections)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

	private static bool IsProtectedColumnProperty(string propertyName)
	{
		return string.Equals(propertyName, "IsKey", StringComparison.Ordinal) ||
		       string.Equals(propertyName, "DataTypeValueUId", StringComparison.Ordinal);
	}

	private static string? GetNestedString(JsonObject obj, string childProperty, string propertyName)
	{
		if (!obj.TryGetPropertyValue(childProperty, out var childNode) || childNode is not JsonObject childObj)
		{
			return null;
		}

		return GetString(childObj, propertyName);
	}

	private static IReadOnlyList<JsonObject> GetColumns(JsonObject descriptor)
	{
		if (!descriptor.TryGetPropertyValue("Columns", out var columnsNode) || columnsNode is not JsonArray columns)
		{
			return Array.Empty<JsonObject>();
		}

		return columns.OfType<JsonObject>().ToArray();
	}

	private static IReadOnlyDictionary<string, JsonObject> IndexColumnsByUid(IReadOnlyList<JsonObject> columns)
	{
		var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
		foreach (var column in columns)
		{
			var uid = GetString(column, "ColumnUId") ?? GetString(column, "UId");
			if (string.IsNullOrWhiteSpace(uid))
			{
				continue;
			}

				result[uid!] = column;
		}

		return result;
	}

	private static bool? GetBoolean(JsonObject obj, string propertyName)
	{
		if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue value)
		{
			return null;
		}

		return value.TryGetValue<bool>(out var result) ? result : null;
	}

	private readonly record struct ObjectArrayIndex(
		IReadOnlyList<string> Order,
		IReadOnlyDictionary<string, JsonObject> Values);
}
