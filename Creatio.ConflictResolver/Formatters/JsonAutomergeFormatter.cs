using System.Text.Json;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver;

internal sealed class JsonAutomergeFormatter : IAutomergeConflictFormatter
{
	public bool CanFormat(MergeRequest request, MergeResult result)
	{
		if (string.IsNullOrWhiteSpace(result.MergedContent))
		{
			return false;
		}

		return request.FileType switch
		{
			ConflictFileType.PropertiesJson => true,
			ConflictFileType.MetadataJson => !FlatMetadataAutomergeFormatter.LooksLikeFlatMetadata(request),
			_ => false
		};
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
			new JsonSerializerOptions());

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
			options);
	}
}
