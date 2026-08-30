using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver;

internal sealed class DataBindingAutomergeFormatter : IAutomergeConflictFormatter
{
	private const long MaxDescriptorFileSizeBytes = 1 * 1024 * 1024;

	public bool CanFormat(MergeRequest request, MergeResult result)
	{
		return request.FileType == ConflictFileType.DataBinding &&
		       !string.IsNullOrWhiteSpace(result.MergedContent);
	}

	public string? TryFormat(MergeRequest request, MergeResult result, IReadOnlyCollection<string> conflictTokens)
	{
		var conflictPaths = conflictTokens
			.Where(static path => path.StartsWith("$", StringComparison.Ordinal))
			.ToArray();
		if (conflictPaths.Length == 0 || string.IsNullOrWhiteSpace(result.MergedContent))
		{
			return null;
		}

		return TryFormatDataBinding(request, result.MergedContent!, conflictPaths);
	}

	private static string? TryFormatDataBinding(
		MergeRequest request,
		string mergedContent,
		IReadOnlyCollection<string> conflictPaths)
	{
		if (!TryParseJson(request.Base, out var baseNode) ||
		    !TryParseJson(request.Local, out var localNode) ||
		    !TryParseJson(request.Remote, out var remoteNode) ||
		    !TryParseJson(mergedContent, out var mergedNode))
		{
			return null;
		}

		var useLocalizedRowKeys = IsLocalizedDataFile(request.FilePath);
		IReadOnlyList<string>? keyColumnUids = null;
		if (!useLocalizedRowKeys &&
		    !TryGetKeyColumnUids(request.DescriptorContent, request.FilePath, out keyColumnUids))
		{
			return null;
		}

		var normalizedConflictPaths = conflictPaths
			.Select(NormalizeConflictPath)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		var options = new JsonConflictRenderOptions(
			"  ",
			DetectNewLine(mergedContent),
			new JsonSerializerOptions
			{
				Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
			},
			ArrayItemKeyResolver: (arrayPath, item) => ResolveArrayItemKey(arrayPath, item, useLocalizedRowKeys, keyColumnUids));

		return JsonConflictMarkerSerializer.Serialize(
			baseNode,
			mergedNode,
			localNode,
			remoteNode,
			normalizedConflictPaths,
			options);
	}

	private static string NormalizeConflictPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return path;
		}

		return path.Contains(".Row[", StringComparison.Ordinal) && path.EndsWith("]", StringComparison.Ordinal)
			? path + ".Value"
			: path;
	}

	private static string? ResolveArrayItemKey(
		string arrayPath,
		JsonObject item,
		bool useLocalizedRowKeys,
		IReadOnlyList<string>? keyColumnUids)
	{
		if (string.Equals(arrayPath, "$.PackageData", StringComparison.Ordinal))
		{
			return TryBuildRowKey(item, useLocalizedRowKeys, keyColumnUids, out var key)
				? key
				: null;
		}

		if (arrayPath.EndsWith(".Row", StringComparison.Ordinal))
		{
			return TryGetString(item, "SchemaColumnUId", out var uid) ? uid : null;
		}

		return TryGetString(item, "UId", out var objectUid) ? objectUid : null;
	}

	private static bool TryParseJson(string content, out JsonNode? node)
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

	private static bool TryGetKeyColumnUids(
		string? inlineDescriptorContent,
		string? dataFilePath,
		out IReadOnlyList<string>? keyColumnUids)
	{
		keyColumnUids = null;
		var descriptorContent = inlineDescriptorContent;
		if (string.IsNullOrWhiteSpace(descriptorContent))
		{
			if (string.IsNullOrWhiteSpace(dataFilePath))
			{
				return false;
			}

			var descriptorPath = ResolveDescriptorPath(dataFilePath!);
			if (descriptorPath is null || !TryReadDescriptorContent(descriptorPath, out descriptorContent))
			{
				return false;
			}
		}

		descriptorContent = descriptorContent!.TrimStart('\uFEFF');
		if (!TryParseJson(descriptorContent, out var descriptorNode) ||
			descriptorNode is not JsonObject root ||
			root["Descriptor"] is not JsonObject descriptor ||
			descriptor["Columns"] is not JsonArray columns)
		{
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
			return false;
		}

		keyColumnUids = keys.Distinct(StringComparer.Ordinal).ToArray();
		return true;
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

	private static bool TryReadDescriptorContent(string descriptorPath, out string descriptorContent)
	{
		descriptorContent = string.Empty;
		try
		{
			using var stream = new FileStream(descriptorPath, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (stream.Length > MaxDescriptorFileSizeBytes)
			{
				return false;
			}

			using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
			descriptorContent = reader.ReadToEnd();
			return true;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool TryBuildRowKey(
		JsonObject row,
		bool useLocalizedRowKeys,
		IReadOnlyList<string>? keyColumnUids,
		out string key)
	{
		key = string.Empty;
		if (useLocalizedRowKeys)
		{
			return TryBuildLocalizedRowKey(row, out key);
		}

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

	private static bool TryBuildLocalizedRowKey(JsonObject row, out string key)
	{
		key = string.Empty;
		if (row["Row"] is not JsonArray columns)
		{
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
			return false;
		}

		if (guidCandidates.Count == 1)
		{
			key = guidCandidates[0];
			return true;
		}

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

	private static string DetectNewLine(string content) =>
		content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
