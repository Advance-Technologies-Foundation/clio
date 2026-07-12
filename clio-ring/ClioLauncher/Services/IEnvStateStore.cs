using ClioLauncher.Models;

namespace ClioLauncher.Services;

/// <summary>Loads and persists the launcher's environment state (active + pinned + MRU).</summary>
public interface IEnvStateStore {
	/// <summary>Loads persisted state, or a fresh empty state when none exists / on error.</summary>
	EnvState Load();

	/// <summary>Persists the state (best-effort; never throws).</summary>
	void Save(EnvState state);
}
