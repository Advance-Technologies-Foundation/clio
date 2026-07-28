using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the <c>find-app</c> MCP tool.
/// </summary>
[TestFixture]
// [AllureNUnit] is intentionally omitted for the same reason as EntitySchemaToolE2ETests:
// its NUnit lifecycle hooks can deadlock async MCP flows. The Allure metadata attributes
// below are still safe because they do not install lifecycle hooks.
[NonParallelizable]
public sealed class FindAppToolE2ETests {
	private const string FindAppToolName = FindAppTool.FindAppToolName;

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Starts the real clio MCP server, calls find-app with no filter against the sandbox, and verifies a structured applications-with-sections envelope.")]
	[AllureTag(FindAppToolName)]
	[AllureName("find-app returns applications with their sections")]
	[AllureDescription("Uses the real clio MCP server to call find-app for the configured sandbox environment with no filter and verifies the response succeeds and always includes the applications collection, with a sections collection on every application item.")]
	public async Task FindApp_Should_Return_Applications_With_Sections() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await CallFindAppAsync(
			session,
			settings.Sandbox.EnvironmentName!,
			cancellationTokenSource.Token);
		FindAppResponseEnvelope result = EntitySchemaStructuredResultParser.Extract<FindAppResponseEnvelope>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid find-app request should return a structured MCP payload instead of a transport-level error");
		result.Success.Should().BeTrue(
			because: "find-app should succeed whether the target environment currently has zero installed apps or many");
		result.Applications.Should().NotBeNull(
			because: "find-app should always include the applications collection so MCP clients can handle empty and populated environments uniformly");
		result.Applications!.Should().OnlyContain(application => application.Sections != null,
			because: "every find-app application item must carry its sections collection so callers never need a follow-up list-app-sections call");
		result.Error.Should().BeNullOrWhiteSpace(
			because: "successful find-app calls should not include an error payload");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Starts the real clio MCP server, calls find-app with an unknown environment, and verifies the structured error carries an actionable reg-web-app fix.")]
	[AllureTag(FindAppToolName)]
	[AllureName("find-app reports invalid environment with an actionable reg-web-app hint")]
	[AllureDescription("Uses the real clio MCP server to call find-app with a guaranteed-missing environment name and verifies the structured error envelope names the environment and includes a copy-pasteable reg-web-app command.")]
	public async Task FindApp_Should_Report_Invalid_Environment_With_Actionable_Hint() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string invalidEnvironmentName = $"missing-find-app-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallFindAppAsync(
			session,
			invalidEnvironmentName,
			cancellationTokenSource.Token,
			searchPattern: "case");
		FindAppResponseEnvelope result = EntitySchemaStructuredResultParser.Extract<FindAppResponseEnvelope>(callResult);

		// Assert
		result.Success.Should().BeFalse(
			because: "find-app should return a structured error envelope when the requested environment is not registered");
		result.Error.Should().Contain("reg-web-app",
			because: "the env-not-found error must include a copy-pasteable reg-web-app fix so the agent can self-heal");
		result.Error.Should().Contain(invalidEnvironmentName,
			because: "the actionable fix should reference the exact environment name the caller tried to use");
	}

	private static async Task<CallToolResult> CallFindAppAsync(
		McpServerSession session,
		string environmentName,
		CancellationToken cancellationToken,
		string? searchPattern = null,
		string? code = null) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(FindAppToolName,
			because: "the find-app MCP tool must be advertised before the end-to-end call can be executed");

		Dictionary<string, object?> args = new() {
			["environment-name"] = environmentName
		};
		if (!string.IsNullOrWhiteSpace(searchPattern)) {
			args["search-pattern"] = searchPattern;
		}

		if (!string.IsNullOrWhiteSpace(code)) {
			args["code"] = code;
		}

		return await session.CallToolAsync(
			FindAppToolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			cancellationToken);
	}
}
