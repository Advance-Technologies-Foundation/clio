namespace Creatio.ConflictResolver;

/// <summary>Controls whether logical conflicts are exposed as selectable marker blocks.</summary>
public enum MergeMode
{
	/// <summary>Returns the resolver's semantic result without marker formatting.</summary>
	Default = 0,
	/// <summary>Formats semantic conflicts as complete local and remote marker alternatives.</summary>
	Automerge = 1
}
