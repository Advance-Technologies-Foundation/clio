namespace Creatio.ConflictResolver;

/// <summary>Identifies the semantic result of a resolver invocation.</summary>
public enum MergeStatus
{
	/// <summary>The semantic merge completed and verified without marker conflicts.</summary>
	Resolved = 0,
	/// <summary>The artifact type has no semantic merge strategy.</summary>
	UnsupportedType = 1,
	/// <summary>The input could not be parsed or merged safely.</summary>
	InvalidInput = 2,
	/// <summary>The resolver detected a conflict it could not safely format.</summary>
	UnresolvedConflict = 3,
	/// <summary>The artifact requires whole-file manual resolution.</summary>
	ManualMergeRequired = 4,
	/// <summary>The merge preserves complete alternatives in conflict markers for a human choice.</summary>
	AutoResolvedWithConflicts = 5
}
