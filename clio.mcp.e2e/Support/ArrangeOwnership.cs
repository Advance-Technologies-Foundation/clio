using System.Threading.Tasks;

namespace Clio.Mcp.E2E.Support;

/// <summary>
/// Keeps a fixture's cleanup ownership ahead of its side effects.
/// </summary>
/// <remarks>
/// An arrange step that returns its disposable context only after the last remote call leaves a window in
/// which the caller owns nothing: if any step after the first side effect throws or is cancelled, the
/// <c>await using</c> value is never handed over, and neither the remote package nor the temporary local
/// workspace is ever cleaned up. Creating the context before the first side effect and running the rest of
/// arrange through this helper closes that window - the context is disposed on the way out and the original
/// failure still propagates unchanged.
/// </remarks>
internal static class ArrangeOwnership {
	/// <summary>
	/// Runs <paramref name="remainingSteps"/> and returns <paramref name="resource"/>, disposing it when a
	/// step fails.
	/// </summary>
	internal static async Task<TResource> CompleteOrDisposeAsync<TResource>(
		TResource resource,
		Func<Task> remainingSteps)
		where TResource : IAsyncDisposable {
		try {
			await remainingSteps();
			return resource;
		}
		catch {
			//DisposeAsync is defensive by contract here (it reports rather than throws), so compensation
			//cannot replace the arrange failure with a teardown one.
			await resource.DisposeAsync();
			throw;
		}
	}
}
