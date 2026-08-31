using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver.Strategies;

internal sealed class FlatDiffMetadataMergeStrategy : IMergeStrategy, IMetadataMergeStrategy
{
	private const int MaxFlatOperationsPerStage = 2_500;
	private static readonly Regex HeaderRegex = new(
		"^([=+\\-~])\\s+(\\S+)(?:\\s+(.*))?$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant,
		TimeSpan.FromSeconds(1));
	
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

			return HeaderRegex.IsMatch(line);
		}

		return false;
	}
	public bool CanHandle(ConflictFileType fileType) => fileType == ConflictFileType.MetadataJson;

	public bool CanHandle(MergeRequest request)
	{
		return LooksLikeFlat(request.Base) &&
		       LooksLikeFlat(request.Local) &&
		       LooksLikeFlat(request.Remote);
	}

	public MergeResult Merge(MergeRequest request)
	{
		if (ExceedsOperationBudget(request.Base) ||
			ExceedsOperationBudget(request.Local) ||
			ExceedsOperationBudget(request.Remote))
		{
			return MergeResultFactory.InvalidInput(
				"FlatOperationLimitExceeded",
				$"Flat metadata exceeds the {MaxFlatOperationsPerStage}-operation limit.");
		}

		var flatDiffTranspiler = new FlatDiffTranspiler();
		var localTranspiled = flatDiffTranspiler.Transform(request.Local);
		var remoteTranspiled = flatDiffTranspiler.Transform(request.Remote);
		var baseTranspiled = flatDiffTranspiler.Transform(request.Base);
		if (HasAmbiguousDuplicateRootItems(baseTranspiled) ||
			HasAmbiguousDuplicateRootItems(localTranspiled) ||
			HasAmbiguousDuplicateRootItems(remoteTranspiled))
		{
			return MergeRepeatedOperationStreams(request);
		}
		var mergeRequest = new MergeRequest(
			request.FileType,
			baseTranspiled,
			localTranspiled,
			remoteTranspiled,
			request.FilePath);
		var mergeStrategy = new JsonMetadataMergeStrategy();
		var result = mergeStrategy.Merge(mergeRequest);
		if (result.Status != MergeStatus.Resolved || string.IsNullOrWhiteSpace(result.MergedContent))
		{
			return result;
		}

		var mergedTransformedContent = result.MergedContent!;
		if (JsonMetadataReorderer.TryReorder(
			baseTranspiled,
			localTranspiled,
			remoteTranspiled,
			mergedTransformedContent,
			out var orderedMergedTransformedContent))
		{
			mergedTransformedContent = orderedMergedTransformedContent;
		}

		var mergedContent = flatDiffTranspiler.Restore(mergedTransformedContent);
		return new MergeResult()
		{
			Status = result.Status,
			MergedContent = mergedContent,
			ErrorCode = result.ErrorCode,
			ErrorMessage = result.ErrorMessage,
			Report = result.Report
		};
	}

	private static bool ExceedsOperationBudget(string content)
	{
		int operationCount = 0;
		using var reader = new StringReader(content);
		while (reader.ReadLine() is { } line)
		{
			if (line.Length >= 2 && line[0] is '=' or '+' or '-' or '~' && char.IsWhiteSpace(line[1]) &&
				++operationCount > MaxFlatOperationsPerStage)
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasAmbiguousDuplicateRootItems(string content)
	{
		if (JsonNode.Parse(content)?["Items"] is not JsonArray items)
		{
			return false;
		}

		var seen = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonObject item in items.OfType<JsonObject>())
		{
			if (item["UId"] is not JsonValue value || !value.TryGetValue<string>(out string? key))
			{
				continue;
			}
			if (seen.TryGetValue(key, out JsonObject? previous) && !JsonNode.DeepEquals(previous, item))
			{
				return true;
			}
			seen[key] = item;
		}

		return false;
	}

	private static MergeResult MergeRepeatedOperationStreams(MergeRequest request)
	{
		if (string.Equals(request.Local, request.Remote, StringComparison.Ordinal) ||
			string.Equals(request.Remote, request.Base, StringComparison.Ordinal))
		{
			return MergeResultFactory.Resolved(request.Local, "flat_operation_stream_3way");
		}
		if (string.Equals(request.Local, request.Base, StringComparison.Ordinal))
		{
			return MergeResultFactory.Resolved(request.Remote, "flat_operation_stream_3way");
		}

		return MergeResultFactory.UnresolvedConflict(
			"RepeatedFlatOperationConflict",
			"Repeated flat metadata operations require a whole-stream choice.",
			"flat_operation_stream_conflict",
			trueConflicts: ["flat-operation-stream"],
			mergedContent: string.Join("\n", "<<<<<<< Local", request.Local, "=======", request.Remote, ">>>>>>> Remote"));
	}
}
