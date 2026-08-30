using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Creatio.ConflictResolver;

internal sealed class DescriptorAutomergeFormatter : IAutomergeConflictFormatter
{
	private static readonly Regex TimestampRegex = new(
		"^/Date\\((\\d+)\\)/$",
		RegexOptions.CultureInvariant | RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	private static readonly Regex ColumnConflictRegex = new(
		"^Columns\\[(.+)\\]\\.(.+)$",
		RegexOptions.CultureInvariant | RegexOptions.Compiled,
		TimeSpan.FromSeconds(1));

	public bool CanFormat(MergeRequest request, MergeResult result) =>
		request.FileType == ConflictFileType.DescriptorJson;

	public string? TryFormat(MergeRequest request, MergeResult result, IReadOnlyCollection<string> conflictTokens)
	{
		return TryFormatDescriptor(request, conflictTokens);
	}

	private static string? TryFormatDescriptor(MergeRequest request, IReadOnlyCollection<string> conflictTokens)
	{
		if (!TryParseRoot(request.Base, out var baseRoot) ||
		    !TryParseRoot(request.Local, out var localRoot) ||
		    !TryParseRoot(request.Remote, out var remoteRoot))
		{
			return null;
		}

		var useLocal = TryGetTimestamp(GetDescriptor(localRoot), out var localTimestamp) &&
		               TryGetTimestamp(GetDescriptor(remoteRoot), out var remoteTimestamp)
			? localTimestamp >= remoteTimestamp
			: true;

		var mergedRoot = MergeDataBindingDescriptor(baseRoot, localRoot, remoteRoot, useLocal);
		var mappedConflictPaths = conflictTokens
			.Select(MapConflictPath)
			.OfType<string>()
			.Where(static path => !string.IsNullOrWhiteSpace(path))
			.ToArray();
		if (mappedConflictPaths.Length == 0)
		{
			return null;
		}

		var options = new JsonConflictRenderOptions(
			"  ",
			DetectNewLine(request.Local),
			new JsonSerializerOptions
			{
				Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
			},
			ArrayItemKeyResolver: static (arrayPath, item) =>
			{
				if (!string.Equals(arrayPath, "$.Descriptor.Columns", StringComparison.Ordinal))
				{
					return null;
				}

				return GetString(item, "ColumnUId") ?? GetString(item, "UId");
			});

		return JsonConflictMarkerSerializer.Serialize(
			baseRoot,
			mergedRoot,
			localRoot,
			remoteRoot,
			mappedConflictPaths,
			options);
	}

	private static bool TryParseRoot(string content, out JsonObject root)
	{
		try
		{
			root = JsonNode.Parse(content) as JsonObject ?? new JsonObject();
			return root.Count > 0;
		}
		catch (JsonException)
		{
			root = new JsonObject();
			return false;
		}
	}

	private static JsonObject MergeDataBindingDescriptor(JsonObject baseRoot, JsonObject localRoot, JsonObject remoteRoot, bool useLocal)
	{
		var winnerRoot = (JsonObject)(useLocal ? localRoot : remoteRoot).DeepClone();
		if (winnerRoot["Descriptor"] is not JsonObject winnerDescriptor)
		{
			return winnerRoot;
		}

		var baseDescriptor = GetDescriptor(baseRoot);
		var localDescriptor = GetDescriptor(localRoot);
		var remoteDescriptor = GetDescriptor(remoteRoot);
		if (localDescriptor is null || remoteDescriptor is null)
		{
			return winnerRoot;
		}

		winnerDescriptor["Columns"] = MergeColumns(baseDescriptor, localDescriptor, remoteDescriptor, useLocal);
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
		foreach (var property in baseObj)
		{
			keys.Add(property.Key);
		}

		foreach (var property in localColumn)
		{
			keys.Add(property.Key);
		}

		foreach (var property in remoteColumn)
		{
			keys.Add(property.Key);
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

	private static string? MapConflictPath(string conflictToken)
	{
		if (string.IsNullOrWhiteSpace(conflictToken))
		{
			return null;
		}

		if (conflictToken.StartsWith("$", StringComparison.Ordinal))
		{
			return conflictToken;
		}

		if (string.Equals(conflictToken, "Columns.Count", StringComparison.Ordinal) ||
		    string.Equals(conflictToken, "Columns.UId", StringComparison.Ordinal))
		{
			return "$.Descriptor.Columns";
		}

		var columnMatch = ColumnConflictRegex.Match(conflictToken);
		if (columnMatch.Success)
		{
			return $"$.Descriptor.Columns[{columnMatch.Groups[1].Value}].{columnMatch.Groups[2].Value}";
		}

		return $"$.Descriptor.{conflictToken}";
	}

	private static JsonObject? GetDescriptor(JsonObject root) => root["Descriptor"] as JsonObject;

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
			if (!string.IsNullOrWhiteSpace(uid))
			{
					result[uid!] = column;
			}
		}

		return result;
	}

	private static bool TryGetTimestamp(JsonObject? descriptor, out long timestamp)
	{
		timestamp = 0;
		var raw = GetString(descriptor, "ModifiedOnUtc");
		if (string.IsNullOrWhiteSpace(raw))
		{
			return false;
		}

		var match = TimestampRegex.Match(raw);
		return match.Success && long.TryParse(match.Groups[1].Value, out timestamp);
	}

	private static string? GetString(JsonObject? obj, string propertyName)
	{
		if (obj is null || !obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue value)
		{
			return null;
		}

		return value.TryGetValue<string>(out var result) ? result : null;
	}

	private static bool IsProtectedColumnProperty(string propertyName)
	{
		return string.Equals(propertyName, "IsKey", StringComparison.Ordinal) ||
		       string.Equals(propertyName, "DataTypeValueUId", StringComparison.Ordinal);
	}

	private static string DetectNewLine(string content) =>
		content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
