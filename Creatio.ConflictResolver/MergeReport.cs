namespace Creatio.ConflictResolver;

/// <summary>Summarizes semantic changes, conflicts, winner policy, and verification state.</summary>
public sealed class MergeReport
{
	/// <summary>Gets the semantic strategy used to produce the result.</summary>
	public string ResolutionType { get; init; } = string.Empty;

	/// <summary>Gets semantic keys added only by the local branch.</summary>
	public IReadOnlyList<string> LocalAdditions { get; init; } = Array.Empty<string>();

	/// <summary>Gets semantic keys added only by the remote branch.</summary>
	public IReadOnlyList<string> RemoteAdditions { get; init; } = Array.Empty<string>();

	/// <summary>Gets semantic keys deleted only by the local branch.</summary>
	public IReadOnlyList<string> LocalDeletions { get; init; } = Array.Empty<string>();

	/// <summary>Gets semantic keys deleted only by the remote branch.</summary>
	public IReadOnlyList<string> RemoteDeletions { get; init; } = Array.Empty<string>();

	/// <summary>Gets semantic keys that require an explicit human choice.</summary>
	public IReadOnlyList<string> TrueConflicts { get; init; } = Array.Empty<string>();

	/// <summary>Gets the policy used when a strategy selected one branch automatically.</summary>
	public string WinnerPolicy { get; init; } = "LOCAL";

	/// <summary>Gets whether the strategy verified the structural result.</summary>
	public bool VerificationPassed { get; init; }
}
