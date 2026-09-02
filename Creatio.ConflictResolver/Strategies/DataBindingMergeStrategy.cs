using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver.Strategies;

internal class DataBindingMergeStrategy : IMergeStrategy
{
	private const long MaxDescriptorFileSizeBytes = 1 * 1024 * 1024;
	private static readonly JsonSerializerOptions PrettyJsonOptions = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public bool CanHandle(ConflictFileType fileType)
	{
		return fileType == ConflictFileType.DataBinding;
	}

	public MergeResult Merge(MergeRequest request)
	{
		if (!TryParseDataDocument(request.Base, out var baseRoot, out var baseRows, out var baseError))
		{
			return MergeResultFactory.InvalidInput("InvalidBaseDataJson", baseError);
		}

		if (!TryParseDataDocument(request.Local, out var localRoot, out var localRows, out var localError))
		{
			return MergeResultFactory.InvalidInput("InvalidLocalDataJson", localError);
		}

		if (!TryParseDataDocument(request.Remote, out var remoteRoot, out var remoteRows, out var remoteError))
		{
			return MergeResultFactory.InvalidInput("InvalidRemoteDataJson", remoteError);
		}

		var useLocalizedRowKeys = IsLocalizedDataFile(request.FilePath);
		IReadOnlyList<string>? keyColumnUids = null;
		if (!useLocalizedRowKeys && !TryGetKeyColumnUids(
			request.DescriptorContent,
			request.FilePath,
			out keyColumnUids,
			out var keyColumnsError))
		{
			return MergeResultFactory.InvalidInput("InvalidDataDescriptor", keyColumnsError);
		}

		var context = new MergeContext();
		if (!TryMergeRows(baseRows!, localRows!, remoteRows!, useLocalizedRowKeys, keyColumnUids, context, out var mergedRows, out var mergeError))
		{
			return MergeResultFactory.UnresolvedConflict(
				"DataBindingKeyResolutionFailed",
				mergeError,
				"data_json_key_resolution",
				trueConflicts: context.TrueConflicts,
				verificationPassed: false);
		}

		localRoot!["PackageData"] = mergedRows;
		var mergedContent = BoundedJsonSerializer.Serialize(localRoot, PrettyJsonOptions);
		if (context.TrueConflicts.Count > 0)
		{
			return MergeResultFactory.UnresolvedConflict(
				"DataBindingLogicalConflict",
				"Automatic merge completed with logical conflicts in data binding rows.",
				"data_json_3way_local_win",
				trueConflicts: context.TrueConflicts,
				mergedContent: mergedContent,
				verificationPassed: false);
		}

		return MergeResultFactory.Resolved(
			mergedContent,
			"data_json_3way_local_win",
			trueConflicts: context.TrueConflicts,
			verificationPassed: true,
			winnerPolicy: "LOCAL");
	}

	private static bool TryParseDataDocument(string content, out JsonObject? root, out JsonArray? packageData, out string error)
	{
		root = null;
		packageData = null;
		error = string.Empty;

		try
		{
			if (JsonNode.Parse(content) is not JsonObject parsedRoot)
			{
				error = "Data JSON must have an object root.";
				return false;
			}

			if (parsedRoot["PackageData"] is not JsonArray parsedRows)
			{
				error = "Data JSON must contain PackageData array.";
				return false;
			}

			root = parsedRoot;
			packageData = parsedRows;
			return true;
		}
		catch (JsonException ex)
		{
			error = $"Invalid JSON: {ex.Message}";
			return false;
		}
	}

