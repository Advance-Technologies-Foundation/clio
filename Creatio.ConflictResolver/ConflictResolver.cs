namespace Creatio.ConflictResolver;

/// <summary>
/// Performs semantic three-way merges for supported Creatio package artifacts.
/// </summary>
public sealed class ConflictResolver : IConflictResolver
{
	private readonly IReadOnlyList<IMergeStrategy> _strategies;

	/// <summary>
	/// Creates a resolver with the built-in Creatio merge strategies.
	/// </summary>
	public ConflictResolver()
		: this(CreateDefaultStrategies())
	{
	}

	internal ConflictResolver(IEnumerable<IMergeStrategy> strategies)
	{
		_strategies = strategies?.ToArray() ?? throw new ArgumentNullException(nameof(strategies));
	}

	/// <inheritdoc />
	public MergeResult Resolve(MergeRequest request)
	{
		if (request is null)
		{
			return MergeResultFactory.InvalidInput("RequestIsNull", "MergeRequest cannot be null.");
		}

		if (string.IsNullOrWhiteSpace(request.Base) ||
			string.IsNullOrWhiteSpace(request.Local) ||
			string.IsNullOrWhiteSpace(request.Remote))
		{
			return MergeResultFactory.InvalidInput(
				"EmptyContent",
				"Base, Local and Remote content must be non-empty strings.");
		}

		var strategy = _strategies.FirstOrDefault(s => s.CanHandle(request.FileType));
		if (strategy is null)
		{
			return MergeResultFactory.UnsupportedType(request.FileType);
		}

		try
		{
			var result = strategy.Merge(request);
			var formatted = AutomergeConflictFormatter.Format(request, result);
			return NormalizeMergedContentLineEndings(formatted, request);
		}
		catch (MergeReportLimitExceededException)
		{
			return MergeResultFactory.InvalidInput(
				"MergeReportLimitExceeded",
				"The semantic merge report exceeds its safe size limit.");
		}
		catch (MergeOutputLimitExceededException)
		{
			return MergeResultFactory.InvalidInput(
				"MergeOutputLimitExceeded",
				"The semantic merge output exceeds the 4 MiB limit.");
		}
	}

	// The individual strategies/formatters emit line endings inconsistently:
	// System.Text.Json (WriteIndented) uses Environment.NewLine, the XML writer hard-codes "\r\n",
	// while the JS/flat formatters preserve the source EOL. To keep the merged output deterministic
	// across platforms (and so it does not introduce spurious EOL churn into the merged file), we
	// re-apply the EOL detected from the input request to the whole merged content in one place.
	private static MergeResult NormalizeMergedContentLineEndings(MergeResult result, MergeRequest request)
	{
		if (result?.MergedContent is null)
		{
			return result!;
		}

		var newLine = DetectNewLine(request);
		var normalized = NormalizeNewLines(result.MergedContent, newLine);
		if (string.Equals(normalized, result.MergedContent, StringComparison.Ordinal))
		{
			return result;
		}

		return new MergeResult
		{
			Status = result.Status,
			MergedContent = normalized,
			Report = result.Report,
			ErrorCode = result.ErrorCode,
			ErrorMessage = result.ErrorMessage
		};
	}

	// Detect the dominant newline of the input, preferring Local ("ours" / the working-tree file),
	// then Base, then Remote. Defaults to LF when no newline is present.
	private static string DetectNewLine(MergeRequest request)
	{
		foreach (var content in new[] { request.Local, request.Base, request.Remote })
		{
			if (string.IsNullOrEmpty(content))
			{
				continue;
			}

			var index = content.IndexOf('\n');
			if (index < 0)
			{
				continue;
			}

			return index > 0 && content[index - 1] == '\r' ? "\r\n" : "\n";
		}

		return "\n";
	}

	// Collapse CRLF to LF, then expand to the target newline. Only changes EOL style — never the
	// number of line breaks — so trailing-newline behaviour from the formatters is preserved.
	private static string NormalizeNewLines(string content, string newLine)
	{
		var lf = content.Replace("\r\n", "\n");
		return newLine == "\n" ? lf : lf.Replace("\n", newLine);
	}

	private static IReadOnlyList<IMergeStrategy> CreateDefaultStrategies()
	{
		return
		[
			new Strategies.MetadataMergeStrategy(),
			new Strategies.DescriptorMergeStrategy(),
			new Strategies.DataBindingMergeStrategy(),
			new Strategies.ResourceMergeStrategy(),
			new Strategies.ClientUnitJsMergeStrategy(),
			new Strategies.PropertiesJsonMergeStrategy(),
			new Strategies.ManualMergeRequiredStrategy()
		];
	}
}
