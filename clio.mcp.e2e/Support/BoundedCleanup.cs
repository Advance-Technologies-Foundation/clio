using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Mcp.E2E.Support;

/// <summary>
/// Runs a fixture teardown command under its own bounded cancellation token and never throws.
/// </summary>
/// <remarks>
/// Teardown has two hard requirements that the arrange token cannot satisfy. It must still run when the
/// arrange token is already cancelled or expired - that is exactly the case a timed-out test leaves behind -
/// and it must be bounded, because <c>ClioCliCommandRunner.RunAsync</c> passes its token straight to
/// <c>Process.WaitForExitAsync</c>: a non-cancelable token turns a stalled stand or a wedged child process
/// into an E2E worker that hangs forever and reports nothing. A fresh short-lived token gives the runner
/// something to fire on, and the runner then kills the process tree. Failures are returned as a diagnostic
/// message instead of thrown, so teardown can never turn a passing test red or mask a failing one.
/// </remarks>
internal static class BoundedCleanup {
	/// <summary>
	/// Executes <paramref name="command"/> with a token cancelled after <paramref name="timeout"/>.
	/// </summary>
	/// <returns><c>null</c> when the command exited with code 0, otherwise a diagnostic message.</returns>
	internal static async Task<string?> RunAsync(
		Func<CancellationToken, Task<int>> command,
		TimeSpan timeout,
		string description) {
		using CancellationTokenSource cleanupCancellation = new(timeout);
		try {
			int exitCode = await command(cleanupCancellation.Token);
			return exitCode == 0
				? null
				: $"{description} failed with exit {exitCode}; it may need cleaning up by hand.";
		}
		catch (OperationCanceledException) {
			return $"{description} did not finish within {timeout.TotalSeconds:0}s and was cancelled; "
				+ "it may need cleaning up by hand.";
		}
		catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception) {
			return $"{description} failed: {exception.Message}; it may need cleaning up by hand.";
		}
	}
}
