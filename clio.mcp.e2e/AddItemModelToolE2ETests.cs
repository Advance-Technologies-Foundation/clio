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
/// End-to-end tests for the add-item-model MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("add-item-model")]
[NonParallelizable]
public sealed class AddItemModelToolE2ETests {
	private const string ToolName = AddItemModelTool.AddItemModelToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes add-item-model against a reachable environment, and verifies that model files are generated into the requested folder.")]
	[AllureTag(ToolName)]
	[AllureName("Add item model generates files into requested folder")]
	[AllureDescription("Uses the real clio MCP server to call add-item-model for a reachable environment and verifies that BaseModelExtensions.cs plus at least one generated model file are written into the requested local folder.")]
	public async Task AddItemModel_Should_Generate_Model_Files() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to install cliogate and run add-item-model end-to-end tests.");
		}

		await using AddItemModelArrangeContext arrangeContext = await ArrangeSuccessAsync(settings);

		// Act
		AddItemModelActResult actResult = await ActAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.EnvironmentName,
			arrangeContext.OutputFolderPath);

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult, 0,
			"add-item-model should succeed for a reachable environment and an existing local folder");
		AssertIncludesInfoMessage(actResult,
			"successful add-item-model execution should emit progress output");
		File.Exists(Path.Combine(arrangeContext.OutputFolderPath, "BaseModelExtensions.cs")).Should().BeTrue(
			because: "model generation should emit the shared BaseModelExtensions helper file");
		Directory.GetFiles(arrangeContext.OutputFolderPath, "*.cs", SearchOption.TopDirectoryOnly)
			.Should().Contain(path => !string.Equals(Path.GetFileName(path), "BaseModelExtensions.cs", StringComparison.OrdinalIgnoreCase),
				because: "model generation should create at least one model class file in addition to the shared helper");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes add-item-model with a nonexistent folder, and verifies that a human-readable validation error is returned without creating files.")]
	[AllureTag(ToolName)]
	[AllureName("Add item model rejects nonexistent folder")]
	[AllureDescription("Uses the real clio MCP server to call add-item-model with a missing absolute folder and verifies that the MCP result stays structured, the command fails clearly, and no output directory is created.")]
	public async Task AddItemModel_Should_Report_Invalid_Folder_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using AddItemModelFailureArrangeContext arrangeContext = await ArrangeFailureAsync(settings);

		// Act
		AddItemModelActResult actResult = await ActAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			"missing-env-not-used",
			arrangeContext.MissingFolderPath);

		// Assert
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "folder validation failures should be returned as normal command execution envelopes");
		AssertCommandExitCode(actResult, 1,
			"add-item-model should reject nonexistent folders before command execution");
		actResult.Execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: "folder validation failures should emit error-level diagnostics");
		DescribeExecution(actResult.Execution).Should().Contain("Folder path not found",
			because: "the failure should explain why the requested folder was rejected");
		Directory.Exists(arrangeContext.MissingFolderPath).Should().BeFalse(
			because: "the tool should not create the missing output folder when validation fails");
	}

	private static async Task<AddItemModelArrangeContext> ArrangeSuccessAsync(McpE2ESettings settings) {
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(10));
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		await ClioCliCommandRunner.EnsureCliogateInstalledAsync(settings, environmentName, cancellationTokenSource.Token);
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-add-item-model-e2e-{Guid.NewGuid():N}");
		string outputFolderPath = Path.Combine(rootDirectory, "Models");
		Directory.CreateDirectory(outputFolderPath);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new AddItemModelArrangeContext(rootDirectory, outputFolderPath, environmentName, session, cancellationTokenSource);
	}

	private static async Task<AddItemModelFailureArrangeContext> ArrangeFailureAsync(McpE2ESettings settings) {
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-add-item-model-invalid-{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootDirectory);
		string missingFolderPath = Path.Combine(rootDirectory, "MissingModels");
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new AddItemModelFailureArrangeContext(rootDirectory, missingFolderPath, session, cancellationTokenSource);
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
			$"add-item-model MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private static async Task<AddItemModelActResult> ActAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string folder) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the add-item-model MCP tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["namespace"] = "Contoso.Models",
					["folder"] = folder,
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new AddItemModelActResult(callResult, execution);
	}

	private static void AssertToolCallSucceeded(AddItemModelActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"successful add-item-model requests should return a normal MCP tool result. Actual MCP content: {DescribeCallResult(actResult.CallResult)}. Parsed execution: {DescribeExecution(actResult.Execution)}");
	}

	private static void AssertCommandExitCode(AddItemModelActResult actResult, int expectedExitCode, string because) {
		actResult.Execution.ExitCode.Should().Be(expectedExitCode,
			because: $"{because}. Actual execution: {DescribeExecution(actResult.Execution)}");
	}

	private static void AssertIncludesInfoMessage(AddItemModelActResult actResult, string because) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "successful add-item-model execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: because);
	}

	private sealed record AddItemModelArrangeContext(
		string RootDirectory,
		string OutputFolderPath,
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

	private sealed record AddItemModelFailureArrangeContext(
		string RootDirectory,
		string MissingFolderPath,
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

	private sealed record AddItemModelActResult(CallToolResult CallResult, CommandExecutionEnvelope Execution);

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
