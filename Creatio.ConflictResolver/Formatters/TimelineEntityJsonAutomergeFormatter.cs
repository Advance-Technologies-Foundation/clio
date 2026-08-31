using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver;

internal sealed class TimelineEntityJsonAutomergeFormatter : IAutomergeConflictFormatter
{
	private const string AddonSchemaManagerName = "AddonSchemaManager";
	private const string TimelineEntitySchemaType = "TimelineEntity";

	public bool CanFormat(MergeRequest request, MergeResult result)
	{
		if (request.FileType != ConflictFileType.MetadataJson || string.IsNullOrWhiteSpace(result.MergedContent))
		{
			return false;
		}

		return IsTimelineEntityContent(request.Base) ||
		       IsTimelineEntityContent(request.Local) ||
		       IsTimelineEntityContent(request.Remote) ||
		       IsTimelineEntityContent(result.MergedContent!);
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

		if (!JsonAutomergeFormattingSupport.TryParseJson(request.Base, out var baseNode) ||
		    !JsonAutomergeFormattingSupport.TryParseJson(request.Local, out var localNode) ||
		    !JsonAutomergeFormattingSupport.TryParseJson(request.Remote, out var remoteNode) ||
		    !JsonAutomergeFormattingSupport.TryParseJson(result.MergedContent!, out var mergedNode))
		{
			return null;
		}

		var options = new JsonConflictRenderOptions(
			"  ",
			JsonAutomergeFormattingSupport.DetectNewLine(result.MergedContent!),
			new JsonSerializerOptions
			{
				Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
			},
			ArrayItemKeyResolver: ResolveArrayItemKey);

		var normalizedConflictPaths = JsonAutomergeFormattingSupport.NormalizeJsonConflictPaths(
			baseNode,
			mergedNode,
			localNode,
			remoteNode,
			conflictPaths,
			options);
		if (normalizedConflictPaths.Length == 0)
		{
			return null;
		}

		return JsonConflictMarkerSerializer.Serialize(
				baseNode,
				mergedNode,
				localNode,
				remoteNode,
				normalizedConflictPaths,
				options)
			.Replace("\\u0022", "\\\"", StringComparison.Ordinal);
	}

	private static string? ResolveArrayItemKey(string arrayPath, JsonObject item)
	{
		if (!arrayPath.EndsWith(".ColumnLayouts", StringComparison.Ordinal))
		{
			return null;
		}

		return TryGetStringProperty(item, "ColumnName", out var columnName)
			? columnName
			: null;
	}

	private static bool IsTimelineEntityContent(string content)
	{
		return JsonAutomergeFormattingSupport.TryParseJson(content, out var node) &&
		       node is JsonObject root &&
		       IsTimelineEntityRoot(root);
	}

	private static bool IsTimelineEntityRoot(JsonObject root)
	{
		if (root["MetaData"] is not JsonObject metadata ||
		    metadata["Schema"] is not JsonObject schema ||
		    schema["AD4"] is not JsonObject)
		{
			return false;
		}

		return TryGetStringProperty(schema, "ManagerName", out var managerName) &&
		       TryGetStringProperty(schema, "AD3", out var schemaType) &&
		       string.Equals(managerName, AddonSchemaManagerName, StringComparison.Ordinal) &&
		       string.Equals(schemaType, TimelineEntitySchemaType, StringComparison.Ordinal);
	}

	private static bool TryGetStringProperty(JsonObject obj, string propertyName, out string value)
	{
		value = string.Empty;
		if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue jsonValue)
		{
			return false;
		}

		if (!jsonValue.TryGetValue<string>(out var parsedValue) || string.IsNullOrWhiteSpace(parsedValue))
		{
			return false;
		}

		value = parsedValue;
		return true;
	}
}
