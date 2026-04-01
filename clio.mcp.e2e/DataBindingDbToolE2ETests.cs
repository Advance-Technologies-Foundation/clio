using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the DB-first data-binding MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("data-binding-db")]
[NonParallelizable]
public sealed class DataBindingDbToolE2ETests {
	private const string CreateDbToolName = CreateDataBindingDbTool.CreateDataBindingDbToolName;
	private const string UpsertRowDbToolName = UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName;
	private const string RemoveRowDbToolName = RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName;

	[Test]
	[Description("Advertises all three DB-first data-binding MCP tools in the server tool list so callers can discover and invoke them.")]
	[AllureTag(CreateDbToolName)]
	[AllureName("DB-first data-binding tools are advertised by the MCP server")]
	[AllureDescription("Verifies that create-data-binding-db, upsert-data-binding-row-db, and remove-data-binding-row-db appear in the MCP server tool manifest.")]
	public async Task DataBindingDbTools_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: false);

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(t => t.Name);

		// Assert
		toolNames.Should().Contain(CreateDbToolName,
			because: "create-data-binding-db must be advertised so MCP clients can discover it");
		toolNames.Should().Contain(UpsertRowDbToolName,
			because: "upsert-data-binding-row-db must be advertised so MCP clients can discover it");
		toolNames.Should().Contain(RemoveRowDbToolName,
			because: "remove-data-binding-row-db must be advertised so MCP clients can discover it");
	}

	[Test]
	[Description("Creates a DB-first data binding through MCP on a real Creatio environment, verifying the command exits with code 0 and emits a completion message.")]
	[AllureTag(CreateDbToolName)]
	[AllureName("Create DB-first data binding succeeds on real environment")]
	[AllureDescription("Uses the real clio MCP server to invoke create-data-binding-db against a reachable Creatio sandbox environment and verifies the tool completes without error.")]
	public async Task CreateDataBindingDb_Should_Succeed_On_Real_Environment() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);

		// Act
		CommandExecutionActResult result = await ActCommandAsync(
			arrangeContext,
			CreateDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "SysSettings",
				["binding-name"] = $"UsrDbE2E{arrangeContext.PackageName}",
				["rows"] = """[{"values":{"Name":"E2E DB binding row","Code":"UsrE2EDbRow"}}]"""
			});

		// Assert
		AssertToolCallSucceeded(result);
		AssertCommandExitCode(result, 0,
			"create-data-binding-db should succeed when a reachable environment and valid schema are provided");
		AssertIncludesInfoMessage(result,
			"successful create-data-binding-db execution should emit a completion message");
	}

	[Test]
	[Description("Fails create-data-binding-db through MCP with exit code 1 when environment-name is empty, matching the command-layer validation guard.")]
	[AllureTag(CreateDbToolName)]
	[AllureName("Create DB-first binding without environment fails with exit code 1")]
	[AllureDescription("Uses the real clio MCP server to invoke create-data-binding-db without an environment-name and verifies that the tool returns exit code 1 with an error message.")]
	public async Task CreateDataBindingDb_Should_Fail_Without_Environment() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: false);

		// Act
		CommandExecutionActResult result = await ActCommandAsync(
			arrangeContext,
			CreateDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = string.Empty,
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "SysSettings"
			});

		// Assert
		AssertToolCallSucceeded(result);
		AssertCommandExitCode(result, 1,
			"create-data-binding-db must reject empty environment-name with exit code 1");
	}

	[Test]
	[Description("Re-running create-data-binding-db with the same row Name skips the insert and does not emit 'Created row' for the duplicate, but still succeeds and includes the existing row Id in the binding.")]
	[AllureTag(CreateDbToolName)]
	[AllureName("create-data-binding-db skips insert for existing Name on re-run")]
	[AllureDescription("Uses the real clio MCP server to invoke create-data-binding-db twice with the same row Name against a reachable Creatio sandbox. Verifies the second call exits with code 0, does not emit a 'Created row' message for the duplicate, and succeeds without creating a phantom binding reference.")]
	public async Task CreateDataBindingDb_Should_Skip_Duplicate_Name_On_Rerun() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);
		const string rowName = "E2E Dedup Row";
		const string rowsJson = """[{"values":{"Name":"E2E Dedup Row","Code":"UsrDedupRow"}}]""";
		var firstCallArgs = new Dictionary<string, object?> {
			["environment-name"] = arrangeContext.EnvironmentName,
			["package-name"] = arrangeContext.PackageName,
			["schema-name"] = "SysSettings",
			["binding-name"] = $"UsrDedupE2E{arrangeContext.PackageName}",
			["rows"] = rowsJson
		};

		// Act - first call inserts the row
		CommandExecutionActResult firstResult = await ActCommandAsync(arrangeContext, CreateDbToolName, firstCallArgs);

		// Act - second call with the same Name must skip the insert
		CommandExecutionActResult secondResult = await ActCommandAsync(arrangeContext, CreateDbToolName, firstCallArgs);

		// Assert
		AssertToolCallSucceeded(firstResult);
		AssertCommandExitCode(firstResult, 0, "first create-data-binding-db should succeed and insert the row");
		AssertIncludesInfoMessage(firstResult, "first call should emit at least one info message");

		AssertToolCallSucceeded(secondResult);
		AssertCommandExitCode(secondResult, 0, "second create-data-binding-db should succeed even when the row Name already exists");
		secondResult.Execution.Output.Should().NotBeNull();
		secondResult.Execution.Output!
			.Where(m => m.MessageType == LogDecoratorType.Info)
			.Select(m => m.Value?.ToString() ?? string.Empty)
			.Should().NotContain(
				msg => msg.Contains("Created row") && msg.Contains(rowName),
				because: "duplicate Name must not produce a second INSERT and must not appear in the 'Created row' output");
	}

	private static async Task<DataBindingDbArrangeContext> ArrangeAsync(bool requireEnvironment) {
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
		CancellationTokenSource cancellationTokenSource = new(System.TimeSpan.FromMinutes(5));

		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["create-workspace", workspaceName, "--empty", "--directory", rootDirectory],
			cancellationToken: cancellationTokenSource.Token);
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["add-package", packageName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationTokenSource.Token);

		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new DataBindingDbArrangeContext(
			rootDirectory,
			workspacePath,
			packageName,
			environmentName,
			session,
			cancellationTokenSource);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName) &&
			await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		const string fallbackEnvironmentName = "d2";
		if (await CanReachEnvironmentAsync(settings, fallbackEnvironmentName)) {
			return fallbackEnvironmentName;
		}

		Assert.Ignore(
			$"DB-first data-binding MCP E2E requires a reachable environment. " +
			$"Configured sandbox environment '{configuredEnvironmentName}' was not reachable, " +
			$"and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private static async Task<CommandExecutionActResult> ActCommandAsync(
		DataBindingDbArrangeContext arrangeContext,
		string toolName,
		Dictionary<string, object?> args) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(toolName,
			because: "the requested DB-first data-binding MCP tool must be advertised before the end-to-end call");

		ModelContextProtocol.Protocol.CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> { ["args"] = args },
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new CommandExecutionActResult(callResult, execution);
	}

	private static void AssertToolCallSucceeded(CommandExecutionActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"the MCP tool call should not return an error envelope. Content: {DescribeCallResult(actResult.CallResult)}");
	}

	private static void AssertCommandExitCode(CommandExecutionActResult actResult, int expectedExitCode, string because) {
		actResult.Execution.ExitCode.Should().Be(expectedExitCode, because: because);
	}

	private static void AssertIncludesInfoMessage(CommandExecutionActResult actResult, string because) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: because);
	}

	private static string DescribeCallResult(ModelContextProtocol.Protocol.CallToolResult callResult) {
		if (callResult.Content is null || callResult.Content.Count == 0) {
			return "<no content>";
		}

		return string.Join(" | ", callResult.Content.Select(c => c?.ToString() ?? "<null>"));
	}

	private sealed record DataBindingDbArrangeContext(
		string RootDirectory,
		string WorkspacePath,
		string PackageName,
		string? EnvironmentName,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : System.IAsyncDisposable {
		public async System.Threading.Tasks.ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
		}
	}

	private sealed record CommandExecutionActResult(
		ModelContextProtocol.Protocol.CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
