namespace Creatio.ConflictResolver.Strategies;

internal interface IMetadataMergeStrategy
{
	bool CanHandle(MergeRequest request);

	MergeResult Merge(MergeRequest request);
}
