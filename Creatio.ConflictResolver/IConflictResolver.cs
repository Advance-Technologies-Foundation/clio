namespace Creatio.ConflictResolver;

/// <summary>
/// Resolves semantic conflicts between three versions of a Creatio package artifact.
/// </summary>
public interface IConflictResolver
{
	/// <summary>Merges the supplied base, local, and remote content.</summary>
	/// <param name="request">The artifact type, contents, and merge mode.</param>
	/// <returns>The semantic merge outcome and verification report.</returns>
	MergeResult Resolve(MergeRequest request);
}
