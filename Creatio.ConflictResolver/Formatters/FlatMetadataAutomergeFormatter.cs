using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Creatio.ConflictResolver;

internal sealed class FlatMetadataAutomergeFormatter : IAutomergeConflictFormatter
{
	private static readonly JsonSerializerOptions PrettyJsonOptions = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private static readonly Regex FlatMetadataHeaderRegex = new(
		"^([=+\\-~])\\s+(\\S+)(?:\\s+(.*))?$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	public bool CanFormat(MergeRequest request, MergeResult result)
	{
		return request.FileType == ConflictFileType.MetadataJson &&
		       !string.IsNullOrWhiteSpace(result.MergedContent) &&
		       LooksLikeFlatMetadata(request);
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

		try
		{
			var transpiler = new FlatDiffTranspiler();
			var baseTransformed = transpiler.Transform(request.Base);
			var localTransformed = transpiler.Transform(request.Local);
			var remoteTransformed = transpiler.Transform(request.Remote);
			var mergedTransformed = transpiler.Transform(result.MergedContent!);

			return RestoreWithConflictMarkers(
				baseTransformed,
				localTransformed,
				remoteTransformed,
				mergedTransformed,
				result.MergedContent!,
				conflictPaths);
		}
		catch (FormatException)
		{
			return null;
		}
	}

	private static string RestoreWithConflictMarkers(
		string baseTransformedMetadata,
		string localTransformedMetadata,
		string remoteTransformedMetadata,
		string mergedTransformedMetadata,
		string sourceContent,
		IEnumerable<string> conflictPaths)
	{
		var baseItems = FlatDiffTranspiler.ParseTransformedMetadata(baseTransformedMetadata);
		var localItems = FlatDiffTranspiler.ParseTransformedMetadata(localTransformedMetadata);
		var remoteItems = FlatDiffTranspiler.ParseTransformedMetadata(remoteTransformedMetadata);
		var mergedItems = FlatDiffTranspiler.ParseTransformedMetadata(mergedTransformedMetadata);
		var conflictPathSet = conflictPaths
			.Where(static path => !string.IsNullOrWhiteSpace(path))
			.ToHashSet(StringComparer.Ordinal);
		var newLine = FlatDiffTranspiler.DetectNewLine(sourceContent);

		var baseIndex = BuildItemIndex(baseItems);
		var localIndex = BuildItemIndex(localItems);
		var remoteIndex = BuildItemIndex(remoteItems);
		var mergedIndex = BuildItemIndex(mergedItems);
		var renderItems = IncludeConflictOnlyItems(mergedItems, localItems, remoteItems, conflictPathSet);

		var outputLines = new List<string>();
		foreach (var item in renderItems)
		{
			var conflictItemUid = FlatDiffTranspiler.AddHasBodyMarker(item.UId, item.HasBody);
			var itemPath = $"$.Items[{conflictItemUid}]";
			var bodyPath = $"{itemPath}.Body";

			baseIndex.TryGetValue(item.UId, out var baseItem);
			localIndex.TryGetValue(item.UId, out var localItem);
			remoteIndex.TryGetValue(item.UId, out var remoteItem);
			mergedIndex.TryGetValue(item.UId, out var mergedItem);

			if (IsEntitySchemaColumnItem(item) && conflictPathSet.Contains(itemPath))
			{
				AppendWholeItemConflict(outputLines, localItem, remoteItem);
				continue;
			}

			if (IsEntitySchemaColumnCollection(item) && conflictPathSet.Contains(bodyPath) &&
			    TryBuildPrimitiveArrayChoices(mergedItem, baseItem, localItem, remoteItem,
				    out var localChoice, out var remoteChoice))
			{
				AppendWholeItemConflict(outputLines, localChoice, remoteChoice);
				continue;
			}

			var bodyText = item.HasBody
				? SerializeItemBody(
					item,
					baseItem,
					localItem,
					remoteItem,
					bodyPath,
					conflictPathSet,
					newLine)
				: null;

			if (!item.HasBody)
			{
				outputLines.Add($"{item.Operation} {FlatDiffTranspiler.BuildFlatPath(item)}");
				continue;
			}

			var bodyLines = FlatDiffTranspiler.SplitLines(bodyText!);
			if (bodyLines.Count == 1)
			{
				outputLines.Add($"{item.Operation} {FlatDiffTranspiler.BuildFlatPath(item)} {bodyLines[0]}");
				continue;
			}

			outputLines.Add($"{item.Operation} {FlatDiffTranspiler.BuildFlatPath(item)} {bodyLines[0]}");
			for (var i = 1; i < bodyLines.Count; i++)
			{
				outputLines.Add(bodyLines[i]);
			}
		}

		var output = string.Join(newLine, outputLines);
		if (FlatDiffTranspiler.HasTrailingNewLine(sourceContent))
		{
			output += newLine;
		}

		return output;
	}

	private static IReadOnlyList<FlatDiffTranspiler.TransformedItem> IncludeConflictOnlyItems(
		IReadOnlyList<FlatDiffTranspiler.TransformedItem> mergedItems,
		IReadOnlyList<FlatDiffTranspiler.TransformedItem> localItems,
		IReadOnlyList<FlatDiffTranspiler.TransformedItem> remoteItems,
		ISet<string> conflictPaths)
	{
		var result = mergedItems.ToList();
		var knownUIds = result.Select(static item => item.UId).ToHashSet(StringComparer.Ordinal);
		foreach (var candidate in remoteItems.Concat(localItems))
		{
			var candidatePath = $"$.Items[{FlatDiffTranspiler.AddHasBodyMarker(candidate.UId, candidate.HasBody)}]";
			if (!IsEntitySchemaColumnItem(candidate) ||
			    knownUIds.Contains(candidate.UId) ||
			    !conflictPaths.Contains(candidatePath))
			{
				continue;
			}

			var flatPath = FlatDiffTranspiler.BuildFlatPath(candidate);
			var insertAt = result.FindIndex(item =>
				item.Operation == '~' &&
				string.Equals(FlatDiffTranspiler.BuildFlatPath(item), flatPath, StringComparison.Ordinal));
			result.Insert(insertAt < 0 ? result.Count : insertAt, candidate);
			knownUIds.Add(candidate.UId);
		}
		return result;
	}

	private static bool IsEntitySchemaColumnItem(FlatDiffTranspiler.TransformedItem item) =>
		item.Operation == '+' &&
		string.Equals(FlatDiffTranspiler.BuildFlatPath(item), "MetaData.Schema.D2", StringComparison.Ordinal);

	private static bool IsEntitySchemaColumnCollection(FlatDiffTranspiler.TransformedItem item) =>
		item.Operation == '~' &&
		string.Equals(FlatDiffTranspiler.BuildFlatPath(item), "MetaData.Schema.D2", StringComparison.Ordinal);

	private static void AppendWholeItemConflict(
		ICollection<string> outputLines,
		FlatDiffTranspiler.TransformedItem? localItem,
		FlatDiffTranspiler.TransformedItem? remoteItem)
	{
		outputLines.Add("<<<<<<< Local");
		if (localItem is not null)
		{
			foreach (var line in SerializeFlatItem(localItem))
			{
				outputLines.Add(line);
			}
		}
		outputLines.Add("=======");
		if (remoteItem is not null)
		{
			foreach (var line in SerializeFlatItem(remoteItem))
			{
				outputLines.Add(line);
			}
		}
		outputLines.Add(">>>>>>> Remote");
	}

	private static IReadOnlyList<string> SerializeFlatItem(FlatDiffTranspiler.TransformedItem item)
	{
		var header = $"{item.Operation} {FlatDiffTranspiler.BuildFlatPath(item)}";
		if (!item.HasBody)
		{
			return [header];
		}

		var bodyLines = FlatDiffTranspiler.SplitLines(FlatDiffTranspiler.SerializeBody(item.Body, item.Inline));
		var result = new List<string> { $"{header} {bodyLines[0]}" };
		for (var index = 1; index < bodyLines.Count; index++)
		{
			result.Add(bodyLines[index]);
		}
		return result;
	}

	private static bool TryBuildPrimitiveArrayChoices(
		FlatDiffTranspiler.TransformedItem? mergedItem,
		FlatDiffTranspiler.TransformedItem? baseItem,
		FlatDiffTranspiler.TransformedItem? localItem,
		FlatDiffTranspiler.TransformedItem? remoteItem,
		out FlatDiffTranspiler.TransformedItem? localChoice,
		out FlatDiffTranspiler.TransformedItem? remoteChoice)
	{
		localChoice = null;
		remoteChoice = null;
		if (mergedItem?.Body is not JsonArray mergedArray ||
		    baseItem?.Body is not JsonArray baseArray ||
		    localItem?.Body is not JsonArray localArray ||
		    remoteItem?.Body is not JsonArray remoteArray ||
		    !ArePrimitiveArrays(mergedArray, baseArray, localArray, remoteArray))
		{
			return false;
		}

		var localChoiceArray = (JsonArray)mergedArray.DeepClone();
		var remoteChoiceArray = (JsonArray)mergedArray.DeepClone();
		foreach (var baseValue in baseArray)
		{
			if (!ContainsValue(localArray, baseValue) && ContainsValue(remoteArray, baseValue))
			{
				InsertInBaseOrder(remoteChoiceArray, baseArray, baseValue);
			}
		}

		localChoice = mergedItem with { Body = localChoiceArray };
		remoteChoice = mergedItem with { Body = remoteChoiceArray };
		return true;
	}

	private static bool ArePrimitiveArrays(params JsonArray[] arrays) =>
		arrays.All(array => array.All(static item => item is not JsonObject and not JsonArray));

	private static bool ContainsValue(JsonArray array, JsonNode? value) =>
		array.Any(item => JsonNode.DeepEquals(item, value));

	private static void InsertInBaseOrder(JsonArray target, JsonArray baseArray, JsonNode? value)
	{
		if (ContainsValue(target, value))
		{
			return;
		}

		var baseIndex = IndexOf(baseArray, value);
		for (var index = baseIndex - 1; index >= 0; index--)
		{
			var targetIndex = IndexOf(target, baseArray[index]);
			if (targetIndex >= 0)
			{
				target.Insert(targetIndex + 1, value?.DeepClone());
				return;
			}
		}

		for (var index = baseIndex + 1; index < baseArray.Count; index++)
		{
			var targetIndex = IndexOf(target, baseArray[index]);
			if (targetIndex >= 0)
			{
				target.Insert(targetIndex, value?.DeepClone());
				return;
			}
		}

		target.Insert(0, value?.DeepClone());
	}

	private static int IndexOf(JsonArray array, JsonNode? value)
	{
		for (var index = 0; index < array.Count; index++)
		{
			if (JsonNode.DeepEquals(array[index], value))
			{
				return index;
			}
		}
		return -1;
	}

	private static string SerializeItemBody(
		FlatDiffTranspiler.TransformedItem mergedItem,
		FlatDiffTranspiler.TransformedItem? baseItem,
		FlatDiffTranspiler.TransformedItem? localItem,
		FlatDiffTranspiler.TransformedItem? remoteItem,
		string bodyPath,
		ISet<string> conflictPaths,
		string newLine)
	{
		var hasRelevantConflicts = conflictPaths.Any(path =>
			string.Equals(path, bodyPath, StringComparison.Ordinal) ||
			path.StartsWith(bodyPath + ".", StringComparison.Ordinal) ||
			path.StartsWith(bodyPath + "[", StringComparison.Ordinal));

		if (!hasRelevantConflicts)
		{
			return FlatDiffTranspiler.SerializeBody(mergedItem.Body, mergedItem.Inline);
		}

		var renderOptions = new JsonConflictRenderOptions(
			"  ",
			newLine,
			PrettyJsonOptions);

		return JsonConflictMarkerSerializer.Serialize(
			baseItem?.Body,
			mergedItem.Body,
			localItem?.Body,
			remoteItem?.Body,
			conflictPaths,
			renderOptions,
			bodyPath);
	}

	private static IReadOnlyDictionary<string, FlatDiffTranspiler.TransformedItem> BuildItemIndex(
		IReadOnlyList<FlatDiffTranspiler.TransformedItem> items)
	{
		var index = new Dictionary<string, FlatDiffTranspiler.TransformedItem>(StringComparer.Ordinal);
		foreach (var item in items)
		{
			index[item.UId] = item;
		}

		return index;
	}

	internal static bool LooksLikeFlatMetadata(MergeRequest request)
	{
		return LooksLikeFlat(request.Base) &&
		       LooksLikeFlat(request.Local) &&
		       LooksLikeFlat(request.Remote);
	}

	private static bool LooksLikeFlat(string content)
	{
		var splitLines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Split('\n')
			.ToArray();
		foreach (var line in splitLines)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			return FlatMetadataHeaderRegex.IsMatch(line);
		}

		return false;
	}
}
