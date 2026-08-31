namespace Creatio.ConflictResolver.Strategies;
using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class JsonMetadataMergeStrategy : IMergeStrategy, IMetadataMergeStrategy
{
	private const string DefaultObjectArrayKeyPropertyName = "UId";
	private static readonly JsonSerializerOptions PrettyJsonOptions = new()
	{
		WriteIndented = true
	};
	private readonly string _objectArrayKeyPropertyName;

	public JsonMetadataMergeStrategy(string objectArrayKeyPropertyName = DefaultObjectArrayKeyPropertyName)
	{
		if (string.IsNullOrWhiteSpace(objectArrayKeyPropertyName))
		{
			throw new ArgumentException("Object array key property name cannot be null or whitespace.", nameof(objectArrayKeyPropertyName));
		}

		_objectArrayKeyPropertyName = objectArrayKeyPropertyName;
	}
	
	public bool CanHandle(ConflictFileType fileType) => fileType == ConflictFileType.MetadataJson;

	public bool CanHandle(MergeRequest request)
	{
		return TryParseRootObject(request.Base, out _, out _) &&
			   TryParseRootObject(request.Local, out _, out _) &&
			   TryParseRootObject(request.Remote, out _, out _);
	}

	public MergeResult Merge(MergeRequest request)
	{
		if (!TryParseRootObject(request.Base, out var baseObject, out var baseError))
		{
			return MergeResultFactory.InvalidInput("InvalidBaseMetadataJson", baseError);
		}

		if (!TryParseRootObject(request.Local, out var localObject, out var localError))
		{
			return MergeResultFactory.InvalidInput("InvalidLocalMetadataJson", localError);
		}

		if (!TryParseRootObject(request.Remote, out var remoteObject, out var remoteError))
		{
			return MergeResultFactory.InvalidInput("InvalidRemoteMetadataJson", remoteError);
		}

		if (TryFindDuplicate(baseObject, out var baseDuplicate))
		{
			return MergeResultFactory.InvalidInput("DuplicateBaseSemanticKey", $"Base metadata contains a duplicate semantic key at {baseDuplicate}.");
		}
		if (TryFindDuplicate(localObject, out var localDuplicate))
		{
			return MergeResultFactory.InvalidInput("DuplicateLocalSemanticKey", $"Local metadata contains a duplicate semantic key at {localDuplicate}.");
		}
		if (TryFindDuplicate(remoteObject, out var remoteDuplicate))
		{
			return MergeResultFactory.InvalidInput("DuplicateRemoteSemanticKey", $"Remote metadata contains a duplicate semantic key at {remoteDuplicate}.");
		}

		var context = new MergeContext(_objectArrayKeyPropertyName);
		var mergedRoot = MergeObject(baseObject!, localObject!, remoteObject!, "$", context);
		var mergedContent = BoundedJsonSerializer.Serialize(mergedRoot, PrettyJsonOptions);

		return MergeResultFactory.Resolved(
			mergedContent,
			"json_3way_local_win",
			trueConflicts: context.TrueConflicts,
			verificationPassed: context.VerificationPassed);
	}

	private static bool TryParseRootObject(string content, out JsonObject? root, out string error)
	{
		root = null;
		try
		{
			var node = JsonNode.Parse(content);
			if (node is not JsonObject obj)
			{
				error = "Metadata JSON must have an object root.";
				return false;
			}

			root = obj;
			error = string.Empty;
			return true;
		}
		catch (JsonException ex)
		{
			error = $"Invalid JSON: {ex.Message}";
			return false;
		}
	}

	private static JsonObject MergeObject(
		JsonObject baseObject,
		JsonObject localObject,
		JsonObject remoteObject,
		string path,
		MergeContext context)
	{
		var result = (JsonObject)baseObject.DeepClone();
		foreach (var propertyName in EnumerateKeys(baseObject, remoteObject, localObject))
		{
			var baseState = GetObjectProperty(baseObject, propertyName);
			var localState = GetObjectProperty(localObject, propertyName);
			var remoteState = GetObjectProperty(remoteObject, propertyName);

			var mergedProperty = MergeNode(
				baseState,
				localState,
				remoteState,
				BuildPath(path, propertyName),
				context);

			if (mergedProperty.Exists)
			{
				result[propertyName] = CloneNode(mergedProperty.Value);
			}
			else
			{
				result.Remove(propertyName);
			}
		}

		return result;
	}

	private static MergeNodeResult MergeNode(
		NodeState baseState,
		NodeState localState,
		NodeState remoteState,
		string path,
		MergeContext context)
	{
		if (!baseState.Exists && !localState.Exists && !remoteState.Exists)
		{
			return MergeNodeResult.Missing;
		}

		if (IsObjectSet(baseState, localState, remoteState))
		{
			return MergeObjectNode(baseState, localState, remoteState, path, context);
		}

		if (IsArraySet(baseState, localState, remoteState))
		{
			return MergeArrayNode(baseState, localState, remoteState, path, context);
		}

		return MergePrimitiveNode(baseState, localState, remoteState, path, context);
	}

	private static MergeNodeResult MergePrimitiveNode(
		NodeState baseState,
		NodeState localState,
		NodeState remoteState,
		string path,
		MergeContext context)
	{
		if (baseState.Exists)
		{
			if (localState.Exists && remoteState.Exists)
			{
				var localChanged = !JsonEquals(localState.Value, baseState.Value);
				var remoteChanged = !JsonEquals(remoteState.Value, baseState.Value);
				if (localChanged && remoteChanged)
				{
					if (JsonEquals(localState.Value, remoteState.Value))
					{
						return MergeNodeResult.From(localState.Value);
					}

					context.AddConflict(path);
					return MergeNodeResult.From(localState.Value);
				}

				if (localChanged)
				{
					return MergeNodeResult.From(localState.Value);
				}

				if (remoteChanged)
				{
					return MergeNodeResult.From(remoteState.Value);
				}

				return MergeNodeResult.From(baseState.Value);
			}

			if (!localState.Exists && !remoteState.Exists)
			{
				return MergeNodeResult.Missing;
			}

			if (localState.Exists && !remoteState.Exists)
			{
				if (JsonEquals(localState.Value, baseState.Value))
				{
					return MergeNodeResult.Missing;
				}

				context.AddConflict(path);
				return MergeNodeResult.From(localState.Value);
			}

			context.AddConflict(path);
			return MergeNodeResult.Missing;
		}

		if (localState.Exists && remoteState.Exists)
		{
			if (JsonEquals(localState.Value, remoteState.Value))
			{
				return MergeNodeResult.From(localState.Value);
			}

			context.AddConflict(path);
			return MergeNodeResult.From(localState.Value);
		}

		if (localState.Exists)
		{
			return MergeNodeResult.From(localState.Value);
		}

		if (remoteState.Exists)
		{
			return MergeNodeResult.From(remoteState.Value);
		}

		return MergeNodeResult.Missing;
	}

	private static MergeNodeResult MergeObjectNode(
		NodeState baseState,
		NodeState localState,
		NodeState remoteState,
		string path,
		MergeContext context)
	{
		var baseObject = baseState.Exists ? (JsonObject)baseState.Value! : new JsonObject();
		var localObject = localState.Exists ? (JsonObject)localState.Value! : new JsonObject();
		var remoteObject = remoteState.Exists ? (JsonObject)remoteState.Value! : new JsonObject();

		if (baseState.Exists)
		{
			if (!localState.Exists && !remoteState.Exists)
			{
				return MergeNodeResult.Missing;
			}

			if (localState.Exists && !remoteState.Exists)
			{
				if (JsonEquals(localObject, baseObject))
				{
					return MergeNodeResult.Missing;
				}

				context.AddConflict(path);
				return MergeNodeResult.From((JsonObject)localObject.DeepClone());
			}

			if (!localState.Exists && remoteState.Exists)
			{
				context.AddConflict(path);
				return MergeNodeResult.Missing;
			}

			return MergeNodeResult.From(MergeObject(baseObject, localObject, remoteObject, path, context));
		}

		if (!localState.Exists && !remoteState.Exists)
		{
			return MergeNodeResult.Missing;
		}

		if (localState.Exists && !remoteState.Exists)
		{
			return MergeNodeResult.From((JsonObject)localObject.DeepClone());
		}

		if (!localState.Exists && remoteState.Exists)
		{
			return MergeNodeResult.From((JsonObject)remoteObject.DeepClone());
		}

		if (JsonEquals(localObject, remoteObject))
		{
			return MergeNodeResult.From((JsonObject)localObject.DeepClone());
		}

		context.AddConflict(path);
		return MergeNodeResult.From(MergeObject(new JsonObject(), localObject, remoteObject, path, context));
	}

	private static MergeNodeResult MergeArrayNode(
		NodeState baseState,
		NodeState localState,
		NodeState remoteState,
		string path,
		MergeContext context)
	{
		var baseArray = baseState.Exists ? (JsonArray)baseState.Value! : new JsonArray();
		var localArray = localState.Exists ? (JsonArray)localState.Value! : new JsonArray();
		var remoteArray = remoteState.Exists ? (JsonArray)remoteState.Value! : new JsonArray();

		if (baseState.Exists)
		{
			if (!localState.Exists && !remoteState.Exists)
			{
				return MergeNodeResult.Missing;
			}

			if (localState.Exists && !remoteState.Exists)
			{
				if (JsonEquals(localArray, baseArray))
				{
					return MergeNodeResult.Missing;
				}

				context.AddConflict(path);
				return MergeNodeResult.From((JsonArray)localArray.DeepClone());
			}

			if (!localState.Exists && remoteState.Exists)
			{
				context.AddConflict(path);
				return MergeNodeResult.Missing;
			}

			return MergeNodeResult.From(MergeArray(baseArray, localArray, remoteArray, path, context));
		}

		if (!localState.Exists && !remoteState.Exists)
		{
			return MergeNodeResult.Missing;
		}

		if (localState.Exists && !remoteState.Exists)
		{
			return MergeNodeResult.From((JsonArray)localArray.DeepClone());
		}

		if (!localState.Exists && remoteState.Exists)
		{
			return MergeNodeResult.From((JsonArray)remoteArray.DeepClone());
		}

		if (JsonEquals(localArray, remoteArray))
		{
			return MergeNodeResult.From((JsonArray)localArray.DeepClone());
		}

		context.AddConflict(path);
		return MergeNodeResult.From(MergeArray(new JsonArray(), localArray, remoteArray, path, context));
	}

	private static JsonArray MergeArray(
		JsonArray baseArray,
		JsonArray localArray,
		JsonArray remoteArray,
		string path,
		MergeContext context)
	{
		var kind = DetectArrayKind(baseArray, localArray, remoteArray);
		return kind switch
		{
			ArrayKind.Object => MergeObjectArray(baseArray, localArray, remoteArray, path, context),
			ArrayKind.Primitive => MergePrimitiveArray(baseArray, localArray, remoteArray, path, context),
			_ => ResolveMixedArray(baseArray, localArray, remoteArray, path, context)
		};
	}

	private static JsonArray ResolveMixedArray(
		JsonArray baseArray,
		JsonArray localArray,
		JsonArray remoteArray,
		string path,
		MergeContext context)
	{
		var localChanged = !JsonEquals(localArray, baseArray);
		var remoteChanged = !JsonEquals(remoteArray, baseArray);
		if (localChanged && remoteChanged)
		{
			if (JsonEquals(localArray, remoteArray))
			{
				return (JsonArray)localArray.DeepClone();
			}

			context.AddConflict(path);
			return (JsonArray)localArray.DeepClone();
		}

		if (localChanged)
		{
			return (JsonArray)localArray.DeepClone();
		}

		if (remoteChanged)
		{
			return (JsonArray)remoteArray.DeepClone();
		}

		return (JsonArray)baseArray.DeepClone();
	}

	private static JsonArray MergePrimitiveArray(
		JsonArray baseArray,
		JsonArray localArray,
		JsonArray remoteArray,
		string path,
		MergeContext context)
	{
		var baseIndex = BuildPrimitiveArrayIndex(baseArray);
		var localIndex = BuildPrimitiveArrayIndex(localArray);
		var remoteIndex = BuildPrimitiveArrayIndex(remoteArray);

		var result = new JsonArray();
		var added = new HashSet<string>(StringComparer.Ordinal);
		foreach (var key in baseIndex.Order)
		{
			var existsInLocal = localIndex.Values.ContainsKey(key);
			var existsInRemote = remoteIndex.Values.ContainsKey(key);

			if (existsInLocal && existsInRemote)
			{
				AddPrimitiveArrayValue(result, added, key, baseIndex.Values[key]);
				continue;
			}

			if (!existsInLocal && !existsInRemote)
			{
				continue;
			}

			if (!existsInRemote && existsInLocal)
			{
				continue;
			}

			context.AddConflict(path);
		}

		var localAdded = localIndex.Order
			.Where(key => !baseIndex.Values.ContainsKey(key))
			.ToHashSet(StringComparer.Ordinal);
		var remoteAdded = remoteIndex.Order
			.Where(key => !baseIndex.Values.ContainsKey(key))
			.ToHashSet(StringComparer.Ordinal);

		foreach (var key in remoteIndex.Order)
		{
			if (!baseIndex.Values.ContainsKey(key) && !localAdded.Contains(key))
			{
				AddPrimitiveArrayValue(result, added, key, remoteIndex.Values[key]);
			}
		}

		foreach (var key in localIndex.Order)
		{
			if (!baseIndex.Values.ContainsKey(key))
			{
				AddPrimitiveArrayValue(result, added, key, localIndex.Values[key]);
			}
		}

		return result;
	}

	private static JsonArray MergeObjectArray(
		JsonArray baseArray,
		JsonArray localArray,
		JsonArray remoteArray,
		string path,
		MergeContext context)
	{
		var baseIndex = BuildObjectArrayIndex(baseArray, path, "base", context);
		var localIndex = BuildObjectArrayIndex(localArray, path, "local", context);
		var remoteIndex = BuildObjectArrayIndex(remoteArray, path, "remote", context);

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
					if (JsonEquals(localObject, baseObject))
					{
						continue;
					}

					context.AddConflict(uidPath);
					result.Add((JsonObject)localObject!.DeepClone());
					continue;
				}

				if (!hasLocal && hasRemote)
				{
					context.AddConflict(uidPath);
					continue;
				}

				result.Add(MergeObject(baseObject!, localObject!, remoteObject!, uidPath, context));
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

			if (JsonEquals(localObject, remoteObject))
			{
				result.Add((JsonObject)localObject!.DeepClone());
				continue;
			}

			context.AddConflict(uidPath);
			result.Add(MergeObject(new JsonObject(), localObject!, remoteObject!, uidPath, context));
		}

		return result;
	}

	private static PrimitiveArrayIndex BuildPrimitiveArrayIndex(JsonArray array)
	{
		var order = new List<string>();
		var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
		foreach (var item in array)
		{
			if (item is JsonObject or JsonArray)
			{
				continue;
			}

			var key = ToPrimitiveKey(item);
			if (values.ContainsKey(key))
			{
				continue;
			}

			values[key] = CloneNode(item);
			order.Add(key);
		}

		return new PrimitiveArrayIndex(order, values);
	}

	private static ObjectArrayIndex BuildObjectArrayIndex(
		JsonArray array,
		string path,
		string source,
		MergeContext context)
	{
		var order = new List<string>();
		var values = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
		for (var index = 0; index < array.Count; index++)
		{
			if (array[index] is not JsonObject element)
			{
				context.AddMissingUid(path, source, index);
				continue;
			}

			if (!TryGetUid(element, context.ObjectArrayKeyPropertyName, out var uid))
			{
				context.AddMissingUid(path, source, index);
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

	private static bool TryGetUid(JsonObject element, string objectArrayKeyPropertyName, out string uid)
	{
		uid = string.Empty;
		if (!element.TryGetPropertyValue(objectArrayKeyPropertyName, out var uidNode))
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

	private static void AddPrimitiveArrayValue(
		JsonArray result,
		ISet<string> added,
		string key,
		JsonNode? value)
	{
		if (!added.Add(key))
		{
			return;
		}

		result.Add(CloneNode(value));
	}

	private static string ToPrimitiveKey(JsonNode? value)
	{
		return value?.ToJsonString() ?? "null";
	}

	private static bool JsonEquals(JsonNode? left, JsonNode? right)
	{
		return JsonNode.DeepEquals(left, right);
	}

	private static string BuildPath(string parentPath, string propertyName)
	{
		return $"{parentPath}.{propertyName}";
	}

	private static NodeState GetObjectProperty(JsonObject obj, string propertyName)
	{
		return obj.TryGetPropertyValue(propertyName, out var value)
			? new NodeState(true, value)
			: NodeState.Missing;
	}

	private static IEnumerable<string> EnumerateKeys(params JsonObject[] objects)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var obj in objects)
		{
			foreach (var property in obj)
			{
				if (seen.Add(property.Key))
				{
					yield return property.Key;
				}
			}
		}
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

	private static ArrayKind DetectArrayKind(params JsonArray[] arrays)
	{
		var hasObjects = false;
		var hasNonObjects = false;
		foreach (var array in arrays)
		{
			foreach (var item in array)
			{
				if (item is JsonObject)
				{
					hasObjects = true;
					continue;
				}

				if (item is JsonArray)
				{
					return ArrayKind.Mixed;
				}

				hasNonObjects = true;
			}
		}

		if (hasObjects && hasNonObjects)
		{
			return ArrayKind.Mixed;
		}

		return hasObjects ? ArrayKind.Object : ArrayKind.Primitive;
	}

	private static bool IsObjectSet(params NodeState[] states)
	{
		var hasObject = false;
		foreach (var state in states)
		{
			if (!state.Exists)
			{
				continue;
			}

			if (state.Value is JsonObject)
			{
				hasObject = true;
				continue;
			}

			return false;
		}

		return hasObject;
	}

	private static bool IsArraySet(params NodeState[] states)
	{
		var hasArray = false;
		foreach (var state in states)
		{
			if (!state.Exists)
			{
				continue;
			}

			if (state.Value is JsonArray)
			{
				hasArray = true;
				continue;
			}

			return false;
		}

		return hasArray;
	}

	private static JsonNode? CloneNode(JsonNode? node)
	{
		return node?.DeepClone();
	}

	private sealed class MergeContext
	{
		private readonly HashSet<string> _trueConflicts = new(StringComparer.Ordinal);
		private readonly HashSet<string> _missingUidErrors = new(StringComparer.Ordinal);

		public MergeContext(string objectArrayKeyPropertyName)
		{
			ObjectArrayKeyPropertyName = objectArrayKeyPropertyName;
		}

		public string ObjectArrayKeyPropertyName { get; }

		public IEnumerable<string> TrueConflicts => _trueConflicts.Concat(_missingUidErrors);

		public bool VerificationPassed => _missingUidErrors.Count == 0;

		public void AddConflict(string path)
		{
			if (!string.IsNullOrWhiteSpace(path))
			{
				if (path.Length > 2048 || _trueConflicts.Count >= 1024)
				{
					throw new MergeReportLimitExceededException();
				}
				_trueConflicts.Add(path);
			}
		}

		public void AddMissingUid(string path, string source, int index)
		{
			var error = $"Missing{ObjectArrayKeyPropertyName}:{source}:{path}[{index}]";
			if (error.Length > 2048 || _missingUidErrors.Count >= 1024)
			{
				throw new MergeReportLimitExceededException();
			}
			_missingUidErrors.Add(error);
		}

	}

	private bool TryFindDuplicate(JsonNode? node, out string duplicatePath)
	{
		return JsonSemanticKeyValidator.TryFindDuplicate(node, _objectArrayKeyPropertyName, out duplicatePath);
	}

	private readonly record struct NodeState(bool Exists, JsonNode? Value)
	{
		public static NodeState Missing => new(false, null);
	}

	private readonly record struct MergeNodeResult(bool Exists, JsonNode? Value)
	{
		public static MergeNodeResult Missing => new(false, null);

		public static MergeNodeResult From(JsonNode? value) => new(true, value);
	}

	private readonly record struct PrimitiveArrayIndex(
		IReadOnlyList<string> Order,
		IReadOnlyDictionary<string, JsonNode?> Values);

	private readonly record struct ObjectArrayIndex(
		IReadOnlyList<string> Order,
		IReadOnlyDictionary<string, JsonObject> Values);

	private enum ArrayKind
	{
		Primitive,
		Object,
		Mixed
	}
}
