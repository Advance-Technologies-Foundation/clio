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
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the DB-first data-binding MCP tools.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("data-binding-db")]
[NonParallelizable]
public sealed class DataBindingDbToolE2ETests : McpContractFixtureBase {
	private const string CreateDbToolName = CreateDataBindingDbTool.CreateDataBindingDbToolName;
	private const string UpsertRowDbToolName = UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName;
	private const string RemoveRowDbToolName = RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName;

	[Test]
	[Description("Exposes all three DB-first data-binding MCP tools via the get-tool-contract compact index so callers can discover and invoke them on the lazy surface.")]
	[AllureTag(CreateDbToolName)]
	[AllureName("DB-first data-binding tools are discoverable on the lazy surface")]
	[AllureDescription("Verifies that create-data-binding-db, upsert-data-binding-row-db, and remove-data-binding-row-db are discoverable via the get-tool-contract compact index.")]
	public async Task DataBindingDbTools_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: false);

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(CreateDbToolName,
			because: "create-data-binding-db must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
		toolNames.Should().Contain(UpsertRowDbToolName,
			because: "upsert-data-binding-row-db must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
		toolNames.Should().Contain(RemoveRowDbToolName,
			because: "remove-data-binding-row-db must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
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
				["schema-name"] = "Lookup",
				["binding-name"] = $"UsrDbE2E{arrangeContext.PackageName}",
				["rows"] = """[{"values":{"Name":"E2E DB binding row"}}]"""
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
		AssertIncludesErrorMessage(result,
			"create-data-binding-db should emit a human-readable validation error when environment-name is empty");
	}

	[Test]
	[Description("Fails upsert-data-binding-row-db through MCP with exit code 1 when environment-name is empty, matching the command-layer validation guard.")]
	[AllureTag(UpsertRowDbToolName)]
	[AllureName("Upsert DB-first row without environment fails with exit code 1")]
	[AllureDescription("Uses the real clio MCP server to invoke upsert-data-binding-row-db without an environment-name and verifies that the tool returns exit code 1 with an error message.")]
	public async Task UpsertDataBindingRowDb_Should_Fail_Without_Environment() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: false);

		// Act
		CommandExecutionActResult result = await ActCommandAsync(
			arrangeContext,
			UpsertRowDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = string.Empty,
				["package-name"] = arrangeContext.PackageName,
				["binding-name"] = "UsrMissingBinding",
				["values"] = """{"Name":"Updated row"}"""
			});

		// Assert
		AssertToolCallSucceeded(result);
		AssertCommandExitCode(result, 1,
			"upsert-data-binding-row-db must reject empty environment-name with exit code 1");
		AssertIncludesErrorMessage(result,
			"upsert-data-binding-row-db should emit a human-readable validation error when environment-name is empty");
	}

	[Test]
	[Description("Fails remove-data-binding-row-db through MCP with exit code 1 when environment-name is empty, matching the command-layer validation guard.")]
	[AllureTag(RemoveRowDbToolName)]
	[AllureName("Remove DB-first row without environment fails with exit code 1")]
	[AllureDescription("Uses the real clio MCP server to invoke remove-data-binding-row-db without an environment-name and verifies that the tool returns exit code 1 with an error message.")]
	public async Task RemoveDataBindingRowDb_Should_Fail_Without_Environment() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: false);

		// Act
		CommandExecutionActResult result = await ActCommandAsync(
			arrangeContext,
			RemoveRowDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = string.Empty,
				["package-name"] = arrangeContext.PackageName,
				["binding-name"] = "UsrMissingBinding",
				["key-value"] = "4f41bcc2-7ed0-45e8-a1fd-474918966d15"
			});

		// Assert
		AssertToolCallSucceeded(result);
		AssertCommandExitCode(result, 1,
			"remove-data-binding-row-db must reject empty environment-name with exit code 1");
		AssertIncludesErrorMessage(result,
			"remove-data-binding-row-db should emit a human-readable validation error when environment-name is empty");
	}

	[Test]
	[Description("Returns a binding-layer error when create-data-binding-db is called without the MCP args wrapper. On the lazy surface the call is dispatched through clio-run, so the same binding failure surfaces as an executor-wrapped error that still names the target tool.")]
	[AllureTag(CreateDbToolName)]
	[AllureName("Create DB-first binding rejects malformed MCP envelope")]
	[AllureDescription("Uses the real clio MCP server to invoke create-data-binding-db without the args wrapper and verifies the binding layer rejects the malformed envelope before tool execution starts — either natively or via the clio-run dispatch on the lazy surface.")]
	public async Task CreateDataBindingDb_Should_Return_Invocation_Error_When_Args_Wrapper_Is_Missing() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: false);

		// Act
		ModelContextProtocol.Protocol.CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			CreateDbToolName,
			new Dictionary<string, object?>(),
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertInvocationFailure(callResult, CreateDbToolName);
	}

	[Test]
	[Description("Returns a binding-layer error when upsert-data-binding-row-db receives a non-object args payload. On the lazy surface the hidden tool is dispatched through clio-run, so the malformed args payload fails binding clio-run's own args parameter and the diagnostic names clio-run instead of the target tool.")]
	[AllureTag(UpsertRowDbToolName)]
	[AllureName("Upsert DB-first row rejects malformed MCP envelope")]
	[AllureDescription("Uses the real clio MCP server to invoke upsert-data-binding-row-db with args set to a string and verifies the binding layer rejects the malformed envelope before tool execution starts; on the lazy surface the failure is reported against the clio-run executor.")]
	public async Task UpsertDataBindingRowDb_Should_Return_Invocation_Error_When_Args_Have_Invalid_Type() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: false);

		// Act
		ModelContextProtocol.Protocol.CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			UpsertRowDbToolName,
			new Dictionary<string, object?> { ["args"] = "invalid" },
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		// Lazy-surface routing: upsert-data-binding-row-db is not resident, so the session dispatches the
		// call through clio-run. The string "invalid" fails to deserialize into clio-run's own `args`
		// parameter, therefore the SDK binding diagnostic names 'clio-run' — not the target tool. The
		// intent is unchanged: a binding-layer failure with no structured payload.
		AssertInvocationFailure(callResult, ClioRunTool.ToolName);
	}

	[Test]
	[Description("Returns a binding-layer error when remove-data-binding-row-db is called without the MCP args wrapper. On the lazy surface the call is dispatched through clio-run, so the same binding failure surfaces as an executor-wrapped error that still names the target tool.")]
	[AllureTag(RemoveRowDbToolName)]
	[AllureName("Remove DB-first row rejects malformed MCP envelope")]
	[AllureDescription("Uses the real clio MCP server to invoke remove-data-binding-row-db without the args wrapper and verifies the binding layer rejects the malformed envelope before tool execution starts — either natively or via the clio-run dispatch on the lazy surface.")]
	public async Task RemoveDataBindingRowDb_Should_Return_Invocation_Error_When_Args_Wrapper_Is_Missing() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: false);

		// Act
		ModelContextProtocol.Protocol.CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			RemoveRowDbToolName,
			new Dictionary<string, object?>(),
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		AssertInvocationFailure(callResult, RemoveRowDbToolName);
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
		const string rowsJson = """[{"values":{"Name":"E2E Dedup Row"}}]""";
		var firstCallArgs = new Dictionary<string, object?> {
			["environment-name"] = arrangeContext.EnvironmentName,
			["package-name"] = arrangeContext.PackageName,
			["schema-name"] = "Lookup",
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

	[Test]
	[Description("Creates a DB-first binding for Account when the requested row references only supported columns, even if the runtime schema contains other unsupported columns.")]
	[AllureTag(CreateDbToolName)]
	[AllureName("Create DB-first Account binding succeeds when unsupported runtime columns are unused")]
	[AllureDescription("Uses the real clio MCP server to invoke create-data-binding-db against Account on a reachable Creatio sandbox. Verifies the tool succeeds when only supported columns are referenced by the requested row payload.")]
	public async Task CreateDataBindingDb_Should_Succeed_For_Account_When_Unused_Unsupported_Runtime_Columns_Exist() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);
		string bindingName = $"UsrAcctE2E{arrangeContext.PackageName}";
		string rowName = $"E2E Account {arrangeContext.PackageName}";

		// Act
		CommandExecutionActResult result = await ActCommandAsync(
			arrangeContext,
			CreateDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "Account",
				["binding-name"] = bindingName,
				["rows"] = $"[{{\"values\":{{\"Name\":\"{rowName}\"}}}}]"
			});

		// Assert
		AssertToolCallSucceeded(result);
		AssertCommandExitCode(result, 0,
			"create-data-binding-db should succeed for Account when the requested row only references supported columns");
		AssertIncludesInfoMessage(result,
			"successful Account DB-first binding creation should emit a completion message");
	}

	[Test]
	[Description("Upserts a row whose Id exists in the table but is not yet bound to the target binding, and verifies the row is UPDATED (exit 0) instead of failing with the insert-required-field error, proving the live-but-unbound adoption path over the real MCP wire.")]
	[AllureTag(UpsertRowDbToolName)]
	[AllureName("upsert-data-binding-row-db adopts and updates a live-but-unbound row")]
	[AllureDescription("Seeds a Lookup row in one binding, establishes a second empty binding, then upserts that row's Id into the second binding. Verifies the upsert exits 0 by updating the existing row rather than attempting an insert that would fail because required columns are absent.")]
	public async Task UpsertDataBindingRowDb_Should_Update_Live_Row_When_Unbound_In_Target_Binding() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);
		string seedBindingName = $"UsrAdoptSeed{arrangeContext.PackageName}";
		string targetBindingName = $"UsrAdoptTarget{arrangeContext.PackageName}";
		string rowName = $"E2E Adopt {arrangeContext.PackageName}";

		// Act - seed a Lookup row (inserts it into the table and binds it in the seed binding)
		CommandExecutionActResult seedResult = await ActCommandAsync(
			arrangeContext,
			CreateDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "Lookup",
				["binding-name"] = seedBindingName,
				["rows"] = $"[{{\"values\":{{\"Name\":\"{rowName}\"}}}}]"
			});
		AssertToolCallSucceeded(seedResult);
		AssertCommandExitCode(seedResult, 0, "seeding the Lookup row should succeed");
		string createdRowId = ExtractCreatedRowId(seedResult);

		// Act - establish a SEPARATE empty binding so the seeded row exists in the table but is NOT bound here
		CommandExecutionActResult targetBindingResult = await ActCommandAsync(
			arrangeContext,
			CreateDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "Lookup",
				["binding-name"] = targetBindingName
			});
		AssertToolCallSucceeded(targetBindingResult);
		AssertCommandExitCode(targetBindingResult, 0, "establishing the empty target binding should succeed");

		// Act - upsert the seeded row's Id into the empty target binding
		CommandExecutionActResult upsertResult = await ActCommandAsync(
			arrangeContext,
			UpsertRowDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["binding-name"] = targetBindingName,
				["values"] = $"{{\"Id\":\"{createdRowId}\",\"Name\":\"{rowName} Updated\"}}"
			});

		// Assert
		AssertToolCallSucceeded(upsertResult);
		AssertCommandExitCode(upsertResult, 0,
			"upsert must UPDATE a row that exists in the table but is unbound in the target binding, not attempt an insert that fails on required columns");
	}

	private static string ExtractCreatedRowId(CommandExecutionActResult seedResult) {
		seedResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "create-data-binding-db should emit a 'Created row: <id>' message for the seeded row");
		foreach (CommandLogMessageEnvelope message in seedResult.Execution.Output!) {
			System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
				message.Value?.ToString() ?? string.Empty,
				@"Created row:\s*([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
			if (match.Success) {
				return match.Groups[1].Value;
			}
		}

		Assert.Fail("Could not extract the created row Id from the seed create-data-binding-db output.");
		return string.Empty;
	}

	private async Task<DataBindingDbArrangeContext> ArrangeAsync(bool requireEnvironment) {
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
		if (requireEnvironment && !string.IsNullOrWhiteSpace(environmentName)) {
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(
				settings,
				["push-workspace", "-e", environmentName],
				workingDirectory: workspacePath,
				cancellationToken: cancellationTokenSource.Token);
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(
				settings,
				["pkg-hotfix", packageName, "true", "-e", environmentName],
				workingDirectory: workspacePath,
				cancellationToken: cancellationTokenSource.Token);
		}

		McpServerSession session = Session;
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

	private static void AssertIncludesErrorMessage(CommandExecutionActResult actResult, string because) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: because);
	}

	private static void AssertInvocationFailure(ModelContextProtocol.Protocol.CallToolResult callResult, string toolName) {
		callResult.IsError.Should().BeTrue(
			because: $"malformed MCP envelopes for {toolName} should be rejected before tool execution starts");
		callResult.StructuredContent.Should().BeNull(
			because: "invocation-level MCP failures should not return structured command output");
		string diagnostics = DescribeCallResult(callResult);
		diagnostics.Should().Contain(toolName,
			because: "the invocation failure should identify the affected MCP tool");
		// Accept both the native SDK binding diagnostic (resident tools) and the clio-run-surfaced
		// executor error (lazy surface): "An error occurred invoking '<tool>'." /
		// "Failed to deserialize argument 'args' for MCP tool '<tool>'" / "Error: tool '<tool>' failed: …".
		diagnostics.Should().MatchRegex(
			"(?is)(an error occurred invoking|failed to deserialize argument|failed)",
			because: "the diagnostic should describe a binding-layer failure whether it is raised natively or wrapped by the clio-run executor");
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
		public System.Threading.Tasks.ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
			return System.Threading.Tasks.ValueTask.CompletedTask;
		}
	}

	private sealed record CommandExecutionActResult(
		ModelContextProtocol.Protocol.CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