	private static bool TryGetKeyColumnUids(
		string? inlineDescriptorContent,
		string? dataFilePath,
		out IReadOnlyList<string>? keyColumnUids,
		out string error)
	{
		keyColumnUids = null;
		error = string.Empty;

		try
		{
			var descriptorContent = inlineDescriptorContent;
			if (string.IsNullOrWhiteSpace(descriptorContent))
			{
				if (string.IsNullOrWhiteSpace(dataFilePath))
				{
					error = "DescriptorContent or FilePath is required for data merge.";
					return false;
				}

				var descriptorPath = ResolveDescriptorPath(dataFilePath!);
				if (descriptorPath is null)
				{
					error = "descriptor.json was not found near the data file.";
					return false;
				}

				if (!TryReadDescriptorContent(descriptorPath, out descriptorContent, out error))
				{
					return false;
				}
			}
			descriptorContent = descriptorContent!.TrimStart('\uFEFF');
			if (JsonNode.Parse(descriptorContent) is not JsonObject root ||
				root["Descriptor"] is not JsonObject descriptor ||
				descriptor["Columns"] is not JsonArray columns)
			{
				error = "descriptor.json has invalid structure. Expected Descriptor.Columns array.";
				return false;
			}

			var keys = new List<string>();
			foreach (var column in columns.OfType<JsonObject>())
			{
				if (!TryGetString(column, "ColumnUId", out var uid))
				{
					continue;
				}

				if (!TryGetBoolean(column, "IsKey", out var isKey) || !isKey)
				{
					continue;
				}

				keys.Add(uid);
			}

			if (keys.Count == 0)
			{
				error = "descriptor.json does not contain key columns (Columns[*].IsKey == true).";
				return false;
			}

			keyColumnUids = keys.Distinct(StringComparer.Ordinal).ToArray();
			return true;
		}
		catch (JsonException ex)
		{
			error = $"Failed to read descriptor.json: {ex.Message}";
			return false;
		}
	}

	private static string? ResolveDescriptorPath(string dataFilePath)
	{
		var dataDirectory = Path.GetDirectoryName(dataFilePath);
		if (string.IsNullOrWhiteSpace(dataDirectory))
		{
			return null;
		}

		var localDescriptorPath = Path.Combine(dataDirectory, "descriptor.json");
		if (File.Exists(localDescriptorPath))
		{
			return localDescriptorPath;
		}

		var directoryInfo = new DirectoryInfo(dataDirectory);
		if (string.Equals(directoryInfo.Name, "Localization", StringComparison.OrdinalIgnoreCase) &&
			directoryInfo.Parent is not null)
		{
			var parentDescriptorPath = Path.Combine(directoryInfo.Parent.FullName, "descriptor.json");
			if (File.Exists(parentDescriptorPath))
			{
				return parentDescriptorPath;
			}
		}

		return null;
	}

	private static bool TryReadDescriptorContent(string descriptorPath, out string content, out string error)
	{
		content = string.Empty;
		error = string.Empty;

		long fileSize;
		try
		{
			fileSize = new FileInfo(descriptorPath).Length;
		}
		catch (IOException ex)
		{
			error = $"Failed to inspect descriptor.json: {ex.Message}";
			return false;
		}
		catch (UnauthorizedAccessException ex)
		{
			error = $"Failed to inspect descriptor.json: {ex.Message}";
			return false;
		}

		if (fileSize > MaxDescriptorFileSizeBytes)
		{
			error = $"descriptor.json exceeds maximum allowed size of {MaxDescriptorFileSizeBytes} bytes.";
			return false;
		}

		try
		{
			content = ReadFileText(descriptorPath, MaxDescriptorFileSizeBytes);
			return true;
		}
		catch (IOException ex)
		{
			error = $"Failed to read descriptor.json: {ex.Message}";
			return false;
		}
		catch (UnauthorizedAccessException ex)
		{
			error = $"Failed to read descriptor.json: {ex.Message}";
			return false;
		}
	}

	private static string ReadFileText(string path, long maxBytes)
	{
		const int bufferSize = 81920;
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
		if (stream.Length > maxBytes)
		{
			throw new InvalidDataException($"descriptor.json exceeds maximum allowed size of {maxBytes} bytes.");
		}

		if (stream.Length > int.MaxValue)
		{
			throw new InvalidDataException("descriptor.json is too large to process.");
		}

		var expectedLength = (int)stream.Length;
		var buffer = new byte[expectedLength];
		var offset = 0;
		while (offset < expectedLength)
		{
			var bytesRead = stream.Read(buffer, offset, expectedLength - offset);
			if (bytesRead == 0)
			{
				break;
			}

			offset += bytesRead;
		}

		return Encoding.UTF8.GetString(buffer, 0, offset);
	}

