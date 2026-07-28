using System.Collections.Generic;

namespace ClioRing.Models;

/// <summary>
/// Persisted launcher state (names only — never credentials): the active environment, the pinned
/// favourites, and the most-recently-used list. Stored next to the user profile as JSON.
/// </summary>
public sealed class EnvState {
	/// <summary>Currently selected environment name (the command target).</summary>
	public string? Selected { get; set; }

	/// <summary>Pinned favourite environment names, in pin order.</summary>
	public List<string> Pinned { get; set; } = new();

	/// <summary>Most-recently-used environment names, most-recent first.</summary>
	public List<string> Recents { get; set; } = new();
}
