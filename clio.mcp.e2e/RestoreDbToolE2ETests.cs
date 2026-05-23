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
	[Description("Starts the real clio MCP server, discovers the consolidated restore-db tool, and verifies that it is advertised as a destructive operation with the expected mode discriminator.")]
	[AllureTag(RestoreDbTool.RestoreDbToolName)]
	[AllureName("Restore-db tool advertises stable name, destructive metadata, and mode discriminator")]
	[AllureDescription("Uses the real clio MCP server tool discovery payload to verify that the consolidated restore-db tool is discoverable, marked destructive, and advertises the mode discriminator covering the environment, db-credentials, and local-server contracts.")]
	public async Task RestoreDb_Should_Advertise_Consolidated_Tool() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		McpClientTool restoreTool = tools.Single(tool => tool.Name == RestoreDbTool.RestoreDbToolName);

		// Assert
		restoreTool.ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue(
			because: "the consolidated restore-db tool can replace a target database in every mode");

		JsonElement inputSchema = JsonSerializer.SerializeToElement(restoreTool.ProtocolTool.InputSchema);
		JsonElement argsProperties = inputSchema.GetProperty("properties").GetProperty("args").GetProperty("properties");
		string[] propertyNames = argsProperties.EnumerateObject().Select(property => property.Name).ToArray();
		propertyNames.Should().Contain(
			["mode", "environment-name", "db-server-uri", "db-server-name", "backup-path", "db-name", "force", "as-template", "disable-reset-password"],
			because: "the consolidated restore-db MCP tool must advertise the mode discriminator together with every mode-specific argument that callers rely on");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes restore-db with mode='local-server' against invalid inputs, and verifies that the response still includes a database-operation log artifact path.")]
	[AllureTag(RestoreDbTool.RestoreDbToolName)]
	[AllureName("Restore-db failures still surface log-file-path")]
	[AllureDescription("Uses the real clio MCP server to call restore-db with mode='local-server' against a missing backup path and verifies that the failure remains human-readable while still returning a temp database-operation log artifact path.")]
	public async Task RestoreDb_Should_Return_Log_File_Path_On_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string missingBackupPath = Path.Combine(Path.GetTempPath(), $"missing-restore-{Guid.NewGuid():N}.backup");

		// Act
		var callResult = await session.CallToolAsync(
		ClioRunTool.ToolName,
		new Dictionary<string, object?> {
			["args"] = new Dictionary<string, object?> {
				["command"] = RestoreDbTool.RestoreDbToolName,
					["mode"] = RestoreDbTool.ModeLocalServer,
					["db-server-name"] = $"missing-{Guid.NewGuid():N}",
					["backup-path"] = missingBackupPath,
					["db-name"] = $"db_{Guid.NewGuid():N}"
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
