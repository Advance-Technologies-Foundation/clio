using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver.Strategies;

internal sealed class TimelineEntityMetadataMergeStrategy : IMergeStrategy, IMetadataMergeStrategy
{
	private const string AddonSchemaManagerName = "AddonSchemaManager";
	private const string TimelineEntitySchemaType = "TimelineEntity";
	private const string TimelineEntityValuesPropertyName = "TimelineEntityValues";
	private const string ColumnLayoutsPropertyName = "ColumnLayouts";
	private const string TimelineEntityValueKeyPropertyName = "UId";
	private const string ColumnLayoutKeyPropertyName = "ColumnName";

	private static readonly JsonSerializerOptions PrettyJsonOptions = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public bool CanHandle(ConflictFileType fileType) => fileType == ConflictFileType.MetadataJson;

	public bool CanHandle(MergeRequest request)
	{
		return TryParseRootObject(request.Base, out var baseRoot, out _) &&
		       TryParseRootObject(request.Local, out var localRoot, out _) &&
		       TryParseRootObject(request.Remote, out var remoteRoot, out _) &&
		       (IsTimelineEntityAddon(baseRoot) ||
		        IsTimelineEntityAddon(localRoot) ||
		        IsTimelineEntityAddon(remoteRoot));
	}

	public MergeResult Merge(MergeRequest request)
	{
		if (!TryParseRootObject(request.Base, out var baseRoot, out var baseError))
		{
			return MergeResultFactory.InvalidInput("InvalidBaseMetadataJson", baseError);
		}

		if (!TryParseRootObject(request.Local, out var localRoot, out var localError))
		{
			return MergeResultFactory.InvalidInput("InvalidLocalMetadataJson", localError);
		}

		if (!TryParseRootObject(request.Remote, out var remoteRoot, out var remoteError))
		{
			return MergeResultFactory.InvalidInput("InvalidRemoteMetadataJson", remoteError);
		}

		var transformedBase = (JsonObject)baseRoot!.DeepClone();
		var transformedLocal = (JsonObject)localRoot!.DeepClone();
		var transformedRemote = (JsonObject)remoteRoot!.DeepClone();

		var useLegacyTimelineEntityValuesMerge =
			HasLegacyTimelineEntityValues(transformedBase) ||
			HasLegacyTimelineEntityValues(transformedLocal) ||
			HasLegacyTimelineEntityValues(transformedRemote);

		if (useLegacyTimelineEntityValuesMerge)
		{
			ReplaceTimelineEntityValuesWithSerializedArray(transformedBase);
			ReplaceTimelineEntityValuesWithSerializedArray(transformedLocal);
			ReplaceTimelineEntityValuesWithSerializedArray(transformedRemote);
		}
		else
		{
			InjectColumnLayoutKeys(transformedBase);
			InjectColumnLayoutKeys(transformedLocal);
			InjectColumnLayoutKeys(transformedRemote);
		}

		var innerRequest = new MergeRequest(
			request.FileType,
			JsonSerializer.Serialize(transformedBase, PrettyJsonOptions),
			JsonSerializer.Serialize(transformedLocal, PrettyJsonOptions),
			JsonSerializer.Serialize(transformedRemote, PrettyJsonOptions),
			request.FilePath);

		var innerResult = new JsonMetadataMergeStrategy().Merge(innerRequest);
		if (innerResult.Status != MergeStatus.Resolved || string.IsNullOrWhiteSpace(innerResult.MergedContent))
		{
			return innerResult;
		}

		if (!TryParseRootObject(innerResult.MergedContent!, out var mergedRoot, out var mergedError))
		{
			return MergeResultFactory.InvalidInput("InvalidMergedMetadataJson", mergedError);
		}

		if (useLegacyTimelineEntityValuesMerge)
		{
			RestoreTimelineEntityValuesFromSerializedArray(mergedRoot!);
		}
		else
		{
			RemoveInjectedColumnLayoutKeys(mergedRoot!);
		}

		return new MergeResult
		{
			Status = innerResult.Status,
			MergedContent = SerializeTimelineEntityContent(mergedRoot!),
			ErrorCode = innerResult.ErrorCode,
			ErrorMessage = innerResult.ErrorMessage,
			Report = innerResult.Report
		};
	}

