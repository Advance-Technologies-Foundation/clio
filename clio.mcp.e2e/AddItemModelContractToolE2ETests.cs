using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract tests for the add-item-model MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("add-item-model")]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class AddItemModelContractToolE2ETests : McpContractFixtureBase {
	private const string ToolName = AddItemModelTool.AddItemModelToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes add-item-model with a relative folder, and verifies that a human-readable validation error is returned without creating files.")]
	[AllureTag(ToolName)]
	[AllureName("Add item model rejects relative folder")]
	[AllureDescription("Uses the real clio MCP server to call add-item-model with a relative folder and verifies that the MCP result stays structured, the command fails clearly, and no unintended output directory is created.")]
	public async Task AddItemModel_Should_Report_Invalid_Folder_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		ClioProcessDescriptor process = ClioExecutableResolver.Resolve(settings);
		string relativeFolderPath = Path.Combine($"relative-add-item-model-{Guid.NewGuid():N}", "Models");
		string accidentalOutputFolderPath = Path.Combine(process.WorkingDirectory, relativeFolderPath);
		await using ArrangeContext arrangeContext = Arrange();

		// Act
		AddItemModelActResult actResult = await ActAsync(
			arrangeContext,
			"missing-env-not-used",
			relativeFolderPath);

		// Assert
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "folder validation failures should be returned as normal command execution envelopes");
		actResult.Execution.ExitCode.Should().Be(1,
			because: $"add-item-model should reject relative folders before command execution. Actual execution: {DescribeExecution(actResult.Execution)}");
		actResult.Execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: "folder validation failures should emit error-level diagnostics");
		DescribeExecution(actResult.Execution).Should().Contain("Folder path must be absolute",
			because: "the failure should explain that the requested folder must be absolute");
		Directory.Exists(accidentalOutputFolderPath).Should().BeFalse(
			because: "the tool should not create an output folder for invalid relative paths");
	}

	private static async Task<AddItemModelActResult> ActAsync(
		ArrangeContext arrangeContext,
		string environmentName,
		string folder) {
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the add-item-model MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["namespace"] = "Contoso.Models",
					["folder"] = folder,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new AddItemModelActResult(callResult, execution);
	}

	private static string DescribeExecution(CommandExecutionEnvelope execution) {
		string messages = execution.Output is null
			? "<no messages>"
			: string.Join(" | ", execution.Output.Select(message => $"{message.MessageType}: {message.Value}"));
		return $"ExitCode={execution.ExitCode}; Messages={messages}";
	}

	private sealed record AddItemModelActResult(CallToolResult CallResult, CommandExecutionEnvelope Execution);
}
