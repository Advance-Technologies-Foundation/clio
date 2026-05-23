using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[NonParallelizable]
public sealed class ApplicationSectionMaintenanceToolE2ETests {
	private const string SectionListToolName = ApplicationSectionGetListTool.ApplicationSectionGetListToolName;
	private const string SectionDeleteToolName = ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName;

	[Test]
	[Description("Advertises list-app-sections in the MCP tool list so callers can discover the installed-app section discovery tool.")]
	[AllureFeature(SectionListToolName)]
	[AllureTag(SectionListToolName)]
	[AllureName("Application section list tool is advertised by the MCP server")]
	[AllureDescription("Starts the real clio MCP server and verifies that list-app-sections appears in the advertised tool manifest.")]
	public async Task ApplicationSectionGetList_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(SectionListToolName,
			because: "list-app-sections must be advertised so MCP callers can discover installed-app section discovery");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes list-app-sections with an invalid environment, and verifies that the failure remains human-readable.")]
	[AllureFeature(SectionListToolName)]
	[AllureTag(SectionListToolName)]
	[AllureName("Application section list reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call list-app-sections with an unknown environment name and verifies that the tool returns a structured readable error envelope.")]
	public async Task ApplicationSectionGetList_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string invalidEnvironmentName = $"missing-section-list-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["application-code"] = "UsrMissingApp"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionListContextResponseEnvelope response = ApplicationResultParser.ExtractSectionList(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured list-app-sections failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "list-app-sections should fail when the requested environment does not exist");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes list-app-sections without application-code, and verifies that the tool returns a clear validation failure.")]
	[AllureFeature(SectionListToolName)]
	[AllureTag(SectionListToolName)]
	[AllureName("Application section list rejects missing application-code")]
	[AllureDescription("Uses the real clio MCP server to call list-app-sections without application-code and verifies that the failure explains the required app selector.")]
	public async Task ApplicationSectionGetList_Should_Reject_Missing_ApplicationCode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionListContextResponseEnvelope response = ApplicationResultParser.ExtractSectionList(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "structured validation failures should stay inside the section-list response payload");
		response.Success.Should().BeFalse(
			because: "list-app-sections should reject requests that omit application-code");
		response.Error.Should().MatchRegex("(?is)(application-code|required)",
			because: "the failure should explain that application-code is required");
	}

	[Test]
	[Description("Starts the real clio MCP server, discovers an installed application through list-apps, and verifies that list-app-sections returns a structured section envelope for that application.")]
	[AllureFeature(SectionListToolName)]
	[AllureTag(SectionListToolName)]
	[AllureName("Application section list returns structured section metadata")]
	[AllureDescription("Uses the real clio MCP server to discover an installed application via list-apps, ignores when none exist, and otherwise verifies that list-app-sections returns the expected structured installed-application section envelope.")]
	public async Task ApplicationSectionGetList_Should_Return_Structured_Section_List() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		ApplicationListItemEnvelope installedApplication = await ResolveInstalledApplicationOrIgnoreAsync(
			session,
			cancellationTokenSource.Token,
			environmentName);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = installedApplication.Code
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionListContextResponseEnvelope response = ApplicationResultParser.ExtractSectionList(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured list-app-sections success should be returned in the payload instead of as an MCP invocation error. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeTrue(
			because: "list-app-sections should succeed for a discovered installed application");
		response.ApplicationId.Should().Be(installedApplication.Id,
			because: "the section list envelope should resolve the same installed application discovered through list-apps");
		response.ApplicationCode.Should().Be(installedApplication.Code,
			because: "the section list envelope should preserve the discovered installed application code");
		response.ApplicationName.Should().Be(installedApplication.Name,
			because: "the section list envelope should preserve the discovered installed application name");
		response.Sections.Should().NotBeNull(
			because: "list-app-sections should always include the sections collection so clients can handle empty and populated applications uniformly");
		response.Error.Should().BeNullOrWhiteSpace(
			because: "successful list-app-sections calls should not include an error payload");
	}

	[Test]
	[Description("Advertises delete-app-section in the MCP tool list so callers can discover the installed-app section deletion tool.")]
	[AllureFeature(SectionDeleteToolName)]
	[AllureTag(SectionDeleteToolName)]
	[AllureName("Application section delete tool is advertised by the MCP server")]
	[AllureDescription("Starts the real clio MCP server and verifies that delete-app-section appears in the advertised tool manifest.")]
	public async Task ApplicationSectionDelete_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(SectionDeleteToolName,
			because: "delete-app-section must be advertised so MCP callers can discover installed-app section deletion");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes delete-app-section with an invalid environment, and verifies that the failure remains human-readable.")]
	[AllureFeature(SectionDeleteToolName)]
	[AllureTag(SectionDeleteToolName)]
	[AllureName("Application section delete reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call delete-app-section with an unknown environment name and verifies that the tool returns a structured readable error envelope.")]
	public async Task ApplicationSectionDelete_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string invalidEnvironmentName = $"missing-section-delete-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionDeleteToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["application-code"] = "UsrMissingApp",
					["section-code"] = "UsrMissingSection"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionDeleteContextResponseEnvelope response = ApplicationResultParser.ExtractSectionDelete(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured delete-app-section failures should be returned in the payload instead of as MCP invocation errors. Actual result: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "delete-app-section should fail when the requested environment does not exist");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes delete-app-section without section-code, and verifies that the tool returns a clear validation failure.")]
	[AllureFeature(SectionDeleteToolName)]
	[AllureTag(SectionDeleteToolName)]
	[AllureName("Application section delete rejects missing section-code")]
	[AllureDescription("Uses the real clio MCP server to call delete-app-section without section-code and verifies that the failure explains the required section selector.")]
	public async Task ApplicationSectionDelete_Should_Reject_Missing_SectionCode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionDeleteToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = "UsrOrdersApp"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionDeleteContextResponseEnvelope response = ApplicationResultParser.ExtractSectionDelete(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "structured validation failures should stay inside the section-delete response payload");
		response.Success.Should().BeFalse(
			because: "delete-app-section should reject requests that omit section-code");
		response.Error.Should().MatchRegex("(?is)(section-code|required)",
			because: "the failure should explain that section-code is required");
	}

	[Test]
	[Description("Deferred positive coverage for delete-app-section when the E2E environment has a known installed application and section lifecycle data.")]
	[AllureFeature(SectionDeleteToolName)]
	[AllureTag(SectionDeleteToolName)]
	[AllureName("Application section delete removes a created section from the section list")]
	[AllureDescription("Placeholder for a future seeded-data E2E that creates, lists, deletes, and re-lists a section in a known installed application.")]
	public void ApplicationSectionDelete_Should_Remove_Created_Section_From_Section_List() {
		Assert.Ignore("TODO: ENG-88547 add predefined installed application data to the E2E environment, then restore this positive delete-app-section lifecycle scenario.");
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
			$"application section MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", environmentName],
				cancellationToken: cts.Token);
			return result.ExitCode == 0;
		} catch (OperationCanceledException) {
			return false;
		}
	}

	private static async Task<ApplicationListItemEnvelope> ResolveInstalledApplicationOrIgnoreAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName) {
		CallToolResult callResult = await session.CallToolAsync(
			ApplicationGetListTool.ApplicationGetListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		ApplicationListResponseEnvelope response = ApplicationResultParser.ExtractList(callResult);
		ApplicationListItemEnvelope? installedApplication = response.Applications?.FirstOrDefault();
		if (installedApplication is not null) {
			return installedApplication;
		}

		Assert.Ignore("TODO: ENG-88547 add predefined installed application data to the E2E environment.");
		return null!;
	}

	private static string DescribeCallResult(CallToolResult callResult) {
		return JsonSerializer.Serialize(new {
			callResult.IsError,
			callResult.StructuredContent,
			callResult.Content
		});
	}
}
