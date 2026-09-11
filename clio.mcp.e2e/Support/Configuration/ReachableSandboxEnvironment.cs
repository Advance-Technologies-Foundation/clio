namespace Clio.Mcp.E2E.Support.Configuration;

/// <summary>
/// Resolves — once per run — which registered environment the sandbox fixtures should talk to.
/// </summary>
/// <remarks>
/// <para>
/// The probe itself is a <c>ping-app</c>: an out-of-process clio start plus a real authenticated round
/// trip to Creatio. Fixtures used to run it inside their own arrange, so a suite of N sandbox tests
/// paid for N identical probes against the one stand the build deploys. The answer cannot change
/// during a run — the stand is created by the build's own deploy step and removed after it — so only
/// the first probe carries information.
/// </para>
/// <para>
/// Only a SUCCESSFUL resolution is cached. A transient refusal during the stand's warm-up must not be
/// frozen for the whole run — that would silently turn every sandbox fixture into a skip, which reads
/// as a green build with no coverage. A fixture that finds no reachable environment still calls
/// <see cref="Assert.Ignore(string)"/> itself, so the fixture-specific skip message is preserved and a
/// skipped fixture is still reported as skipped.
/// </para>
/// </remarks>
internal static class ReachableSandboxEnvironment {

	/// <summary>
	/// Environment probed when the configured sandbox environment is absent or unreachable. Kept for
	/// developer machines, where the shared dev stand is registered under this name.
	/// </summary>
	public const string FallbackEnvironmentName = "d2";

	private static readonly SemaphoreSlim ResolutionGate = new(1, 1);
	private static string? _resolvedEnvironmentName;

	/// <summary>
	/// Returns the name of the reachable environment for this run, or <see langword="null"/> when
	/// neither the configured sandbox environment nor the fallback answered.
	/// </summary>
	/// <param name="settings">Settings carrying the configured sandbox environment name.</param>
	/// <returns>The reachable environment name, or <see langword="null"/>.</returns>
	public static async Task<string?> ResolveAsync(McpE2ESettings settings) {
		if (_resolvedEnvironmentName is not null) {
			return _resolvedEnvironmentName;
		}
		await ResolutionGate.WaitAsync();
		try {
			_resolvedEnvironmentName ??= await ProbeAsync(settings);
			return _resolvedEnvironmentName;
		} finally {
			ResolutionGate.Release();
		}
	}

	/// <summary>
	/// Returns the reachable environment for this run, or ignores the calling test with
	/// <paramref name="ignoreMessage"/> when no environment answered.
	/// </summary>
	/// <param name="settings">Settings carrying the configured sandbox environment name.</param>
	/// <param name="ignoreMessage">Fixture-specific message explaining what the test needed.</param>
	/// <returns>The reachable environment name.</returns>
	public static async Task<string> ResolveOrIgnoreAsync(McpE2ESettings settings, string ignoreMessage) {
		string? environmentName = await ResolveAsync(settings);
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore(ignoreMessage);
		}
		return environmentName!;
	}

	private static async Task<string?> ProbeAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName)
				&& await CanReachAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}
		return await CanReachAsync(settings, FallbackEnvironmentName) ? FallbackEnvironmentName : null;
	}

	private static async Task<bool> CanReachAsync(McpE2ESettings settings, string environmentName) =>
		await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName);
}
