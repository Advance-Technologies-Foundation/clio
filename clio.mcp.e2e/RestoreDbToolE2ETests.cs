using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using System.Text.Json;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[AllureFeature("restore-db")]
public sealed class RestoreDbToolE2ETests {
	[Test]
	[Description("Starts the real clio MCP server, discovers the restore-db tools, and verifies that all three restore entrypoints are advertised as destructive operations.")]
	[AllureTag(RestoreDbTool.RestoreDbByEnvironmentToolName)]
	[AllureTag(RestoreDbTool.RestoreDbByCredentialsToolName)]
	[AllureTag(RestoreDbTool.RestoreDbToLocalServerToolName)]
	[AllureName("Restore-db tools advertise stable names and destructive metadata")]
	[AllureDescription("Uses the real clio MCP server tool discovery payload to verify that the environment, credentials, and local-server restore-db tools are all discoverable and marked destructive.")]
	public async Task RestoreDb_Should_Advertise_All_Tool_Variants() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		McpClientTool environmentTool = tools.Single(tool => tool.Name == RestoreDbTool.RestoreDbByEnvironmentToolName);
		McpClientTool credentialsTool = tools.Single(tool => tool.Name == RestoreDbTool.RestoreDbByCredentialsToolName);
		McpClientTool localServerTool = tools.Single(tool => tool.Name == RestoreDbTool.RestoreDbToLocalServerToolName);

		// Assert
		environmentTool.ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue(
			because: "environment-based restore-db execution can replace a target database");
		credentialsTool.ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue(
			because: "credentials-based restore-db execution can replace a target database");
		localServerTool.ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue(
			because: "local-server restore-db execution can replace a target database");

		JsonElement localInputSchema = JsonSerializer.SerializeToElement(localServerTool.ProtocolTool.InputSchema);
		localInputSchema.GetProperty("properties").GetProperty("args").GetProperty("properties").EnumerateObject()
			.Select(property => property.Name)
			.Should().BeEquivalentTo(["dbServerName", "backupPath", "dbName", "dropIfExists"],
				because: "the local restore-db MCP tool should advertise the approved local restore argument contract");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes restore-db-to-local-server with invalid inputs, and verifies that the response still includes a database-operation log artifact path.")]
	[AllureTag(RestoreDbTool.RestoreDbToLocalServerToolName)]
	[AllureName("Restore-db failures still surface log-file-path")]
	[AllureDescription("Uses the real clio MCP server to call restore-db-to-local-server with a missing backup path and verifies that the failure remains human-readable while still returning a temp database-operation log artifact path.")]
	public async Task RestoreDb_Should_Return_Log_File_Path_On_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string missingBackupPath = Path.Combine(Path.GetTempPath(), $"missing-restore-{Guid.NewGuid():N}.backup");

		// Act
		var callResult = await session.CallToolAsync(
			RestoreDbTool.RestoreDbToLocalServerToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["dbServerName"] = $"missing-{Guid.NewGuid():N}",
					["backupPath"] = missingBackupPath,
					["dbName"] = $"db_{Guid.NewGuid():N}"
				}
			},
			cancellationTokenSource.Token);
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
