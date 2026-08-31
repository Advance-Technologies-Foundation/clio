using System.Text.Json;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver.Strategies;

internal sealed class MetadataMergeStrategy : IMergeStrategy
{
	private const string DefaultObjectArrayKeyPropertyName = "UId";
	private readonly IReadOnlyList<IMetadataMergeStrategy> _strategies;

	public MetadataMergeStrategy()
		: this(DefaultObjectArrayKeyPropertyName)
	{
	}

	public MetadataMergeStrategy(string objectArrayKeyPropertyName)
		: this(
		[
			new TimelineEntityMetadataMergeStrategy(),
			new JsonMetadataMergeStrategy(objectArrayKeyPropertyName),
			new FlatDiffMetadataMergeStrategy()
		])
	{
	}

	internal MetadataMergeStrategy(IEnumerable<IMetadataMergeStrategy> strategies)
	{
		_strategies = strategies?.ToArray() ?? throw new ArgumentNullException(nameof(strategies));
	}

	public bool CanHandle(ConflictFileType fileType) => fileType == ConflictFileType.MetadataJson;

	public MergeResult Merge(MergeRequest request)
	{
		var strategy = _strategies.FirstOrDefault(s => s.CanHandle(request));
		if (strategy is null)
		{
			return MergeResultFactory.InvalidInput(
				"UnsupportedMetadataFormat",
				"Metadata format is not supported. Expected JSON or flat metadata diff format.");
		}

		return strategy.Merge(request);
	}
}
