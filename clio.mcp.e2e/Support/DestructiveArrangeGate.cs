using System.Threading.Tasks;
using Clio.Mcp.E2E.Support.Configuration;

namespace Clio.Mcp.E2E.Support;

/// <summary>
/// The destructive-opt-in decision, normally <see cref="DestructiveStandAuthorization.IsAuthorized"/>.
/// </summary>
/// <remarks>
/// A named delegate rather than <c>Func&lt;bool, bool, bool&gt;</c>: two positional booleans swap silently at
/// the call site, and swapping these two turns a denial into a permission to mutate the stand.
/// </remarks>
/// <param name="touchesStand">Whether the arrange step reaches a Creatio environment at all.</param>
/// <param name="allowDestructiveMcpTests">The <c>McpE2E:AllowDestructiveMcpTests</c> setting.</param>
internal delegate bool DestructiveAuthorizationDecision(bool touchesStand, bool allowDestructiveMcpTests);

/// <summary>
/// The steps a stand-touching arrange step runs, in the order the destructive opt-in has to gate them.
/// </summary>
/// <param name="IsAuthorized">The destructive-opt-in decision.</param>
/// <param name="Deny">What to do when the opt-in is off, normally <c>Assert.Ignore</c>.</param>
/// <param name="ResolveClioProcessPath">Resolves the clio executable the arrange step will spawn.</param>
/// <param name="ResolveEnvironmentAsync">
/// Resolves the sandbox environment; it pings the stand, so it must never run before the gate.
/// </param>
internal sealed record DestructiveArrangeSteps(
	DestructiveAuthorizationDecision IsAuthorized,
	Action<string> Deny,
	Func<string> ResolveClioProcessPath,
	Func<McpE2ESettings, Task<string>> ResolveEnvironmentAsync);

/// <summary>
/// Runs a stand-touching arrange step behind the destructive opt-in.
/// </summary>
/// <remarks>
/// The ordering this helper enforces is the whole point: <c>[Explicit]</c> and the CI guard only stop
/// automatic selection, so a developer selecting such a fixture by hand reaches the arrange code with the
/// opt-in off. Everything past the gate touches the configured stand - resolving the environment pings it,
/// and the remaining steps push a package and publish configuration. Keeping the gate, the resolvers and
/// the remaining steps inside one helper makes the order a property of code an environment-free test can
/// execute, instead of the order in which the calls happen to appear in the fixture source.
/// </remarks>
internal static class DestructiveArrangeGate {
	/// <summary>
	/// Checks the destructive opt-in and, only when it passes, resolves the clio executable and the
	/// environment and runs <paramref name="remainingSteps"/>.
	/// </summary>
	/// <param name="settings">The e2e settings; its <c>ClioProcessPath</c> is filled in here.</param>
	/// <param name="touchesStand">Whether this arrange step reaches a Creatio environment at all.</param>
	/// <param name="steps">The injectable seam; production passes the real implementations.</param>
	/// <param name="remainingSteps">
	/// The rest of arrange, receiving the resolved environment name (<c>null</c> when the step needs none).
	/// </param>
	internal static async Task<TContext> RunAsync<TContext>(
		McpE2ESettings settings,
		bool touchesStand,
		DestructiveArrangeSteps steps,
		Func<string?, Task<TContext>> remainingSteps) {
		if (!steps.IsAuthorized(touchesStand, settings.AllowDestructiveMcpTests)) {
			steps.Deny(DestructiveStandAuthorization.MissingOptInMessage);
			//Deny normally ends the test by throwing (Assert.Ignore does). Throwing here as well keeps the
			//guarantee independent of that: a deny hook that returns must still not reach the stand.
			throw new InvalidOperationException(DestructiveStandAuthorization.MissingOptInMessage);
		}

		settings.ClioProcessPath = steps.ResolveClioProcessPath();
		string? environmentName = touchesStand
			? await steps.ResolveEnvironmentAsync(settings)
			: null;
		return await remainingSteps(environmentName);
	}
}
