namespace Creatio.ConflictResolver;

internal static class MergeResultFactory
{
	private const int MaxReportEntries = 1024;
	private const int MaxReportCharacters = 256 * 1024;
	private const int MaxReportEntryCharacters = 2048;
	public static MergeResult Resolved(
		string mergedContent,
		string resolutionType,
		IEnumerable<string>? localAdditions = null,
		IEnumerable<string>? remoteAdditions = null,
		IEnumerable<string>? localDeletions = null,
		IEnumerable<string>? remoteDeletions = null,
		IEnumerable<string>? trueConflicts = null,
		bool verificationPassed = true,
		string winnerPolicy = "LOCAL")
	{
		return new MergeResult
		{
			Status = MergeStatus.Resolved,
			MergedContent = mergedContent,
			Report = CreateReport(
				resolutionType,
				localAdditions,
				remoteAdditions,
				localDeletions,
				remoteDeletions,
				trueConflicts,
				verificationPassed,
				winnerPolicy)
		};
	}

	public static MergeResult UnsupportedType(ConflictFileType fileType)
	{
		return new MergeResult
		{
			Status = MergeStatus.UnsupportedType,
			Report = CreateReport(
				"unsupported_type",
				verificationPassed: false),
			ErrorCode = "UnsupportedType",
			ErrorMessage = $"Merge strategy for '{fileType}' is not implemented."
		};
	}

	public static MergeResult InvalidInput(string errorCode, string errorMessage, string resolutionType = "invalid_input")
	{
		return new MergeResult
		{
			Status = MergeStatus.InvalidInput,
			Report = CreateReport(
				resolutionType,
				verificationPassed: false),
			ErrorCode = errorCode,
			ErrorMessage = errorMessage
		};
	}

	public static MergeResult UnresolvedConflict(
		string errorCode,
		string errorMessage,
		string resolutionType,
		IEnumerable<string>? trueConflicts = null,
		string? mergedContent = null,
		IEnumerable<string>? localAdditions = null,
		IEnumerable<string>? remoteAdditions = null,
		IEnumerable<string>? localDeletions = null,
		IEnumerable<string>? remoteDeletions = null,
		bool verificationPassed = false)
	{
		return new MergeResult
		{
			Status = MergeStatus.UnresolvedConflict,
			MergedContent = mergedContent,
			Report = CreateReport(
				resolutionType,
				localAdditions,
				remoteAdditions,
				localDeletions,
				remoteDeletions,
				trueConflicts,
				verificationPassed,
				"LOCAL"),
			ErrorCode = errorCode,
			ErrorMessage = errorMessage
		};
	}

	public static MergeResult ManualMergeRequired(
		ConflictFileType fileType,
		string localContent,
		string remoteContent,
		string resolutionType = "manual_merge_required")
	{
		return new MergeResult
		{
			Status = MergeStatus.ManualMergeRequired,
			MergedContent = BuildWholeFileConflictMarkers(localContent, remoteContent),
			Report = CreateReport(
				resolutionType,
				trueConflicts: new[] { "full_file" },
				verificationPassed: false,
				winnerPolicy: "CONFLICT_MARKERS"),
			ErrorCode = "ManualMergeRequired",
			ErrorMessage = $"Merge conflict for '{fileType}' must be resolved manually."
		};
	}

	private static string BuildWholeFileConflictMarkers(string localContent, string remoteContent)
	{
		return string.Join(
			"\n",
			"<<<<<<< Local",
			localContent ?? string.Empty,
			"=======",
			remoteContent ?? string.Empty,
			">>>>>>> Remote");
	}

	private static MergeReport CreateReport(
		string resolutionType,
		IEnumerable<string>? localAdditions = null,
		IEnumerable<string>? remoteAdditions = null,
		IEnumerable<string>? localDeletions = null,
		IEnumerable<string>? remoteDeletions = null,
		IEnumerable<string>? trueConflicts = null,
		bool verificationPassed = true,
		string winnerPolicy = "LOCAL")
	{
		return new MergeReport
		{
			ResolutionType = resolutionType,
			LocalAdditions = ToSortedList(localAdditions),
			RemoteAdditions = ToSortedList(remoteAdditions),
			LocalDeletions = ToSortedList(localDeletions),
			RemoteDeletions = ToSortedList(remoteDeletions),
			TrueConflicts = ToSortedList(trueConflicts),
			WinnerPolicy = winnerPolicy,
			VerificationPassed = verificationPassed
		};
	}

	private static IReadOnlyList<string> ToSortedList(IEnumerable<string>? values)
	{
		if (values is null)
		{
			return Array.Empty<string>();
		}

		var result = new HashSet<string>(StringComparer.Ordinal);
		var characters = 0;
		foreach (var value in values)
		{
			if (string.IsNullOrWhiteSpace(value) || !result.Add(value))
			{
				continue;
			}

			characters += value.Length;
			if (result.Count > MaxReportEntries ||
				value.Length > MaxReportEntryCharacters ||
				characters > MaxReportCharacters)
			{
				throw new MergeReportLimitExceededException();
			}
		}

		return result.OrderBy(static x => x, StringComparer.Ordinal).ToArray();
	}
}

internal sealed class MergeReportLimitExceededException : Exception;