	private static string SerializeTimelineEntityContent(JsonObject root)
	{
		return JsonSerializer.Serialize(root, PrettyJsonOptions)
			.Replace("\\u0022", "\\\"", StringComparison.Ordinal);
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

	private static bool IsTimelineEntityAddon(JsonObject? root)
	{
		if (!TryGetSchema(root, out var schema))
		{
			return false;
		}

		var timelineSettings = schema!["AD4"] as JsonObject;
		return string.Equals(GetStringProperty(schema!, "ManagerName"), AddonSchemaManagerName, StringComparison.Ordinal) &&
		       string.Equals(GetStringProperty(schema!, "AD3"), TimelineEntitySchemaType, StringComparison.Ordinal) &&
		       timelineSettings is not null;
	}

	private static bool HasLegacyTimelineEntityValues(JsonObject root)
	{
		if (!TryGetTimelineEntityValues(root, out var values))
		{
			return false;
		}

		foreach (var item in values!)
		{
			if (item is not JsonObject obj || !HasNonEmptyStringProperty(obj, TimelineEntityValueKeyPropertyName))
			{
				return true;
			}
		}

		return false;
	}

	private static void ReplaceTimelineEntityValuesWithSerializedArray(JsonObject root)
	{
		if (!TryGetTimelineEntitySettings(root, out var settings) ||
			!settings!.TryGetPropertyValue(TimelineEntityValuesPropertyName, out var currentNode) ||
			currentNode is not JsonArray values)
		{
			return;
		}

		settings[TimelineEntityValuesPropertyName] = JsonValue.Create(values.ToJsonString());
	}

	private static void RestoreTimelineEntityValuesFromSerializedArray(JsonObject root)
	{
		if (!TryGetTimelineEntitySettings(root, out var settings) ||
			!settings!.TryGetPropertyValue(TimelineEntityValuesPropertyName, out var currentNode) ||
			currentNode is not JsonValue jsonValue ||
			!jsonValue.TryGetValue<string>(out var serializedArray) ||
			string.IsNullOrWhiteSpace(serializedArray))
		{
			return;
		}

		if (JsonNode.Parse(serializedArray) is JsonArray values)
		{
			settings[TimelineEntityValuesPropertyName] = values;
		}
	}

	private static void InjectColumnLayoutKeys(JsonObject root)
	{
		if (!TryGetTimelineEntityValues(root, out var values))
		{
			return;
		}

		foreach (var timelineEntityValue in values!.OfType<JsonObject>())
		{
			if (timelineEntityValue[ColumnLayoutsPropertyName] is not JsonArray columnLayouts)
			{
				continue;
			}

			foreach (var columnLayout in columnLayouts.OfType<JsonObject>())
			{
				if (columnLayout.ContainsKey(TimelineEntityValueKeyPropertyName))
				{
					continue;
				}

				var columnName = GetStringProperty(columnLayout, ColumnLayoutKeyPropertyName);
				if (string.IsNullOrWhiteSpace(columnName))
				{
					continue;
				}

				columnLayout[TimelineEntityValueKeyPropertyName] = columnName;
			}
		}
	}

	private static void RemoveInjectedColumnLayoutKeys(JsonObject root)
	{
		if (!TryGetTimelineEntityValues(root, out var values))
		{
			return;
		}

		foreach (var timelineEntityValue in values!.OfType<JsonObject>())
		{
			if (timelineEntityValue[ColumnLayoutsPropertyName] is not JsonArray columnLayouts)
			{
				continue;
			}

			foreach (var columnLayout in columnLayouts.OfType<JsonObject>())
			{
				var columnName = GetStringProperty(columnLayout, ColumnLayoutKeyPropertyName);
				var uid = GetStringProperty(columnLayout, TimelineEntityValueKeyPropertyName);
				if (string.IsNullOrWhiteSpace(columnName) ||
					!string.Equals(uid, columnName, StringComparison.Ordinal))
				{
					continue;
				}

				columnLayout.Remove(TimelineEntityValueKeyPropertyName);
			}
		}
	}

	private static bool TryGetTimelineEntityValues(JsonObject root, out JsonArray? values)
	{
		values = null;
		if (!TryGetTimelineEntitySettings(root, out var settings) ||
			settings![TimelineEntityValuesPropertyName] is not JsonArray timelineEntityValues)
		{
			return false;
		}

		values = timelineEntityValues;
		return true;
	}

	private static bool TryGetTimelineEntitySettings(JsonObject? root, out JsonObject? settings)
	{
		settings = null;
		if (!TryGetSchema(root, out var schema) ||
			schema!["AD4"] is not JsonObject timelineSettings)
		{
			return false;
		}

		settings = timelineSettings;
		return true;
	}

	private static bool TryGetSchema(JsonObject? root, out JsonObject? schema)
	{
		schema = null;
		if (root?["MetaData"] is not JsonObject metadata ||
			metadata["Schema"] is not JsonObject schemaNode)
		{
			return false;
		}

		schema = schemaNode;
		return true;
	}

	private static string? GetStringProperty(JsonObject obj, string propertyName)
	{
		if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue jsonValue)
		{
			return null;
		}

		return jsonValue.TryGetValue<string>(out var value) ? value : null;
	}

	private static bool HasNonEmptyStringProperty(JsonObject obj, string propertyName)
	{
		return !string.IsNullOrWhiteSpace(GetStringProperty(obj, propertyName));
	}
}
