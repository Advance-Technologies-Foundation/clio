using System.Text.RegularExpressions;
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
/// Stand-free end-to-end contract tests for the generate-process-model MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("generate-process-model")]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class GenerateProcessModelContractToolE2ETests : McpContractFixtureBase {
	private const string ToolName = GenerateProcessModelTool.GenerateProcessModelToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes generate-process-model with an invalid environment name, and verifies that a readable structured failure is returned without creating a file.")]
	[AllureTag(ToolName)]
	[AllureName("Generate process model reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call generate-process-model with an unknown environment name and verifies that the MCP result stays structured, reports a human-readable failure, and does not create the expected output file.")]
	public async Task GenerateProcessModel_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		ClioProcessDescriptor process = ClioExecutableResolver.Resolve(settings);
		string rootDirectory = Path.Combine(process.WorkingDirectory, $"gpm-invalid-e2e-{Guid.NewGuid():N}");
		string destinationPath = Path.Combine(Path.GetFileName(rootDirectory), "generated", "missing-process-model.cs");
		string generatedFilePath = Path.Combine(process.WorkingDirectory, destinationPath);
		Directory.CreateDirectory(rootDirectory);
		await using ArrangeContext arrangeContext = Arrange();
		string invalidEnvironmentName = $"missing-gpm-env-{Guid.NewGuid():N}";
		string missingProcessCode = "UsrMissingProcess";

		try {
			// Act
			GenerateProcessModelActResult actResult = await ActAsync(
				arrangeContext,
				missingProcessCode,
				invalidEnvironmentName,
				destinationPath);

			// Assert
			actResult.CallResult.IsError.Should().NotBeTrue(
				because: "invalid environment failures should be returned as normal command execution envelopes");
			actResult.Execution.ExitCode.Should().Be(1,
				because: $"unknown environment names should fail before generate-process-model writes files. Actual execution: {DescribeExecution(actResult.Execution)}");
			actResult.Execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
				because: "failed generate-process-model execution should emit error diagnostics");
			string combinedOutput = string.Join(
				Environment.NewLine,
				(actResult.Execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));
			combinedOutput.Should().MatchRegex(
				$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
				because: "the failure should help a human understand that the requested environment is not registered");
			File.Exists(generatedFilePath).Should().BeFalse(
				because: "invalid environment failures must not create the requested process model file");
		}
		finally {
			if (Directory.Exists(rootDirectory)) {
				Directory.Delete(rootDirectory, recursive: true);
			}
		}
	}

	private static async Task<GenerateProcessModelActResult> ActAsync(
		ArrangeContext arrangeContext,
		string code,
		string environmentName,
		string destinationPath) {
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the generate-process-model MCP tool must be discoverable via the get-tool-contract compact index on the lazy surface before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["code"] = code,
					["destination-path"] = destinationPath,
					["namespace"] = "Contoso.ProcessModels",
					["culture"] = "en-US",
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new GenerateProcessModelActResult(callResult, execution);
	}

	private static string DescribeExecution(CommandExecutionEnvelope execution) {
		string messages = execution.Output is null
			? "<no messages>"
			: string.Join(" | ", execution.Output.Select(message => $"{message.MessageType}: {message.Value}"));
		return $"ExitCode={execution.ExitCode}; Messages={messages}";
	}

	private sealed record GenerateProcessModelActResult(CallToolResult CallResult, CommandExecutionEnvelope Execution);
}
