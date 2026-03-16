using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the generate-process-model MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("generate-process-model")]
[NonParallelizable]
public sealed class GenerateProcessModelToolE2ETests {
	private const string ToolName = GenerateProcessModelTool.GenerateProcessModelToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes generate-process-model for a configured sandbox process, and verifies that an explicit destination file path is respected.")]
	[AllureTag(ToolName)]
	[AllureName("Generate process model writes requested destination file")]
	[AllureDescription("Uses the real clio MCP server to call generate-process-model for a configured sandbox process code and verifies that the generated process model is written to the requested relative .cs file path.")]
	public async Task GenerateProcessModel_Should_Create_Process_Model_At_Explicit_File_Path() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run generate-process-model end-to-end tests.");
		}

		await using GenerateProcessModelArrangeContext arrangeContext = await ArrangeSuccessAsync(settings);

		// Act
		GenerateProcessModelActResult actResult = await ActAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.ProcessCode,
			arrangeContext.EnvironmentName,
			arrangeContext.DestinationPath);

		// Assert
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"successful generate-process-model requests should return a normal MCP tool result. Actual MCP content: {DescribeCallResult(actResult.CallResult)}. Parsed execution: {DescribeExecution(actResult.Execution)}");
		actResult.Execution.ExitCode.Should().Be(0,
			because: $"generate-process-model should succeed for the configured sandbox process. Actual execution: {DescribeExecution(actResult.Execution)}");
		File.Exists(arrangeContext.GeneratedFilePath).Should().BeTrue(
			because: "generate-process-model should create the expected file at the requested explicit destination path");
		string fileContent = await File.ReadAllTextAsync(arrangeContext.GeneratedFilePath);
		fileContent.Should().Contain("namespace Contoso.ProcessModels",
			because: "the generated file should use the namespace passed to the MCP tool");
		fileContent.Should().Contain($"public class {arrangeContext.ProcessCode}",
			because: "the generated file should define a class named after the requested process code");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes generate-process-model with an invalid environment name, and verifies that a readable structured failure is returned without creating a file.")]
	[AllureTag(ToolName)]
	[AllureName("Generate process model reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call generate-process-model with an unknown environment name and verifies that the MCP result stays structured, reports a human-readable failure, and does not create the expected output file.")]
	public async Task GenerateProcessModel_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using GenerateProcessModelArrangeContext arrangeContext = await ArrangeFailureAsync(settings);
		string invalidEnvironmentName = $"missing-gpm-env-{Guid.NewGuid():N}";
		string missingProcessCode = "UsrMissingProcess";

		// Act
		GenerateProcessModelActResult actResult = await ActAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			missingProcessCode,
			invalidEnvironmentName,
			arrangeContext.DestinationPath);

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
		File.Exists(arrangeContext.GeneratedFilePath).Should().BeFalse(
			because: "invalid environment failures must not create the requested process model file");
	}

	private static async Task<GenerateProcessModelArrangeContext> ArrangeSuccessAsync(McpE2ESettings settings) {
		string? environmentName = settings.Sandbox.EnvironmentName;
		string? processCode = settings.Sandbox.ProcessCode;
		if (string.IsNullOrWhiteSpace(environmentName) || string.IsNullOrWhiteSpace(processCode)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName and McpE2E:Sandbox:ProcessCode to run generate-process-model success E2E.");
		}

		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"generate-process-model MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}

		ClioProcessDescriptor process = ClioExecutableResolver.Resolve(settings);
		string rootDirectory = Path.Combine(process.WorkingDirectory, $"gpm-e2e-{Guid.NewGuid():N}");
		string destinationPath = Path.Combine(Path.GetFileName(rootDirectory), "generated", "custom-process-model.cs");
		string generatedDirectoryPath = Path.GetDirectoryName(Path.Combine(process.WorkingDirectory, destinationPath))!;
		string generatedFilePath = Path.Combine(process.WorkingDirectory, destinationPath);
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		Directory.CreateDirectory(rootDirectory);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new GenerateProcessModelArrangeContext(
			rootDirectory,
			destinationPath,
			generatedDirectoryPath,
			generatedFilePath,
			processCode!,
			environmentName!,
			session,
			cancellationTokenSource);
	}

	private static async Task<GenerateProcessModelArrangeContext> ArrangeFailureAsync(McpE2ESettings settings) {
		ClioProcessDescriptor process = ClioExecutableResolver.Resolve(settings);
		string rootDirectory = Path.Combine(process.WorkingDirectory, $"gpm-invalid-e2e-{Guid.NewGuid():N}");
		string destinationPath = Path.Combine(Path.GetFileName(rootDirectory), "generated", "missing-process-model.cs");
		string generatedDirectoryPath = Path.GetDirectoryName(Path.Combine(process.WorkingDirectory, destinationPath))!;
		string generatedFilePath = Path.Combine(process.WorkingDirectory, destinationPath);
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		Directory.CreateDirectory(rootDirectory);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new GenerateProcessModelArrangeContext(
			rootDirectory,
			destinationPath,
			generatedDirectoryPath,
			generatedFilePath,
			ProcessCode: "UsrMissingProcess",
			EnvironmentName: string.Empty,
			session,
			cancellationTokenSource);
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private static async Task<GenerateProcessModelActResult> ActAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string code,
		string environmentName,
		string destinationPath) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the generate-process-model MCP tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await session.CallToolAsync(
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
			cancellationToken);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new GenerateProcessModelActResult(callResult, execution);
	}

	private sealed record GenerateProcessModelArrangeContext(
		string RootDirectory,
		string DestinationPath,
		string GeneratedDirectoryPath,
		string GeneratedFilePath,
		string ProcessCode,
		string EnvironmentName,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
		}
	}

	private sealed record GenerateProcessModelActResult(CallToolResult CallResult, CommandExecutionEnvelope Execution);

	private static string DescribeCallResult(CallToolResult callResult) {
		if (callResult.Content is null || callResult.Content.Count == 0) {
			return "<no content>";
		}

		return string.Join(
			" | ",
			callResult.Content.Select(content => content?.ToString() ?? "<null>"));
	}

	private static string DescribeExecution(CommandExecutionEnvelope execution) {
		string messages = execution.Output is null
			? "<no messages>"
			: string.Join(" | ", execution.Output.Select(message => $"{message.MessageType}: {message.Value}"));
		return $"ExitCode={execution.ExitCode}; Messages={messages}";
	}
}
