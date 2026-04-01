using System.Text.Json;
using System.Linq;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[AllureFeature("reg-web-app")]
[NonParallelizable]
public sealed class RegWebAppToolE2ETests {
	private const string ToolName = "reg-web-app";

	[Test]
	[AllureTag(ToolName)]
	[AllureName("reg-web-app auto-detects .NET Framework when only the /0 SelectQuery route is valid")]
	[AllureDescription("Starts the real clio MCP server with isolated settings, invokes reg-web-app without is-net-core, and verifies that the stored environment keeps IsNetCore=false when only the framework DataService route succeeds.")]
	[Description("Auto-detects the .NET Framework runtime through MCP registration and persists IsNetCore=false in the clio settings file.")]
	public async Task RegisterWebApp_Should_Persist_Framework_Runtime_When_AutoDetection_Finds_Framework_Route() {
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-reg-web-app-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			"""
			{
			  "ActiveEnvironmentKey": null,
			  "Environments": {}
			}
			""",
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
		await using RuntimeDetectionStubServer stubServer = RuntimeDetectionStubServer.Start(
			new RuntimeDetectionStubServerConfiguration(
				NetCoreHealthEnabled: true,
				NetFrameworkHealthEnabled: true,
				NetCoreServiceEnabled: false,
				NetFrameworkServiceEnabled: true,
				NetCoreUiMarkerEnabled: false,
				NetFrameworkUiMarkerEnabled: true));
		await using ArrangeContext context = await ArrangeAsync(settings, stubServer);

		RegWebAppActResult actResult = await ActAsync(context);

		AssertToolCallSucceeded(actResult);
		AssertCommandSucceeded(actResult);
		AssertInfoOutputIncludesAutoDetectedFrameworkMessage(actResult);
		AssertSettingsPersistedFrameworkRuntime(context.SettingsFilePath, actResult.EnvironmentName);
	}

	private static async Task<ArrangeContext> ArrangeAsync(
		McpE2ESettings settings,
		RuntimeDetectionStubServer stubServer) {
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string settingsFilePath = await ResolveSettingsFilePathAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, stubServer, settingsFilePath);
	}

	private static async Task<RegWebAppActResult> ActAsync(ArrangeContext context) {
		string environmentName = $"runtime-auto-{Guid.NewGuid():N}";
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the real MCP server must advertise reg-web-app before the test can invoke it");

		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["uri"] = context.StubServer.BaseUrl,
					["login"] = "Supervisor",
					["password"] = "Supervisor"
				}
			},
			context.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new RegWebAppActResult(environmentName, callResult, execution);
	}

	private static void AssertToolCallSucceeded(RegWebAppActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "reg-web-app should return a normal MCP tool result when auto-detection succeeds");
	}

	private static void AssertCommandSucceeded(RegWebAppActResult actResult) {
		actResult.Execution.ExitCode.Should().Be(0,
			because: $"the underlying reg-web-app command should finish successfully after auto-detecting the runtime. Actual execution: {DescribeExecution(actResult.Execution)}");
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "successful MCP command execution should include command log output");
		actResult.Execution.Output.Should().Contain(
			message => message.MessageType == LogDecoratorType.Info,
			because: "successful MCP command execution should include info-level log output");
	}

	private static void AssertInfoOutputIncludesAutoDetectedFrameworkMessage(RegWebAppActResult actResult) {
		actResult.Execution.Output.Should().Contain(
			message => message.MessageType == LogDecoratorType.Info
				&& string.Equals(message.Value, "Auto-detected runtime: .NET Framework", StringComparison.Ordinal),
			because: "the command should report which runtime was selected during registration");
	}

	private static void AssertSettingsPersistedFrameworkRuntime(string settingsFilePath, string environmentName) {
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settingsFilePath));
		JsonElement environment = document.RootElement
			.GetProperty("Environments")
			.GetProperty(environmentName);
		environment.GetProperty("IsNetCore").GetBoolean().Should().BeFalse(
			because: "the auto-detected framework route should be persisted in the clio environment settings");
		environment.GetProperty("Uri").GetString().Should().NotBeNullOrWhiteSpace(
			because: "reg-web-app should persist the registered environment URL");
	}

	private static async Task<string> ResolveSettingsFilePathAsync(
		McpE2ESettings settings,
		CancellationToken cancellationToken) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["info", "--settings-file"],
			cancellationToken: cancellationToken);
		result.ExitCode.Should().Be(0,
			because: $"the test must resolve the isolated clio settings file path before asserting persisted output. stderr: {result.StandardError}");
		string pathLine = result.StandardOutput
			.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Trim())
			.Last(line => line.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase));
		int separatorIndex = pathLine.IndexOf("- ", StringComparison.Ordinal);
		return separatorIndex >= 0
			? pathLine[(separatorIndex + 2)..].Trim()
			: pathLine;
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		RuntimeDetectionStubServer StubServer,
		string SettingsFilePath) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record RegWebAppActResult(
		string EnvironmentName,
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);

	private static string DescribeExecution(CommandExecutionEnvelope execution) {
		string output = execution.Output is null
			? "<none>"
			: string.Join(" | ", execution.Output.Select(message => $"{message.MessageType}:{message.Value}"));
		return $"ExitCode={execution.ExitCode}; Output={output}; LogFilePath={execution.LogFilePath ?? "<none>"}";
	}
}
