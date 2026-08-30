namespace Creatio.ConflictResolver;

internal interface IAutomergeConflictFormatter
{
	bool CanFormat(MergeRequest request, MergeResult result);

	string? TryFormat(MergeRequest request, MergeResult result, IReadOnlyCollection<string> conflictTokens);
}
