namespace Creatio.ConflictResolver.Strategies;

internal sealed class ManualMergeRequiredStrategy : IMergeStrategy
{
	private static readonly HashSet<ConflictFileType> SupportedFileTypes =
	[
		ConflictFileType.SourceCode,
		ConflictFileType.SqlScript,
		ConflictFileType.ProcessMetadataJson,
		ConflictFileType.ProcessResourceXml
	];

	public bool CanHandle(ConflictFileType fileType)
	{
		return SupportedFileTypes.Contains(fileType);
	}

	public MergeResult Merge(MergeRequest request)
	{
		if (string.Equals(request.Local, request.Remote, StringComparison.Ordinal))
		{
			return MergeResultFactory.Resolved(request.Local, "text_3way_trivial");
		}

		if (string.Equals(request.Local, request.Base, StringComparison.Ordinal))
		{
			return MergeResultFactory.Resolved(request.Remote, "text_3way_trivial");
		}

		if (string.Equals(request.Remote, request.Base, StringComparison.Ordinal))
		{
			return MergeResultFactory.Resolved(request.Local, "text_3way_trivial");
		}

		return MergeResultFactory.ManualMergeRequired(request.FileType, request.Local, request.Remote);
	}
}
