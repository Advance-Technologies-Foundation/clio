using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// Shared arrange/act/assert plumbing for the DB-first data-binding MCP e2e fixtures.
/// </summary>
/// <remarks>
/// The additive scenarios and the schema-publishing Color round-trip live in two separate
/// fixtures, because the latter is a destructive lifecycle that must never run in an automatic
/// lane (see <c>clio.mcp.e2e/AGENTS.md</c>). They still share one workspace/package arrange
/// step and one set of MCP call helpers, which is what this base class holds - splitting the
/// fixtures must not duplicate them.
/// </remarks>
public abstract class DataBindingDbFixtureBase : McpContractFixtureBase {
	protected const string CreateDbToolName = CreateDataBindingDbTool.CreateDataBindingDbToolName;
	protected const string UpsertRowDbToolName = UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName;
	protected const string RemoveRowDbToolName = RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName;
	protected const string ReadDbToolName = ReadDataBindingDbTool.ReadDataBindingDbToolName;
	protected const string ODataCreateToolName = ODataCreateTool.ToolName;
	protected const string CreateEntitySchemaToolName = CreateEntitySchemaTool.CreateEntitySchemaToolName;

	private protected static void AssertOutputDoesNotContain(CommandExecutionActResult actResult, string unexpected,
		string because) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "command execution should emit human-readable diagnostics");
		actResult.Execution.Output!
			.Select(message => message.Value?.ToString() ?? string.Empty)
			.Should().NotContain(text => text.Contains(unexpected), because: because);
	}

	private protected static int CountOutputOccurrences(CommandExecutionActResult actResult, string needle) =>
		(actResult.Execution.Output ?? [])
			.Select(message => message.Value?.ToString() ?? string.Empty)
			.Sum(text => CountOccurrences(text, needle));

	private protected static int CountOccurrences(string haystack, string needle) {
		int count = 0;
		for (int index = haystack.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase);
				index >= 0;
				index = haystack.IndexOf(needle, index + needle.Length, System.StringComparison.OrdinalIgnoreCase)) {
			count++;
		}
		return count;
	}

	private protected static void AssertOutputContains(CommandExecutionActResult actResult, string expected, string because) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "command execution should emit human-readable diagnostics");
		actResult.Execution.Output!
			.Select(message => message.Value?.ToString() ?? string.Empty)
			.Should().Contain(text => text.Contains(expected), because: because);
	}

	private protected async Task<DataBindingDbArrangeContext> ArrangeAsync(bool requireEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = requireEnvironment
			? await ResolveReachableEnvironmentAsync(settings)
			: null;

		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-db-binding-e2e-{System.Guid.NewGuid():N}");
		Directory.CreateDirectory(rootDirectory);
		string workspaceName = $"workspace-{System.Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		string packageName = $"Pkg{System.Guid.NewGuid():N}".Substring(0, 18);
		CancellationTokenSource cancellationTokenSource = new(System.TimeSpan.FromMinutes(8));

		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["create-workspace", workspaceName, "--empty", "--directory", rootDirectory],
			cancellationToken: cancellationTokenSource.Token);
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["add-package", packageName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationTokenSource.Token);
		if (requireEnvironment && !string.IsNullOrWhiteSpace(environmentName)) {
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(
				settings,
				["push-workspace", "-e", environmentName],
				workingDirectory: workspacePath,
				cancellationToken: cancellationTokenSource.Token);
			await ClioCliCommandRunner.WaitForEnvironmentRecoveryAsync(
				settings,
				environmentName,
				cancellationTokenSource.Token);
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(
				settings,
				["pkg-hotfix", packageName, "true", "-e", environmentName],
				workingDirectory: workspacePath,
				cancellationToken: cancellationTokenSource.Token);
			await ClioCliCommandRunner.WaitForEnvironmentRecoveryAsync(
				settings,
				environmentName,
				cancellationTokenSource.Token);
		}

		McpServerSession session = Session;
		return new DataBindingDbArrangeContext(
			settings,
			rootDirectory,
			workspacePath,
			packageName,
			environmentName,
			session,
			cancellationTokenSource);
	}

	/// <summary>
	/// Returns the sandbox environment this fixture owns, or skips the test.
	/// </summary>
	/// <remarks>
	/// The environment has to be configured explicitly. Falling back to whatever "d2" happened to
	/// resolve to meant the fixture created packages, schemas and rows on a stand nobody had declared
	/// as disposable - and the teardown then deleted a package on it. A run without
	/// McpE2E__Sandbox__EnvironmentName is skipped instead of quietly picking a target.
	/// </remarks>
	private protected static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(configuredEnvironmentName)) {
			Assert.Ignore(
				"DB-first data-binding MCP E2E writes to a Creatio stand, so it needs an explicitly "
				+ "configured sandbox: set McpE2E__Sandbox__EnvironmentName to an environment this suite "
				+ "may create and delete packages on.");
			return string.Empty;
		}
		if (await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		Assert.Ignore(
			$"DB-first data-binding MCP E2E requires a reachable environment, and the configured sandbox "
			+ $"environment '{configuredEnvironmentName}' did not answer ping-app.");
		return string.Empty;
	}

	private protected static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private protected static async Task<CommandExecutionActResult> ActCommandAsync(
		DataBindingDbArrangeContext arrangeContext,
		string toolName,
		Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: "the requested DB-first data-binding MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call");

		ModelContextProtocol.Protocol.CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new CommandExecutionActResult(callResult, execution);
	}

	private protected static void AssertToolCallSucceeded(CommandExecutionActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"the MCP tool call should not return an error envelope. Content: {DescribeCallResult(actResult.CallResult)}");
	}

	private protected static void AssertCommandExitCode(CommandExecutionActResult actResult, int expectedExitCode, string because) {
		actResult.Execution.ExitCode.Should().Be(expectedExitCode,
			because: $"{because}. Command output: {DescribeExecution(actResult.Execution)}");
	}

	/// <summary>
	/// Renders the command's own log messages into the assertion message. Without them an
	/// unexpected exit code reports only the number, which says nothing about whether the
	/// command rejected the request or the shared sandbox was mid-rebuild.
	/// </summary>
	private protected static string DescribeExecution(CommandExecutionEnvelope execution) {
		if (execution.Output is null || execution.Output.Count == 0) {
			return "<no execution log messages>";
		}

		return string.Join(" | ", execution.Output.Select(m => $"[{m.MessageType}] {m.Value}"));
	}

	private protected static string DescribeCallResult(ModelContextProtocol.Protocol.CallToolResult callResult) {
		if (callResult.Content is null || callResult.Content.Count == 0) {
			return "<no content>";
		}

		return string.Join(" | ", callResult.Content.Select(c => c?.ToString() ?? "<null>"));
	}

	private protected sealed record DataBindingDbArrangeContext(
		McpE2ESettings Settings,
		string RootDirectory,
		string WorkspacePath,
		string PackageName,
		string? EnvironmentName,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : System.IAsyncDisposable {

		/// <summary>
		/// Removes what this fixture created: the package it pushed to the sandbox, and - unless the
		/// test failed - the local workspace.
		/// </summary>
		/// <remarks>
		/// The remote package is deleted on every outcome, because leaving it behind accumulates
		/// fixture-owned packages, schemas and rows on the shared stand run after run. The local
		/// workspace is kept when the test failed: its binding descriptor and data files are the
		/// evidence, and deleting them leaves only the assertion text to diagnose from. The path is
		/// written to the test output so it can be found.
		/// </remarks>
		public async System.Threading.Tasks.ValueTask DisposeAsync() {
			await DeleteRemotePackageAsync();
			CancellationTokenSource.Dispose();
			if (!Directory.Exists(RootDirectory)) {
				return;
			}
			if (TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed) {
				TestContext.Out.WriteLine(
					$"Test failed; keeping the workspace for diagnosis: {RootDirectory}");
				return;
			}
			Directory.Delete(RootDirectory, recursive: true);
		}

		private async Task DeleteRemotePackageAsync() {
			if (string.IsNullOrWhiteSpace(EnvironmentName)) {
				return;
			}
			//Teardown must never turn a passing test red or mask the real failure of a failing one, so
			//the exit code is reported rather than asserted. A package that was never pushed - the
			//arrange step failed before push-workspace - simply makes this a no-op on the stand.
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				Settings,
				["delete-pkg-remote", PackageName, "-e", EnvironmentName]);
			if (result.ExitCode != 0) {
				TestContext.Out.WriteLine(
					$"Could not delete the fixture package '{PackageName}' from '{EnvironmentName}' "
					+ $"(exit {result.ExitCode}); it may need removing by hand.");
			}
		}
	}

	private protected sealed record CommandExecutionActResult(
		ModelContextProtocol.Protocol.CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
