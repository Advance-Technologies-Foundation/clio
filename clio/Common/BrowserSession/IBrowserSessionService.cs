using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Orchestrates browser-session retrieval: returns a valid cached storageState when one exists, or
/// authenticates (via <see cref="ICreatioAuthClient"/>) and caches a fresh one. Hides caching,
/// validation, and authentication behind a single call.
/// </summary>
public interface IBrowserSessionService {
	/// <summary>
	/// Returns the path to a valid Playwright storageState for <paramref name="env"/>, reusing a
	/// cached session when it is still valid and re-authenticating otherwise.
	/// </summary>
	/// <param name="env">Target environment.</param>
	/// <param name="overrideOutputPath">Optional explicit destination (CLI <c>--output-path</c>);
	/// when set, the fresh session is written there and that path is returned.</param>
	/// <param name="forceRefresh">When <see langword="true"/>, bypasses the cache and always re-authenticates.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>The absolute path to the storageState file.</returns>
	Task<string> GetSessionPathAsync(EnvironmentSettings env, string overrideOutputPath = null,
		bool forceRefresh = false, CancellationToken ct = default);

	/// <summary>Deletes any cached session for <paramref name="env"/> (idempotent).</summary>
	/// <param name="env">Target environment.</param>
	/// <param name="ct">Cancellation token.</param>
	Task ClearSessionAsync(EnvironmentSettings env, CancellationToken ct = default);
}
