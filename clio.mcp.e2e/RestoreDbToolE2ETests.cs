using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("restore-db")]
[Parallelizable(ParallelScope.Self)]
public sealed class RestoreDbToolE2ETests : McpContractFixtureBase {
	[Test]
	[Description("Starts the real clio MCP server, reads the get-tool-contract compact index, and verifies that all three restore-db entrypoints are discoverable and flagged destructive on the lazy tool surface.")]
	[AllureTag(RestoreDbTool.RestoreDbByEnvironmentToolName)]
	[AllureTag(RestoreDbTool.RestoreDbByCredentialsToolName)]
	[AllureTag(RestoreDbTool.RestoreDbToLocalServerToolName)]
	[AllureName("Restore-db tools are discoverable with destructive metadata on the lazy surface")]
	[AllureDescription("Uses the get-tool-contract compact index and full contract of the real clio MCP server to verify that the environment, credentials, and local-server restore-db tools are all discoverable, marked destructive, and expose the approved argument contract.")]
	public async Task RestoreDb_Should_Advertise_All_Tool_Variants() {
		// Arrange
		await using var arrangeContext = Arrange();
		CancellationToken token = arrangeContext.CancellationTokenSource.Token;

		// Act
		IReadOnlyList<ToolContractIndexEntry> index = await arrangeContext.Session.GetToolContractIndexAsync(token);
		CallToolResult contractResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { RestoreDbTool.RestoreDbToLocalServerToolName }
				}
			},
			token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);

		// Assert
		ToolContractIndexEntry environmentEntry = index.Should()
			.ContainSingle(entry => entry.Name == RestoreDbTool.RestoreDbByEnvironmentToolName,
				because: "the environment-based restore-db tool must be discoverable via the get-tool-contract compact index on the lazy surface")
			.Which;
		ToolContractIndexEntry credentialsEntry = index.Should()
			.ContainSingle(entry => entry.Name == RestoreDbTool.RestoreDbByCredentialsToolName,
				because: "the credentials-based restore-db tool must be discoverable via the get-tool-contract compact index on the lazy surface")
			.Which;
		ToolContractIndexEntry localServerEntry = index.Should()
			.ContainSingle(entry => entry.Name == RestoreDbTool.RestoreDbToLocalServerToolName,
				because: "the local-server restore-db tool must be discoverable via the get-tool-contract compact index on the lazy surface")
			.Which;
		environmentEntry.Destructive.Should().BeTrue(
			because: "environment-based restore-db execution can replace a target database");
		credentialsEntry.Destructive.Should().BeTrue(
			because: "credentials-based restore-db execution can replace a target database");
		localServerEntry.Destructive.Should().BeTrue(
			because: "local-server restore-db execution can replace a target database");

		ToolContractDefinition localContract = contracts.Tools!
			.Single(tool => tool.Name == RestoreDbTool.RestoreDbToLocalServerToolName);
		localContract.InputSchema.Properties.Select(property => property.Name)
			.Should().BeEquivalentTo(["dbServerName", "backupPath", "dbName", "dropIfExists", "asTemplate", "disableResetPassword"],
				because: "the local restore-db MCP tool should expose the approved local restore argument contract through get-tool-contract");
	}

	[Test]
	// NoEnvironment tier: the missing-backup/missing-server inputs fail validation before any
	// Kubernetes/DB call, so the tool returns a structured failure with a log-file artifact env-free.
	// It was previously ignored as "requires reachable Kubernetes/DB infrastructure", but the real
	// blocker was the no-Kubernetes fallback IKubernetes client throwing from Dispose during
	// per-request DI-scope teardown (opaque InternalError) — fixed under ENG-91830.
	[Description("Starts the real clio MCP server, invokes restore-db-to-local-server with invalid inputs, and verifies that the response still includes a database-operation log artifact path.")]
	[AllureTag(RestoreDbTool.RestoreDbToLocalServerToolName)]
	[AllureName("Restore-db failures still surface log-file-path")]
	[AllureDescription("Uses the real clio MCP server to call restore-db-to-local-server with a missing backup path and verifies that the failure remains human-readable while still returning a temp database-operation log artifact path.")]
	public async Task RestoreDb_Should_Return_Log_File_Path_On_Failure() {
		// Arrange
		await using var arrangeContext = Arrange();
		string missingBackupPath = Path.Combine(Path.GetTempPath(), $"missing-restore-{Guid.NewGuid():N}.backup");

		// Act
		var callResult = await arrangeContext.Session.CallToolAsync(
			RestoreDbTool.RestoreDbToLocalServerToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["dbServerName"] = $"missing-{Guid.NewGuid():N}",
					["backupPath"] = missingBackupPath,
					["dbName"] = $"db_{Guid.NewGuid():N}"
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		string combinedOutput = string.Join(
			Environment.NewLine,
			(execution.Output ?? []).Select(message => message.Value ?? string.Empty));

		// Assert
		(callResult.IsError == true || execution.ExitCode != 0).Should().BeTrue(
			because: "restore-db should fail when the requested local server is not configured");
		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "restore-db failures should still return human-readable diagnostics");
		execution.LogFilePath.Should().NotBeNullOrWhiteSpace(
			because: "restore-db should create and return the temp database-operation log artifact even when validation fails");
		File.Exists(execution.LogFilePath!).Should().BeTrue(
			because: "the returned restore-db log-file-path should reference a created temp artifact");
	}
}
