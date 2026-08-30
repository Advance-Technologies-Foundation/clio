namespace Creatio.ConflictResolver.Strategies;

internal class PropertiesJsonMergeStrategy : IMergeStrategy
{
	private IMergeStrategy _mergeStrategy = new JsonMetadataMergeStrategy();
	public bool CanHandle(ConflictFileType fileType)
	{
		return fileType == ConflictFileType.PropertiesJson;
	}

	public MergeResult Merge(MergeRequest request)
	{
		return _mergeStrategy.Merge(request);
	}
}
