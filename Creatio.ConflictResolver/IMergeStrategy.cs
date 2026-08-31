namespace Creatio.ConflictResolver;

/// <summary>Defines one semantic merge strategy for a Creatio artifact type.</summary>
internal interface IMergeStrategy
{
	/// <summary>Determines whether this strategy handles the supplied artifact type.</summary>
	/// <param name="fileType">Detected Creatio artifact type.</param>
	/// <returns><c>true</c> when this strategy can merge the artifact.</returns>
	bool CanHandle(ConflictFileType fileType);

	/// <summary>Performs an in-memory three-way semantic merge.</summary>
	/// <param name="request">Base, local, remote, and optional artifact context.</param>
	/// <returns>The semantic result and verification report.</returns>
	MergeResult Merge(MergeRequest request);
}
