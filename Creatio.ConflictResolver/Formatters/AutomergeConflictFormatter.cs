namespace Creatio.ConflictResolver;

internal static class AutomergeConflictFormatter
{
	private static readonly IAutomergeConflictFormatter[] Formatters =
	[
		new TimelineEntityJsonAutomergeFormatter(),
		new DescriptorAutomergeFormatter(),
		new DataBindingAutomergeFormatter(),
		new FlatMetadataAutomergeFormatter(),
		new JsonAutomergeFormatter(),
		new ResourceXmlConflictMarkerFormatter(),
		new ClientUnitJsConflictMarkerFormatter()
	];

	public static MergeResult Format(MergeRequest request, MergeResult result)
	{
		if (request.Mode != MergeMode.Automerge ||
		    (result.Status != MergeStatus.Resolved && result.Status != MergeStatus.UnresolvedConflict))
		{
			return result;
		}

		var conflictTokens = result.Report.TrueConflicts
			.Where(static path => !string.IsNullOrWhiteSpace(path))
			.ToArray();
		if (conflictTokens.Length == 0)
		{
			return result;
		}

		var formatter = Formatters.FirstOrDefault(current => current.CanFormat(request, result));
		var formattedContent = formatter?.TryFormat(request, result, conflictTokens);
		if (string.IsNullOrWhiteSpace(formattedContent))
		{
			return result;
		}

		return new MergeResult
		{
			Status = MergeStatus.AutoResolvedWithConflicts,
			MergedContent = formattedContent,
			ErrorCode = "AutoResolvedWithLogicalConflicts",
			ErrorMessage = "Automatic merge completed with logical conflicts. Manual resolution is required for conflict markers.",
			Report = new MergeReport
			{
				ResolutionType = string.IsNullOrWhiteSpace(result.Report.ResolutionType)
					? "automerge_conflict_markers"
					: $"{result.Report.ResolutionType}_automerge_conflict_markers",
				LocalAdditions = result.Report.LocalAdditions,
				RemoteAdditions = result.Report.RemoteAdditions,
				LocalDeletions = result.Report.LocalDeletions,
				RemoteDeletions = result.Report.RemoteDeletions,
				TrueConflicts = result.Report.TrueConflicts,
				VerificationPassed = result.Report.VerificationPassed,
				WinnerPolicy = "CONFLICT_MARKERS"
			}
		};
	}
}