	private static bool TryMergeRows(
		JsonArray baseRows,
		JsonArray localRows,
		JsonArray remoteRows,
		bool useLocalizedRowKeys,
		IReadOnlyList<string>? keyColumnUids,
		MergeContext context,
		out JsonArray mergedRows,
		out string error)
	{
		mergedRows = new JsonArray();
		error = string.Empty;

		if (!TryBuildRowIndex(baseRows, useLocalizedRowKeys, keyColumnUids, context, "base", out var baseMap, out error) ||
			!TryBuildRowIndex(localRows, useLocalizedRowKeys, keyColumnUids, context, "local", out var localMap, out error) ||
			!TryBuildRowIndex(remoteRows, useLocalizedRowKeys, keyColumnUids, context, "remote", out var remoteMap, out error))
		{
			return false;
		}

		var mergedRowMap = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
		foreach (var key in EnumerateKeys(baseMap.Order, localMap.Order, remoteMap.Order))
		{
			var hasBase = baseMap.Values.TryGetValue(key, out var baseRow);
			var hasLocal = localMap.Values.TryGetValue(key, out var localRow);
			var hasRemote = remoteMap.Values.TryGetValue(key, out var remoteRow);
			var path = $"$.PackageData[{key}]";

			if (hasBase)
			{
				if (!hasLocal && !hasRemote)
				{
					continue;
				}

				if (hasLocal && !hasRemote)
				{
					if (IsRowChanged(baseRow!, localRow!))
					{
						context.AddConflict(path);
						mergedRowMap[key] = (JsonObject)localRow!.DeepClone();
					}
					continue;
				}

				if (!hasLocal && hasRemote)
				{
					if (IsRowChanged(baseRow!, remoteRow!))
					{
						context.AddConflict(path);
						mergedRowMap[key] = (JsonObject)remoteRow!.DeepClone();
						continue;
					}

					context.AddConflict(path);
					continue;
				}

				mergedRowMap[key] = MergeRow(baseRow!, localRow!, remoteRow!, path, context);
				continue;
			}

			if (!hasLocal && !hasRemote)
			{
				continue;
			}

			if (hasLocal && !hasRemote)
			{
				mergedRowMap[key] = (JsonObject)localRow!.DeepClone();
				continue;
			}

			if (!hasLocal && hasRemote)
			{
				mergedRowMap[key] = (JsonObject)remoteRow!.DeepClone();
				continue;
			}

			context.AddConflict(path);
			mergedRowMap[key] = MergeRow(new JsonObject { ["Row"] = new JsonArray() }, localRow!, remoteRow!, path, context);
		}

		var orderedKeys = BuildMergedRowOrder(baseMap.Order, localMap.Order, remoteMap.Order, mergedRowMap.Keys);
		foreach (var key in orderedKeys)
		{
			if (mergedRowMap.TryGetValue(key, out var row))
			{
				mergedRows.Add(row);
			}
		}

		return true;
	}

	private static IReadOnlyList<string> BuildMergedRowOrder(
		IReadOnlyList<string> baseOrder,
		IReadOnlyList<string> localOrder,
		IReadOnlyList<string> remoteOrder,
		IEnumerable<string> mergedKeys)
	{
		var mergedKeySet = new HashSet<string>(mergedKeys, StringComparer.Ordinal);
		var localInsertions = BuildInsertionSlots(baseOrder, localOrder, mergedKeySet);
		var remoteInsertions = BuildInsertionSlots(baseOrder, remoteOrder, mergedKeySet);
		var orderedKeys = new List<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);

