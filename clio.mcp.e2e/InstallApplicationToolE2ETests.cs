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
/// End-to-end tests for the install-application MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("install-application")]
[NonParallelizable]
public sealed class InstallApplicationToolE2ETests {
	private const string ToolName = InstallApplicationTool.InstallApplicationToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes install-application for a configured sandbox package, and verifies that the requested report file is created.")]
	[AllureTag(ToolName)]
	[AllureName("Install application writes requested report file")]
	[AllureDescription("Uses the real clio MCP server to call install-application for a configured sandbox environment and verifies that the command returns a structured success result and writes the requested report file.")]
	public async Task InstallApplication_Should_Create_Report_File() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run install-application end-to-end tests.");
		}

		await using InstallApplicationArrangeContext arrangeContext = await ArrangeSuccessAsync(settings);

		// Act
		InstallApplicationActResult actResult = await ActAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.ApplicationPackagePath,
			arrangeContext.EnvironmentName,
			arrangeContext.ReportPath);

		// Assert
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"successful install-application requests should return a normal MCP tool result. Actual MCP content: {DescribeCallResult(actResult.CallResult)}. Parsed execution: {DescribeExecution(actResult.Execution)}");
		actResult.Execution.ExitCode.Should().Be(0,
			because: $"install-application should succeed for the configured sandbox package. Actual execution: {DescribeExecution(actResult.Execution)}");
		actResult.Execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: "successful install-application execution should emit info diagnostics");
		File.Exists(arrangeContext.ReportPath).Should().BeTrue(
			because: "install-application should create the requested report file when report-path is provided");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes install-application with an invalid environment name, and verifies that a readable structured failure is returned without creating a report file.")]
	[AllureTag(ToolName)]
	[AllureName("Install application reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call install-application with an unknown environment name and verifies that the MCP result stays structured, reports a human-readable failure, and does not create the requested report file.")]
	public async Task InstallApplication_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using InstallApplicationArrangeContext arrangeContext = await ArrangeFailureAsync(settings);
		string invalidEnvironmentName = $"missing-install-app-env-{Guid.NewGuid():N}";

		// Act
		InstallApplicationActResult actResult = await ActAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			arrangeContext.ApplicationPackagePath,
			invalidEnvironmentName,
			arrangeContext.ReportPath);

		// Assert
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "invalid environment failures should be returned as normal command execution envelopes");
		actResult.Execution.ExitCode.Should().Be(1,
			because: $"unknown environment names should fail before install-application writes the report file. Actual execution: {DescribeExecution(actResult.Execution)}");
		actResult.Execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: "failed install-application execution should emit error diagnostics");
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should help a human understand that the requested environment is not registered");
		File.Exists(arrangeContext.ReportPath).Should().BeFalse(
			because: "invalid environment failures must not create the requested report file");
	}

	private static async Task<InstallApplicationArrangeContext> ArrangeSuccessAsync(McpE2ESettings settings) {
		string? environmentName = settings.Sandbox.EnvironmentName;
		string? applicationPackagePath = settings.Sandbox.ApplicationPackagePath;
		if (string.IsNullOrWhiteSpace(environmentName) || string.IsNullOrWhiteSpace(applicationPackagePath)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName and McpE2E:Sandbox:ApplicationPackagePath to run install-application success E2E.");
		}

		if (!File.Exists(applicationPackagePath)) {
			Assert.Ignore($"install-application MCP E2E requires an existing package file. '{applicationPackagePath}' was not found.");
		}

		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"install-application MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}

		ClioProcessDescriptor process = ClioExecutableResolver.Resolve(settings);
		string rootDirectory = Path.Combine(process.WorkingDirectory, $"install-application-e2e-{Guid.NewGuid():N}");
		string reportPath = Path.Combine(rootDirectory, "install-application.log");
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(10));
		Directory.CreateDirectory(rootDirectory);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new InstallApplicationArrangeContext(
			rootDirectory,
			reportPath,
			applicationPackagePath!,
			environmentName!,
			session,
			cancellationTokenSource);
	}

	private static async Task<InstallApplicationArrangeContext> ArrangeFailureAsync(McpE2ESettings settings) {
		ClioProcessDescriptor process = ClioExecutableResolver.Resolve(settings);
		string rootDirectory = Path.Combine(process.WorkingDirectory, $"install-application-invalid-e2e-{Guid.NewGuid():N}");
		string reportPath = Path.Combine(rootDirectory, "install-application.log");
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		Directory.CreateDirectory(rootDirectory);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new InstallApplicationArrangeContext(
			rootDirectory,
			reportPath,
			ApplicationPackagePath: settings.Sandbox.ApplicationPackagePath ?? @"C:\Packages\missing-app.gz",
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

	private static async Task<InstallApplicationActResult> ActAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string applicationPackagePath,
		string environmentName,
		string reportPath) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the install-application MCP tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["name"] = applicationPackagePath,
					["report-path"] = reportPath,
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new InstallApplicationActResult(callResult, execution);
	}

	private sealed record InstallApplicationArrangeContext(
		string RootDirectory,
		string ReportPath,
		string ApplicationPackagePath,
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

	private sealed record InstallApplicationActResult(CallToolResult CallResult, CommandExecutionEnvelope Execution);

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
