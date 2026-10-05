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
public sealed class FindAppToolE2ETests : McpContractFixtureBase {
	private const string FindAppToolName = FindAppTool.FindAppToolName;
	private const int MaxCodeLookups = 20;

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
		McpServerSession session = Session;

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
	[Description("Starts the real clio MCP server and verifies that find-app with no filter returns, for every application, the same sections as find-app with that application's exact code (ENG-102120).")]
	[AllureTag(FindAppToolName)]
	[AllureName("find-app batch returns every application's sections")]
	[AllureDescription("Calls find-app with no filter, then find-app with each of the first 20 returned application codes, and verifies the section codes match per application. The batch request covers all applications at once, the code request covers one; the virtual ApplicationSection schema once answered a multi-application OR filter with the first application's sections only.")]
	public async Task FindApp_Should_Return_The_Same_Sections_As_A_Code_Lookup_For_Every_Application() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		McpServerSession session = Session;
		string environmentName = settings.Sandbox.EnvironmentName!;

		// Act
		FindAppResponseEnvelope batch = EntitySchemaStructuredResultParser.Extract<FindAppResponseEnvelope>(
			await CallFindAppAsync(session, environmentName, cancellationTokenSource.Token));
		batch.Success.Should().BeTrue(because: $"find-app with no filter should succeed: {batch.Error}");
		// One sequential code lookup per application; bounded so a stand with many apps stays inside the timeout.
		FindAppItemEnvelope[] sampled = (batch.Applications ?? []).Take(MaxCodeLookups).ToArray();
		Dictionary<string, string[]> expectedSectionsByCode = new(StringComparer.OrdinalIgnoreCase);
		foreach (FindAppItemEnvelope application in sampled) {
			FindAppResponseEnvelope single = EntitySchemaStructuredResultParser.Extract<FindAppResponseEnvelope>(
				await CallFindAppAsync(session, environmentName, cancellationTokenSource.Token, code: application.Code));
			single.Success.Should().BeTrue(
				because: $"find-app with the exact code '{application.Code}' should succeed: {single.Error}");
			expectedSectionsByCode[application.Code!] = SectionCodes(single.Applications!.Single());
		}
		if (expectedSectionsByCode.Values.Count(codes => codes.Length > 0) < 2) {
			Assert.Inconclusive("The sandbox needs at least two applications with sections among the first " +
				$"{MaxCodeLookups}, or a batch that returns only the first application's sections would pass unnoticed.");
		}

		// Assert
		sampled.ToDictionary(application => application.Code!, SectionCodes, StringComparer.OrdinalIgnoreCase)
			.Should().BeEquivalentTo(expectedSectionsByCode,
				because: "the batch request must return every application's sections, not only the first application's");
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
		McpServerSession session = Session;
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

	private static string[] SectionCodes(FindAppItemEnvelope application) =>
		(application.Sections ?? []).Select(section => section.Code ?? string.Empty).Order(StringComparer.Ordinal).ToArray();

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