		for (var slot = 0; slot <= baseOrder.Count; slot++)
		{
			AppendSlotItems(slot, localInsertions, remoteInsertions, orderedKeys, seen);
			if (slot >= baseOrder.Count)
			{
				continue;
			}

			var baseKey = baseOrder[slot];
			if (mergedKeySet.Contains(baseKey) && seen.Add(baseKey))
			{
				orderedKeys.Add(baseKey);
			}
		}

		foreach (var key in EnumerateKeys(baseOrder, localOrder, remoteOrder))
		{
			if (mergedKeySet.Contains(key) && seen.Add(key))
			{
				orderedKeys.Add(key);
			}
		}

		foreach (var key in mergedKeySet.OrderBy(static x => x, StringComparer.Ordinal))
		{
			if (seen.Add(key))
			{
				orderedKeys.Add(key);
			}
		}

		return orderedKeys;
	}

	private static IReadOnlyDictionary<int, IReadOnlyList<string>> BuildInsertionSlots(
		IReadOnlyList<string> baseOrder,
		IReadOnlyList<string> sourceOrder,
		ISet<string> mergedKeySet)
	{
		var baseIndexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
		for (var index = 0; index < baseOrder.Count; index++)
		{
			if (!baseIndexByKey.ContainsKey(baseOrder[index]))
			{
				baseIndexByKey[baseOrder[index]] = index;
			}
		}

		var nextBaseSlots = new int[sourceOrder.Count];
		var nextBaseSlot = baseOrder.Count;
		for (var index = sourceOrder.Count - 1; index >= 0; index--)
		{
			if (baseIndexByKey.TryGetValue(sourceOrder[index], out var baseIndex))
			{
				nextBaseSlot = baseIndex;
			}
			nextBaseSlots[index] = nextBaseSlot;
		}

		var slots = new Dictionary<int, List<string>>();
		for (var index = 0; index < sourceOrder.Count; index++)
		{
			var key = sourceOrder[index];
			if (baseIndexByKey.ContainsKey(key) || !mergedKeySet.Contains(key))
			{
				continue;
			}

			var slot = nextBaseSlots[index];
			if (!slots.TryGetValue(slot, out var slotItems))
			{
				slotItems = [];
				slots[slot] = slotItems;
			}

			slotItems.Add(key);
		}

		return slots.ToDictionary(static x => x.Key, static x => (IReadOnlyList<string>)x.Value);
	}

	private static void AppendSlotItems(
		int slot,
		IReadOnlyDictionary<int, IReadOnlyList<string>> localInsertions,
		IReadOnlyDictionary<int, IReadOnlyList<string>> remoteInsertions,
		ICollection<string> orderedKeys,
		ISet<string> seen)
	{
		var hasLocal = localInsertions.TryGetValue(slot, out var localItems);
		var hasRemote = remoteInsertions.TryGetValue(slot, out var remoteItems);
		if (!hasLocal && !hasRemote)
		{
			return;
		}

		IEnumerable<string> itemsToAppend;
		if (hasLocal && hasRemote)
		{
			itemsToAppend = localItems!
				.Concat(remoteItems!)
				.Distinct(StringComparer.Ordinal)
				.OrderBy(static x => x, StringComparer.Ordinal);
		}
		else
		{
			itemsToAppend = hasLocal ? localItems! : remoteItems!;
		}

		foreach (var key in itemsToAppend)
		{
			if (seen.Add(key))
			{
				orderedKeys.Add(key);
			}
		}
	}

	private static JsonObject MergeRow(JsonObject baseRow, JsonObject localRow, JsonObject remoteRow, string path, MergeContext context)
	{
		var baseValues = BuildRowValueIndex(baseRow);
		var localValues = BuildRowValueIndex(localRow);
		var remoteValues = BuildRowValueIndex(remoteRow);

		var localChanged = GetChangedColumns(baseValues, localValues);
		var remoteChanged = GetChangedColumns(baseValues, remoteValues);

		if (localChanged.Count == 0 && remoteChanged.Count == 0)
		{
			return (JsonObject)baseRow.DeepClone();
		}

		if (localChanged.Count > 0 && remoteChanged.Count == 0)
		{
			return (JsonObject)localRow.DeepClone();
		}

		if (localChanged.Count == 0 && remoteChanged.Count > 0)
		{
			return (JsonObject)remoteRow.DeepClone();
		}

		var intersection = localChanged.Intersect(remoteChanged, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
		if (intersection.Count > 0)
		{
			foreach (var uid in intersection)
			{
				context.AddConflict($"{path}.Row[{uid}]");
			}
		}

		var result = (JsonObject)baseRow.DeepClone();
		var targetColumns = result["Row"] as JsonArray ?? new JsonArray();
		var targetIndexes = BuildTargetColumnIndex(targetColumns);
		foreach (var uid in localChanged)
		{
			ApplyColumn(targetColumns, targetIndexes, localValues, uid);
		}

		foreach (var uid in remoteChanged)
		{
			if (intersection.Contains(uid))
			{
				continue;
			}

			ApplyColumn(targetColumns, targetIndexes, remoteValues, uid);
		}

		var compactedColumns = new JsonArray();
		foreach (var column in targetColumns.Where(static column => column is not null).ToArray())
		{
			compactedColumns.Add(column!.DeepClone());
		}
		result["Row"] = compactedColumns;

		return result;
	}

	private static bool IsRowChanged(JsonObject baseRow, JsonObject branchRow)
	{
		var baseValues = BuildRowValueIndex(baseRow);
		var branchValues = BuildRowValueIndex(branchRow);
		return GetChangedColumns(baseValues, branchValues).Count > 0;
	}

	private static IReadOnlyDictionary<string, JsonObject> BuildRowValueIndex(JsonObject row)
	{
		var values = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
		if (row["Row"] is not JsonArray columns)
		{
			return values;
		}

		foreach (var column in columns.OfType<JsonObject>())
		{
			if (!TryGetString(column, "SchemaColumnUId", out var uid))
			{
				continue;
			}

			values[uid] = column;
		}

		return values;
	}

	private static HashSet<string> GetChangedColumns(
		IReadOnlyDictionary<string, JsonObject> baseColumns,
		IReadOnlyDictionary<string, JsonObject> branchColumns)
	{
		var keys = baseColumns.Keys.Concat(branchColumns.Keys).Distinct(StringComparer.Ordinal);
		var changed = new HashSet<string>(StringComparer.Ordinal);

		foreach (var key in keys)
		{
			var hasBase = baseColumns.TryGetValue(key, out var baseColumn);
			var hasBranch = branchColumns.TryGetValue(key, out var branchColumn);

			if (!hasBase || !hasBranch)
			{
				changed.Add(key);
				continue;
			}

			var baseValue = GetColumnValue(baseColumn!);
			var branchValue = GetColumnValue(branchColumn!);
			if (!JsonNode.DeepEquals(baseValue, branchValue))
			{
				changed.Add(key);
			}
		}

		return changed;
	}

	private static JsonNode? GetColumnValue(JsonObject column)
	{
		return column.TryGetPropertyValue("Value", out var value)
			? value
			: null;
	}

	private static Dictionary<string, int> BuildTargetColumnIndex(JsonArray columns)
	{
		var result = new Dictionary<string, int>(StringComparer.Ordinal);
		for (var index = 0; index < columns.Count; index++)
		{
			if (columns[index] is JsonObject column && TryGetString(column, "SchemaColumnUId", out var uid))
			{
				result[uid] = index;
			}
		}
		return result;
	}

	private static void ApplyColumn(
		JsonArray targetColumns,
		IDictionary<string, int> targetIndexes,
		IReadOnlyDictionary<string, JsonObject> sourceColumns,
		string schemaColumnUid)
	{
		if (!sourceColumns.TryGetValue(schemaColumnUid, out var sourceColumn))
		{
			if (targetIndexes.TryGetValue(schemaColumnUid, out var targetIndex))
			{
				targetColumns[targetIndex] = null;
				targetIndexes.Remove(schemaColumnUid);
			}
			return;
		}

		if (!targetIndexes.TryGetValue(schemaColumnUid, out var index))
		{
			targetIndexes[schemaColumnUid] = targetColumns.Count;
			targetColumns.Add(sourceColumn.DeepClone());
			return;
		}

		targetColumns[index] = sourceColumn.DeepClone();
	}

	private static bool TryBuildRowIndex(
		JsonArray rows,
		bool useLocalizedRowKeys,
		IReadOnlyList<string>? keyColumnUids,
		MergeContext context,
		string source,
		out RowIndex rowIndex,
		out string error)
	{
		var order = new List<string>();
		var values = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
		error = string.Empty;

		for (var i = 0; i < rows.Count; i++)
		{
			if (rows[i] is not JsonObject row)
			{
				var conflict = $"InvalidRow:{source}:{i}";
				context.AddConflict(conflict);
				if (useLocalizedRowKeys)
				{
					error = $"Automatic merge is not possible because row {i} in {source} is invalid.";
					rowIndex = default;
					return false;
				}

				continue;
			}

			if (!TryBuildRowKey(row, useLocalizedRowKeys, keyColumnUids, out var key, out var keyError))
			{
				var conflict = $"MissingRowKey:{source}:{i}:{keyError}";
				context.AddConflict(conflict);
				if (useLocalizedRowKeys)
				{
					error = $"Automatic merge is not possible because row {i} in {source} has an ambiguous or missing key: {keyError}";
				 rowIndex = default;
					return false;
				}

				continue;
			}

			if (JsonSemanticKeyValidator.TryFindDuplicate(row["Row"], "SchemaColumnUId", out var duplicateColumn))
			{
				error = $"Automatic merge is not possible because row {i} in {source} contains a duplicate column key at {duplicateColumn}.";
				context.AddConflict($"DuplicateColumnKey:{source}:{i}:{duplicateColumn}");
				rowIndex = default;
				return false;
			}

			if (values.ContainsKey(key))
			{
				error = $"Automatic merge is not possible because {source} contains duplicate row key '{key}'.";
				context.AddConflict($"DuplicateRowKey:{source}:{key}");
				rowIndex = default;
				return false;
			}
			else
			{
				order.Add(key);
			}

			values[key] = row;
		}

		rowIndex = new RowIndex(order, values);
		return true;
	}

	private static bool TryBuildRowKey(
		JsonObject row,
		bool useLocalizedRowKeys,
		IReadOnlyList<string>? keyColumnUids,
		out string key,
		out string error)
	{
		if (useLocalizedRowKeys)
		{
			return TryBuildLocalizedRowKey(row, out key, out error);
		}

		error = string.Empty;
		key = string.Empty;
		var parts = new List<string>();
		foreach (var keyColumnUid in keyColumnUids ?? Array.Empty<string>())
		{
			if (!TryGetRowColumnValue(row, keyColumnUid, out var value))
			{
				return false;
			}

			parts.Add(value);
		}

		if (parts.Count == 1)
		{
			key = parts[0];
			return true;
		}

		parts.Sort(StringComparer.Ordinal);
		key = string.Join("|", parts);
		return parts.Count > 0;
	}

	private static bool TryBuildLocalizedRowKey(JsonObject row, out string key, out string error)
	{
		key = string.Empty;
		error = string.Empty;
		if (row["Row"] is not JsonArray columns)
		{
			error = "Row does not contain Row array.";
			return false;
		}

		var idCandidates = new List<string>();
		var guidCandidates = new List<string>();
		foreach (var column in columns.OfType<JsonObject>())
		{
			if (TryGetString(column, "ColumnName", out var columnName) &&
				string.Equals(columnName, "Id", StringComparison.Ordinal))
			{
				if (!TryGetScalarKeyValue(column, out var idValue))
				{
					error = "Row contains ColumnName 'Id' without a scalar Value.";
					return false;
				}

				idCandidates.Add(idValue);
				continue;
			}

			if (TryGetGuidKeyValue(column, out var guidValue))
			{
				guidCandidates.Add(guidValue);
			}
		}

		if (idCandidates.Count == 1)
		{
			key = idCandidates[0];
			return true;
		}

		if (idCandidates.Count > 1)
		{
			error = "Row contains multiple ColumnName 'Id' values.";
			return false;
		}

		if (guidCandidates.Count == 1)
		{
			key = guidCandidates[0];
			return true;
		}

		error = guidCandidates.Count == 0
			? "Row does not contain a unique GUID value."
			: "Row contains multiple GUID values.";
		return false;
	}

	private static bool TryGetRowColumnValue(JsonObject row, string schemaColumnUid, out string value)
	{
		value = string.Empty;
		if (row["Row"] is not JsonArray columns)
		{
			return false;
		}

		foreach (var column in columns.OfType<JsonObject>())
		{
			if (!TryGetString(column, "SchemaColumnUId", out var uid) ||
				!string.Equals(uid, schemaColumnUid, StringComparison.Ordinal))
			{
				continue;
			}

			if (!column.TryGetPropertyValue("Value", out var valNode))
			{
				return false;
			}

			value = valNode?.ToJsonString() ?? "null";
			return true;
		}

		return false;
	}

	private static bool TryGetScalarKeyValue(JsonObject column, out string value)
	{
		value = string.Empty;
		if (!column.TryGetPropertyValue("Value", out var valueNode) || valueNode is null)
		{
			return false;
		}

		if (valueNode is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
		{
			if (string.IsNullOrWhiteSpace(stringValue))
			{
				return false;
			}

			value = stringValue;
			return true;
		}

		value = valueNode.ToJsonString();
		return !string.IsNullOrWhiteSpace(value);
	}

	private static bool TryGetGuidKeyValue(JsonObject column, out string value)
	{
		value = string.Empty;
		if (!TryGetScalarKeyValue(column, out var scalarValue) ||
			!Guid.TryParse(scalarValue, out var guidValue))
		{
			return false;
		}

		value = guidValue.ToString("D");
		return true;
	}

	private static bool IsLocalizedDataFile(string? dataFilePath)
	{
		if (string.IsNullOrWhiteSpace(dataFilePath))
		{
			return false;
		}

		var fileName = Path.GetFileName(dataFilePath);
		return !string.Equals(fileName, "data.json", StringComparison.OrdinalIgnoreCase) &&
			fileName.StartsWith("data.", StringComparison.OrdinalIgnoreCase) &&
			fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryGetString(JsonObject obj, string propertyName, out string value)
	{
		value = string.Empty;
		if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue jsonValue)
		{
			return false;
		}

		if (!jsonValue.TryGetValue<string>(out var parsed) || string.IsNullOrWhiteSpace(parsed))
		{
			return false;
		}

		value = parsed;
		return true;
	}

	private static bool TryGetBoolean(JsonObject obj, string propertyName, out bool value)
	{
		value = false;
		if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue jsonValue)
		{
			return false;
		}

		return jsonValue.TryGetValue<bool>(out value);
	}

	private static IEnumerable<string> EnumerateKeys(params IReadOnlyList<string>[] keyCollections)
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

	private sealed class MergeContext
	{
		private readonly HashSet<string> _trueConflicts = new(StringComparer.Ordinal);

		public IReadOnlyList<string> TrueConflicts => _trueConflicts.ToArray();

		public void AddConflict(string conflict)
		{
			if (!string.IsNullOrWhiteSpace(conflict))
			{
				_trueConflicts.Add(conflict);
			}
		}
	}

	private readonly record struct RowIndex(
		IReadOnlyList<string> Order,
		IReadOnlyDictionary<string, JsonObject> Values);
}
