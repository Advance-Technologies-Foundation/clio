using System.Diagnostics;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end regression coverage for the mcp-server host shutdown contract (ENG-91591). NOT part
/// of CI — run manually against a freshly built clio binary. These tests spawn the real
/// <c>clio mcp-server</c> process and assert that closing standard input (EOF) — the legitimate
/// stdio-transport termination signal an agent host sends on exit — tears the host down cleanly
/// with exit code 0 and no unhandled exception, rather than crashing with the
/// <see cref="ObjectDisposedException"/> that this fix removes.
/// </summary>
/// <remarks>
/// This is the user-visible guard the isolated <c>RequestShutdown</c> unit tests cannot provide:
/// reverting the handler detach-before-dispose change in <c>McpServerCommand.Execute</c> makes the
/// late <see cref="AppDomain.ProcessExit"/> handler cancel an already-disposed source, which
/// surfaces here as a non-zero exit code and an unhandled exception on stderr.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("mcp-server")]
[NonParallelizable]
public sealed class McpServerShutdownE2ETests {
	private const string McpServerVerb = "mcp-server";
	private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(60);

	[Test]
	[Description("Closing standard input (EOF) on the real clio mcp-server process shuts the host down cleanly with exit code 0 and no unhandled exception on stderr, reproducing the ENG-91591 contract end to end.")]
	[AllureTag(McpServerVerb)]
	[AllureName("mcp-server exits cleanly on stdin EOF")]
	public async Task McpServer_ShouldExitZeroWithoutUnhandledException_WhenStdinReachesEof() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		ClioProcessDescriptor descriptor = ClioExecutableResolver.Resolve(settings, McpServerVerb);
		ProcessStartInfo startInfo = new() {
			FileName = descriptor.Command,
			WorkingDirectory = descriptor.WorkingDirectory,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false
		};
		foreach (string argument in descriptor.Arguments) {
			startInfo.ArgumentList.Add(argument);
		}

		// Act
		using Process process = new() { StartInfo = startInfo };
		process.Start().Should().BeTrue(
			because: "the clio mcp-server process must launch before its shutdown behaviour can be exercised");
		try {
			Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
			Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
			// EOF on stdin is the exact termination signal an agent host (Claude Code / Copilot) sends
			// when it closes the transport on exit; it must drive a clean shutdown, not a crash.
			process.StandardInput.Close();
			bool exitedWithinTimeout = await WaitForCleanExitAsync(process);
			string standardError = await standardErrorTask;
			_ = await standardOutputTask;

			// Assert
			exitedWithinTimeout.Should().BeTrue(
				because: "EOF on stdin must make the mcp-server host loop return so the process exits promptly");
			process.ExitCode.Should().Be(0,
				because: "a clean EOF teardown is a successful shutdown, not a failure");
			standardError.Should().NotContain(nameof(ObjectDisposedException),
				because: "the late ProcessExit handler must no longer cancel an already-disposed CancellationTokenSource");
			standardError.Should().NotContain("Unhandled exception",
				because: "an otherwise-clean EOF shutdown must not surface any unhandled exception on stderr");
		} finally {
			// Guarantee the spawned child is gone even if an assertion above throws first, so a
			// failed run never leaks a clio mcp-server process onto the runner.
			TryKill(process);
		}
	}

	private static async Task<bool> WaitForCleanExitAsync(Process process) {
		using CancellationTokenSource timeoutSource = new(ShutdownTimeout);
		try {
			await process.WaitForExitAsync(timeoutSource.Token);
			return true;
		} catch (OperationCanceledException) {
			// The host did not exit on EOF within the timeout; kill it so the test run does not hang
			// and let the caller report the timeout as a failed shutdown contract.
			TryKill(process);
			return false;
		}
	}

	private static void TryKill(Process process) {
		try {
			if (!process.HasExited) {
				process.Kill(entireProcessTree: true);
			}
		} catch (InvalidOperationException) {
			// The process already exited between the HasExited check and Kill — nothing to clean up.
		}
	}
}
