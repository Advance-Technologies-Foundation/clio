using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Mcp.E2E.Support.Configuration;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Helpers shared by the process-designer E2E fixtures that build processes on a live sandbox: removing what a test
/// built, and telling an outdated sandbox package apart from a regression.
/// </summary>
internal static class ProcessDesignerE2ESupport {

	/// <summary>
	/// The phrases of the refusals clio throws BEFORE a gated call reaches the package: convergence, when the
	/// sandbox's CrtProcessBuilder is older than the archive this clio bundles (<c>BundledPackageConvergence</c>),
	/// and the package requirement itself - missing, or below the declared floor (<c>RequiredPackageChecker</c>).
	/// The package never writes either phrase, so their presence means the call never reached it.
	/// </summary>
	private static readonly string[] PackageNotUsableMarkers = [
		"but the target environment has",
		"To use this command, you need to install the"
	];

	/// <summary>
	/// Removes a process a test built. Called from finally, so a failed or timed-out delete is reported, not
	/// thrown: an exception here would replace the assertion failure the test was already reporting. A remote
	/// delete-schema can outlast the CLI's default request timeout, so the request gets the same ten minutes the
	/// cleanup does.
	/// </summary>
	internal static async Task DeleteProcessAsync(string environmentName, string processName) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cleanup = new(TimeSpan.FromMinutes(10));
		try {
			ClioCliCommandResult deleted = await ClioCliCommandRunner.RunAsync(settings,
				["delete-schema", processName, "--remote", "-e", environmentName, "--timeout", "600000"],
				cancellationToken: cleanup.Token);
			if (deleted.ExitCode != 0) {
				await TestContext.Error.WriteLineAsync(
					$"Could not delete '{processName}': {deleted.StandardOutput} {deleted.StandardError}");
			}
		} catch (OperationCanceledException) {
			await TestContext.Error.WriteLineAsync($"Deleting '{processName}' timed out; it stays on the stand.");
		}
	}

	/// <summary>
	/// Ignores the test when clio refused the call because the sandbox's CrtProcessBuilder is missing, below the
	/// command's floor, or older than the archive this clio bundles. Keyed on those refusals' own wording, so any
	/// other failure - including the regression a test exists to catch - still goes red instead of being reported
	/// as an old environment. Call it BEFORE any cleanup is scheduled: nothing was built when it fires.
	/// </summary>
	internal static void IgnoreWhenProcessBuilderIsBehind(string toolText, string writtenAgainstVersion) {
		foreach (string marker in PackageNotUsableMarkers) {
			if (toolText.Contains(marker, StringComparison.Ordinal)) {
				Assert.Ignore(
					"clio refused the call before the sandbox's CrtProcessBuilder saw it - the package is missing or "
					+ "older than this clio requires. Install it with install-process-builder (this test was written "
					+ $"against {writtenAgainstVersion}); it is Ignored, NOT passing.");
			}
		}
	}

}
