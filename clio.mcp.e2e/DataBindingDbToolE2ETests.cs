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
public sealed class DataBindingDbToolE2ETests : DataBindingDbFixtureBase {

	[Test]
	[Description("Exposes every DB-first data-binding MCP tool via the get-tool-contract compact index so callers can discover and invoke them on the lazy surface.")]
	[AllureTag(CreateDbToolName)]
	[AllureName("DB-first data-binding tools are discoverable on the lazy surface")]
	[AllureDescription("Verifies that create-data-binding-db, upsert-data-binding-row-db, remove-data-binding-row-db, and read-data-binding-db are discoverable via the get-tool-contract compact index.")]
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
	toolNames.Should().Contain(ReadDbToolName,
			because: "read-data-binding-db must be discoverable on the lazy surface, otherwise agents keep exporting the whole package to prove a projection");
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
	[Description("Reports through MCP that a DB-first binding exists only in the database exactly when the environment is in file system development mode, naming pkg-to-file-system as the step that writes it into the package folder (GitHub #1747).")]
	[AllureTag(CreateDbToolName)]
	[AllureName("Create DB-first data binding reports the package folder gap only in file system mode")]
	[AllureDescription("Creates a binding through the real clio MCP server, reads the sandbox's file system development mode with get-fsm-mode, and verifies the result carries the database-only warning naming pkg-to-file-system when the mode is on, and no such warning when it is off.")]
	public async Task CreateDataBindingDb_Should_Report_Package_Folder_Gap_Only_In_File_System_Mode() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);
		string bindingName = $"UsrDbFsmE2E{arrangeContext.PackageName}";

		// Act
		CommandExecutionActResult result = await ActCommandAsync(
			arrangeContext,
			CreateDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "Lookup",
				["binding-name"] = bindingName,
				["rows"] = """[{"values":{"Name":"E2E FSM gap row"}}]"""
			});
		// get-fsm-mode derives the mode from GetApplicationInfo (useStaticFileContent), while the writer reads
		// WorkspaceExplorerService.GetIsFileDesignMode. Both follow the same fileDesignMode switch on a stand
		// configured by turn-fsm; a stand where the two disagree fails here, and that disagreement is worth seeing.
		FsmModeStatusEnvelope fsmStatus = FsmModeStatusResultParser.Extract(
			await arrangeContext.Session.CallToolAsync(
				FsmModeTool.GetFsmModeToolName,
				new Dictionary<string, object?> { ["environmentName"] = arrangeContext.EnvironmentName },
				arrangeContext.CancellationTokenSource.Token));
		bool fileDesignModeOn = string.Equals(fsmStatus.Mode, "on", System.StringComparison.OrdinalIgnoreCase);
		TestContext.Out.WriteLine($"Sandbox file system development mode: {fsmStatus.Mode}; asserting the "
			+ (fileDesignModeOn ? "warning" : "absence of any warning") + ".");

		// Assert
		AssertToolCallSucceeded(result);
		AssertCommandExitCode(result, 0,
			"the warning must never turn a saved binding into a failure that a caller would retry");
		AssertIncludesInfoMessage(result,
			"a saved binding still reports its completion message next to any warning");
		AssertOutputDoesNotContain(result, "could not read whether file system development mode is enabled",
			"the real GetIsFileDesignMode request must be readable, or every binding write carries a conditional warning");
		if (fileDesignModeOn) {
			AssertOutputContains(result, $"Data/{bindingName}",
				"in file system development mode the result must name the package folder that lacks the binding");
			AssertOutputContains(result, LoadPackagesTool.LoadPackagesToFileSystemToolName,
				"in file system development mode the result must name the command that writes the binding to disk");
		} else {
			AssertOutputDoesNotContain(result, LoadPackagesTool.LoadPackagesToFileSystemToolName,
				"with file system development mode off the package folder on disk is not the source of the package, so there is nothing to warn about");
			AssertOutputDoesNotContain(result, $"Data/{bindingName}",
				"with file system development mode off no package folder is named");
		}
	}

	[Test]
	[Description("Writes an existing DB-first binding to the workspace through create-data-binding with from-environment, reproducing the database binding UId and row instead of generating a new descriptor (ENG-106290).")]
	[AllureTag(CreateDbToolName)]
	[AllureTag(CreateDataBindingTool.CreateDataBindingToolName)]
	[AllureName("create-data-binding from-environment writes one DB binding to disk with the database UId")]
	[AllureDescription("Creates a DB-first Lookup binding with a known row Id through create-data-binding-db, reads its uId with read-data-binding-db, then calls create-data-binding with from-environment=true and no schema-name over the real MCP transport, and asserts that Data/<binding>/descriptor.json carries the database uId and data.json the row Id.")]
	public async Task CreateDataBinding_Should_Write_Db_Binding_With_Database_UId_When_From_Environment() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);
		string bindingName = $"UsrFromEnvE2E{arrangeContext.PackageName}";
		string rowId = System.Guid.NewGuid().ToString();
		CommandExecutionActResult createDbResult = await ActCommandAsync(
			arrangeContext,
			CreateDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["schema-name"] = "Lookup",
				["binding-name"] = bindingName,
				["rows"] = $"[{{\"values\":{{\"Id\":\"{rowId}\",\"Name\":\"E2E from-environment row\"}}}}]"
			});
		AssertCommandExitCode(createDbResult, 0, "the DB binding must exist before it can be written to disk");
		CommandExecutionActResult readResult = await ActCommandAsync(
			arrangeContext,
			ReadDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["binding-name"] = bindingName
			});
		AssertCommandExitCode(readResult, 0, "read-data-binding-db reports the uId the descriptor must carry");
		string databaseUId = System.Text.RegularExpressions.Regex.Match(
				string.Join("\n", readResult.Execution.Output!.Select(message => message.Value?.ToString() ?? string.Empty)),
				@"uId:\s+([0-9a-fA-F-]{36})")
			.Groups[1].Value;
		databaseUId.Should().NotBeEmpty(because: "read-data-binding-db prints the binding uId");

		// Act
		CommandExecutionActResult result = await ActCommandAsync(
			arrangeContext,
			CreateDataBindingTool.CreateDataBindingToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["package-name"] = arrangeContext.PackageName,
				["binding-name"] = bindingName,
				["workspace-path"] = arrangeContext.WorkspacePath,
				["from-environment"] = true
			});

		// Assert
		AssertToolCallSucceeded(result);
		AssertCommandExitCode(result, 0,
			"from-environment needs no schema-name and writes the stored binding when it exists");
		string bindingDirectoryPath = Path.Combine(
			arrangeContext.WorkspacePath, "packages", arrangeContext.PackageName, "Data", bindingName);
		string descriptorJson = await File.ReadAllTextAsync(Path.Combine(bindingDirectoryPath, "descriptor.json"));
		descriptorJson.Should().ContainEquivalentOf(databaseUId,
			because: "the folder must describe the database binding, not a newly generated one");
		string dataJson = await File.ReadAllTextAsync(Path.Combine(bindingDirectoryPath, "data.json"));
		dataJson.Should().ContainEquivalentOf(rowId, because: "data.json carries the stored row of the binding");
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
	[Description("read-data-binding-db reaches its binding-resolution layer over the real MCP server and reports a human-readable failure for an unresolvable environment, so an agent never mistakes an unreachable read for a proven projection.")]
	[AllureTag(ReadDbToolName)]
	[AllureName("Read DB-first binding reports failure without an environment")]
	[AllureDescription("Invokes read-data-binding-db through the real clio MCP server with an empty environment-name and verifies the read fails with a readable message instead of returning an empty projection that would read as 'no columns shipped'.")]
	public async Task ReadDataBindingDb_Should_Fail_Without_Environment(){
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: false);

		// Act
		CommandExecutionActResult result = await ActCommandAsync(
			arrangeContext,
			ReadDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = string.Empty,
				["package-name"] = arrangeContext.PackageName,
				["binding-name"] = "UsrMissingBinding"
			});

		// Assert
		AssertToolCallSucceeded(result);
		AssertCommandExitCode(result, 1,
			"read-data-binding-db must reject empty environment-name with exit code 1");
		AssertIncludesErrorMessage(result,
			"an unresolvable read must say why, because an empty result would read as 'the binding ships no columns'");
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
		string targetBindingName = $"UsrAdoptTarget{arrangeContext.PackageName}";
		string rowName = $"E2E Adopt {arrangeContext.PackageName}";
		// Caller-supplied Id so the seeded row is known up front (odata-create echoes it back).
		string createdRowId = System.Guid.NewGuid().ToString();

		// Act - seed the Lookup row UNBOUND via odata-create: it inserts the row into the table WITHOUT
		// binding it, so the row is genuinely live-but-unbound in the target binding. Seeding via
		// create-data-binding-db would BIND the record, and the platform forbids binding the same record in a
		// second binding of the same schema+package - which is not the scenario this test exercises.
		CommandExecutionActResult seedResult = await ActCommandAsync(
			arrangeContext,
			ODataCreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["entity"] = "Lookup",
				["rows"] = new[] {
					new Dictionary<string, object?> { ["Id"] = createdRowId, ["Name"] = rowName }
				}
			});
		AssertToolCallSucceeded(seedResult);

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

	[Test]
	[Description("ENG-105291: refuses create-data-binding-db through MCP when a row's localizations name a culture that is not active on the site, names the culture and the active cultures, and creates no row.")]
	[AllureTag(CreateDbToolName)]
	[AllureName("create-data-binding-db refuses an inactive localization culture before any write")]
	[AllureDescription("Invokes create-data-binding-db with localizations in a culture the site does not have and verifies exit code 1, an error naming that culture and the active cultures, and no 'Created row' output.")]
	public async Task CreateDataBindingDb_Should_Refuse_Inactive_Localization_Culture_Before_Any_Write() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);

		// Act
		CommandExecutionActResult result = await ActCommandAsync(arrangeContext, CreateDbToolName,
			BuildLocalizedRowArgs(arrangeContext, $"UsrLczRefuseE2E{arrangeContext.PackageName}", "xx-XX", "E2E refused row"));

		// Assert
		AssertToolCallSucceeded(result);
		AssertCommandExitCode(result, 1, "a culture the site does not have must refuse the whole call");
		AssertOutputContains(result, "Culture 'xx-XX' is not active on the site",
			"the refusal must name the culture the agent has to fix");
		AssertOutputContains(result, "nothing was written", "the refusal must say no write happened");
		AssertOutputDoesNotContain(result, "Created row", "the check runs before any row is inserted");
	}

	[Test]
	[Description("ENG-105291: stores a lookup value in an active culture other than the connected user's through create-data-binding-db over MCP, reads it back, and reports the record and culture written.")]
	[AllureTag(CreateDbToolName)]
	[AllureName("create-data-binding-db stores and verifies a lookup value translation")]
	[AllureDescription("Discovers an active non-en-US culture from the refusal message, then creates a Lookup row with a translation in that culture and verifies exit code 0 and the 'Localized row' line; the tool itself fails when the value read back differs.")]
	public async Task CreateDataBindingDb_Should_Store_Lookup_Value_Translation() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);
		string culture = await DiscoverActiveCultureOtherThanEnUsAsync(arrangeContext);

		// Act
		CommandExecutionActResult result = await ActCommandAsync(arrangeContext, CreateDbToolName,
			BuildLocalizedRowArgs(arrangeContext, $"UsrLczE2E{arrangeContext.PackageName}", culture!,
				$"E2E translated row {arrangeContext.PackageName}"));

		// Assert
		AssertToolCallSucceeded(result);
		AssertCommandExitCode(result, 0,
			"the translation is written and the value read back matches, otherwise the tool fails as not verified");
		AssertOutputContains(result, $"[{culture}]: Name", "the result lists the culture and column written per record");
	}

	[Test]
	[Description("ENG-105291: forwards seed-rows localizations of a sync-schemas seed-data operation to create-data-binding-db over MCP, so translations sent on the canonical batched path are written instead of dropped.")]
	[AllureTag(SchemaSyncTool.ToolName)]
	[AllureName("sync-schemas seed-rows carry lookup value translations")]
	[AllureDescription("Runs a sync-schemas seed-data operation on Lookup whose seed row carries localizations in an active non-en-US culture and verifies the seed-data result reports the 'Localized row' line for that culture.")]
	public async Task SyncSchemas_SeedRows_Should_Store_Lookup_Value_Translation() {
		// Arrange
		await using DataBindingDbArrangeContext arrangeContext = await ArrangeAsync(requireEnvironment: true);
		string culture = await DiscoverActiveCultureOtherThanEnUsAsync(arrangeContext);
		string rowName = $"E2E seed translated {arrangeContext.PackageName}";

		// Act
		ModelContextProtocol.Protocol.CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			SchemaSyncTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = arrangeContext.EnvironmentName,
					["package-name"] = arrangeContext.PackageName,
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "seed-data",
							["schema-name"] = "Lookup",
							["seed-rows"] = new object?[] {
								new Dictionary<string, object?> {
									["values"] = new Dictionary<string, object?> { ["Name"] = rowName },
									["localizations"] = new Dictionary<string, object?> {
										[culture] = new Dictionary<string, object?> { ["Name"] = $"{rowName} ({culture})" }
									}
								}
							}
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		string diagnostics = DescribeCallResult(callResult);
		callResult.IsError.Should().NotBeTrue(because: $"the seed-data operation is valid. Result: {diagnostics}");
		diagnostics.Should().Contain($"[{culture}]: Name",
			because: "the seed-data result must report the translation written per record and culture, proving the localizations reached create-data-binding-db");
	}

	/// <summary>
	///     Reads the active cultures from the refusal of an impossible culture and returns one other than en-US,
	///     or ignores the test when the sandbox has none.
	/// </summary>
	private static async Task<string> DiscoverActiveCultureOtherThanEnUsAsync(DataBindingDbArrangeContext arrangeContext) {
		CommandExecutionActResult probe = await ActCommandAsync(arrangeContext, CreateDbToolName,
			BuildLocalizedRowArgs(arrangeContext, $"UsrLczProbeE2E{arrangeContext.PackageName}", "xx-XX", "E2E probe row"));
		string probeOutput = DescribeExecution(probe.Execution);
		System.Text.RegularExpressions.Match activeList = System.Text.RegularExpressions.Regex.Match(
			probeOutput, @"active cultures: (?<list>[^)]*)\)");
		string? culture = activeList.Groups["list"].Value
			.Split(',', System.StringSplitOptions.TrimEntries | System.StringSplitOptions.RemoveEmptyEntries)
			.FirstOrDefault(name => !string.Equals(name, "en-US", System.StringComparison.OrdinalIgnoreCase));
		if (culture is null) {
			Assert.Ignore($"The sandbox has no active culture besides en-US ({probeOutput}); activate one to run this test.");
		}

		return culture!;
	}

	private static Dictionary<string, object?> BuildLocalizedRowArgs(
		DataBindingDbArrangeContext arrangeContext, string bindingName, string culture, string rowName) =>
		new() {
			["environment-name"] = arrangeContext.EnvironmentName,
			["package-name"] = arrangeContext.PackageName,
			["schema-name"] = "Lookup",
			["binding-name"] = bindingName,
			["rows"] = System.Text.Json.JsonSerializer.Serialize(new[] {
				new Dictionary<string, object> {
					["values"] = new Dictionary<string, string> { ["Name"] = rowName },
					["localizations"] = new Dictionary<string, Dictionary<string, string>> {
						[culture] = new() { ["Name"] = $"{rowName} ({culture})" }
					}
				}
			})
		};

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
		// Accept every binding-layer surface, all of which report the same contract the asserts above
		// pin — the call failed before the tool ran, with no structured payload:
		//   * the contracted pre-method binder diagnostic ("invalid-parameter-type: argument '<name>' for
		//     MCP tool '<tool>' must be …");
		//   * the native SDK message ("An error occurred invoking '<tool>'." / "Failed to deserialize
		//     argument 'args' for MCP tool '<tool>'");
		//   * the clio-run executor wrapper ("Error: tool '<tool>' failed: …");
		//   * ENG-95885's shape-naming variant, which PRE-EMPTS the raw serializer text whenever an
		//     argument the tool expects as a JSON OBJECT arrives as a JSON string ("… must be a JSON
		//     object, not a JSON string."). That replacement is the point of the change — "…
		//     BytePositionInLine: 31" told an agent nothing about the shape it should have sent.
		diagnostics.Should().MatchRegex(
			"(?is)(invalid-parameter-type|an error occurred invoking|failed to deserialize argument|failed|must be a JSON object)",
			because: "the diagnostic should describe a binding-layer failure whether it is raised natively, wrapped by the clio-run executor, or named as the required argument shape");
	}

}
