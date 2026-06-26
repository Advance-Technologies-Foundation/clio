using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the get-process-signature MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("get-process-signature")]
[NonParallelizable]
public sealed class GetProcessSignatureToolE2ETests {
	private const string ToolName = GetProcessSignatureTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes get-process-signature for the configured sandbox process, and verifies a structured signature with parameter codes.")]
	[AllureTag(ToolName)]
	[AllureName("Get process signature returns structured parameter codes")]
	[AllureDescription("Uses the real clio MCP server to call get-process-signature for the configured sandbox process code and verifies the returned signature is successful, echoes the process code, and exposes parameters keyed by code (not caption).")]
	public async Task GetProcessSignature_Should_Return_Structured_Signature() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run get-process-signature end-to-end tests against a live sandbox.");
		}

		string? environmentName = settings.Sandbox.EnvironmentName;
		string? processCode = settings.Sandbox.ProcessCode;
		if (string.IsNullOrWhiteSpace(environmentName) || string.IsNullOrWhiteSpace(processCode)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName and McpE2E:Sandbox:ProcessCode to run get-process-signature success E2E.");
		}

		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"get-process-signature MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}

		await using GetProcessSignatureArrangeContext arrangeContext = await ArrangeAsync(settings);

		// Act
		GetProcessSignatureEnvelope envelope = await ActAsync(arrangeContext, processCode!, environmentName!);

		// Assert
		envelope.Success.Should().BeTrue(
			because: $"get-process-signature should resolve the configured sandbox process. Error: {envelope.Error}");
		envelope.ProcessCode.Should().Be(processCode,
			because: "the response should echo the requested process code");
		envelope.Parameters.Should().NotBeNull(
			because: "a successful signature must always carry a parameters collection (possibly empty)");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes get-process-signature with an invalid environment name, and verifies a readable structured failure.")]
	[AllureTag(ToolName)]
	[AllureName("Get process signature reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call get-process-signature with an unknown environment name and verifies that the MCP result stays structured and reports a human-readable failure with success=false.")]
	public async Task GetProcessSignature_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string invalidEnvironmentName = $"missing-gps-env-{Guid.NewGuid():N}";
		await using GetProcessSignatureArrangeContext arrangeContext = await ArrangeAsync(settings);

		// Act
		GetProcessSignatureEnvelope envelope = await ActAsync(arrangeContext, "UsrMissingProcess", invalidEnvironmentName);

		// Assert
		envelope.Success.Should().BeFalse(
			because: "an unknown environment cannot resolve a process signature");
		envelope.Error.Should().NotBeNullOrWhiteSpace(
			because: "failed signature lookups should carry a human-readable error");
		envelope.Error!.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|not registered)",
			because: "the failure should help a human understand that the requested environment is not registered");
	}

	private static async Task<GetProcessSignatureArrangeContext> ArrangeAsync(McpE2ESettings settings) {
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new GetProcessSignatureArrangeContext(session, cancellationTokenSource);
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private static async Task<GetProcessSignatureEnvelope> ActAsync(
		GetProcessSignatureArrangeContext arrangeContext,
		string processName,
		string environmentName) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the get-process-signature MCP tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["process-name"] = processName,
					["environment-name"] = environmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		return GetProcessSignatureResultParser.Extract(callResult);
	}

	private sealed record GetProcessSignatureArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
