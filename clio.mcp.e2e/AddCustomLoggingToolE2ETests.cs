using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using System.Text.Json;
using System.Xml.Linq;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>End-to-end contract tests for the add-custom-logging MCP tool.</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("add-custom-logging")]
[Parallelizable(ParallelScope.Self)]
public sealed class AddCustomLoggingToolE2ETests : McpContractFixtureBase {
	private const string ToolName = AddCustomLoggingTool.ToolName;
	private const string EnvironmentName = "custom-logging-e2e";
	private const string PackageName = "UsrMcpLogging";
	private string _environmentRoot = string.Empty;

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		_environmentRoot = CreateFixtureDirectory("custom-logging-environment");
		string packagePath = Path.Combine(_environmentRoot, "Terrasoft.Configuration", "Pkg", PackageName,
			"Files", "src", "cs");
		Directory.CreateDirectory(packagePath);
		File.WriteAllText(Path.Combine(packagePath, "Constants.cs"),
			"internal static class Constants {\r\ninternal const string LoggerName = \"UsrMcpLoggingApp\";\r\n}");
		File.WriteAllText(Path.Combine(_environmentRoot, "nlog.config"),
			"<nlog><rules><logger name=\"*\" writeTo=\"file\" minlevel=\"Info\" /></rules></nlog>");
		File.WriteAllText(Path.Combine(_environmentRoot, "nlog.targets.config"),
			"<nlog xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><variable name=\"TodayLogPath\" value=\"Logs\" /><variable name=\"DefaultLayout\" value=\"${message}\" /><targets><target name=\"file\" xsi:type=\"File\" layout=\"${DefaultLayout}\" fileName=\"${TodayLogPath}/Common.log\" /></targets></nlog>");
		string clioHome = CreateIsolatedClioHome($$"""
			{
			  "ActiveEnvironmentKey": "{{EnvironmentName}}",
			  "Autoupdate": false,
			  "Environments": {
			    "{{EnvironmentName}}": {
			      "Uri": "http://localhost",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": true,
			      "EnvironmentPath": {{JsonSerializer.Serialize(_environmentRoot)}}
			    }
			  }
			}
			""", GetType().Name);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("add-custom-logging tool is discoverable on the lazy surface")]
	[Description("Discovers add-custom-logging through the real clio MCP server compact tool index.")]
	public async Task AddCustomLogging_ShouldBeDiscoverable_WhenLazySurfaceIsQueried() {
		// Arrange
		await using var context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "long-tail tools must remain reachable through get-tool-contract even when absent from tools/list");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("add-custom-logging reports an unregistered environment")]
	[Description("Invokes add-custom-logging through the real MCP process and reports an invalid environment failure.")]
	public async Task AddCustomLogging_ShouldReportError_WhenEnvironmentIsNotRegistered() {
		// Arrange
		await using var context = Arrange();
		string environmentName = $"missing-custom-logging-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await AllureApi.Step("Invoke add-custom-logging through MCP", () =>
			context.Session.CallToolAsync(
				ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["package-name"] = "MyPackage"
					}
				},
				context.CancellationTokenSource.Token));
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the MCP request must reach normal command execution rather than fail in dispatch or binding");
		execution.ExitCode.Should().Be(1,
			because: "an unregistered environment cannot identify a local installation to update");
		string output = string.Join(Environment.NewLine, (execution.Output ?? []).Select(message => message.Value));
		output.Should().Contain(environmentName,
			because: "the real MCP response must identify the rejected registered environment name");
		execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: "the failure must be classified as command error output rather than a transport failure");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("add-custom-logging updates both files and is idempotent")]
	[Description("Invokes add-custom-logging through the real MCP process and verifies both local file changes and an unchanged rerun.")]
	public async Task AddCustomLogging_ShouldUpdateFiles_WhenRegisteredLocalEnvironmentIsValid() {
		// Arrange
		await using var context = Arrange();
		Dictionary<string, object?> arguments = new() {
			["args"] = new Dictionary<string, object?> {
				["environment-name"] = EnvironmentName,
				["package-name"] = PackageName
			}
		};

		// Act
		CallToolResult firstResult = await context.Session.CallToolAsync(
			ToolName, arguments, context.CancellationTokenSource.Token);
		CommandExecutionEnvelope firstExecution = McpCommandExecutionParser.Extract(firstResult);
		byte[] rulesAfterFirst = File.ReadAllBytes(Path.Combine(_environmentRoot, "nlog.config"));
		byte[] targetsAfterFirst = File.ReadAllBytes(Path.Combine(_environmentRoot, "nlog.targets.config"));
		CallToolResult secondResult = await context.Session.CallToolAsync(
			ToolName, arguments, context.CancellationTokenSource.Token);
		CommandExecutionEnvelope secondExecution = McpCommandExecutionParser.Extract(secondResult);

		// Assert
		firstResult.IsError.Should().NotBeTrue(because: "the MCP transport should complete normally");
		firstExecution.ExitCode.Should().Be(0, because: "valid local files should be updated successfully");
		firstExecution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: "successful command execution must return informational output");
		secondExecution.ExitCode.Should().Be(0, because: "an exact rerun must be idempotent");
		File.ReadAllBytes(Path.Combine(_environmentRoot, "nlog.config")).Should().Equal(rulesAfterFirst,
			because: "an idempotent rerun must not rewrite the logger document");
		File.ReadAllBytes(Path.Combine(_environmentRoot, "nlog.targets.config")).Should().Equal(targetsAfterFirst,
			because: "an idempotent rerun must not rewrite the target document");
		XDocument.Load(Path.Combine(_environmentRoot, "nlog.config")).Descendants()
			.Count(element => element.Name.LocalName == "logger" && element.Attribute("name")?.Value == "UsrMcpLoggingApp")
			.Should().Be(1, because: "the command must add exactly one package logger");
		XDocument.Load(Path.Combine(_environmentRoot, "nlog.targets.config")).Descendants()
			.Count(element => element.Name.LocalName == "target" && element.Attribute("name")?.Value == "usrMcpLoggingAppender")
			.Should().Be(1, because: "the command must add exactly one package target");
	}
}
