namespace Creatio.ConflictResolver;

/// <summary>Contains the semantic merge outcome, optional content, and verification details.</summary>
public sealed class MergeResult
{
	/// <summary>Gets the stable semantic outcome.</summary>
	public MergeStatus Status { get; init; }

	/// <summary>Gets preview content when the outcome safely exposes it.</summary>
	public string? MergedContent { get; init; }

	/// <summary>Gets the semantic change and verification report.</summary>
	public MergeReport Report { get; init; } = new();

	/// <summary>Gets the stable resolver error code, when applicable.</summary>
	public string? ErrorCode { get; init; }

	/// <summary>Gets the human-readable resolver error, when applicable.</summary>
	public string? ErrorMessage { get; init; }
}
