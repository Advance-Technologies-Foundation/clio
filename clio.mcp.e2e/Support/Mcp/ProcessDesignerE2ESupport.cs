using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Helpers shared by the process-designer E2E fixtures that build processes on a live sandbox: removing what a test
/// built, telling an outdated sandbox package apart from a regression, and reading and asserting a tool result.
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

	/// <summary>The line only a genuinely successful build logs.</summary>
	internal const string CreatedLine = "created (UId:";

	#region Methods: Result assertions

	/// <summary>A create that must have built: exit code 0 and the created-schema line.</summary>
	internal static void ShouldHaveBuilt(CallToolResult result, string because) {
		ShouldHaveLanded(result, because);
		LogText(result).Should().Contain(CreatedLine,
			because: "only a genuinely successful build logs the created-schema line; the server said: {0}",
			LogText(result));
	}

	/// <summary>
	/// A write that must have LANDED - exit code 0, not merely no transport error - with an Info line, carrying the
	/// server's own log into the failure message, because the reason is the whole diagnosis on a live stand.
	/// </summary>
	internal static void ShouldHaveLanded(CallToolResult result, string because) {
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(result);
		envelope.ExitCode.Should().Be(0, Escape(because) + ". The server said: {0}", LogText(result));
		(envelope.Output ?? []).Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: "a successful write reports at least one Info line");
	}

	/// <summary>A write that must have been refused: a non-zero exit code and an Error line carrying the reason.</summary>
	internal static void ShouldBeRefused(CallToolResult result, string because) {
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(result);
		envelope.ExitCode.Should().NotBe(0, Escape(because) + ". The server said: {0}", LogText(result));
		(envelope.Output ?? []).Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: "a refusal is reported as an Error line, which is where the caller reads the reason");
	}

	/// <summary>Every execution-log message of a tool result, decoded, one per line.</summary>
	internal static string LogText(CallToolResult result) =>
		string.Join("\n", (McpCommandExecutionParser.Extract(result).Output ?? [])
			.Select(message => message.Value ?? string.Empty));

	/// <summary>The Warning lines of a tool result, decoded - where the server's binder notices arrive.</summary>
	internal static string WarningText(CallToolResult result) =>
		string.Join("\n", (McpCommandExecutionParser.Extract(result).Output ?? [])
			.Where(message => message.MessageType == LogDecoratorType.Warning)
			.Select(message => message.Value ?? string.Empty));

	/// <summary>The raw text of a tool result, for the package-version refusal markers.</summary>
	internal static string ToolText(CallToolResult result) =>
		string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

	/// <summary>Escapes a reason for use as a format string.</summary>
	internal static string Escape(string because) =>
		because.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);

	#endregion

	/// <summary>The string value of a member, or <c>null</c> when it is absent, null or not a string.</summary>
	internal static string? Text(JsonObject node, string key) =>
		node[key] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

}
