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
public sealed class ApplicationSectionToolE2ETests {
	private const string SectionCreateToolName = ApplicationSectionCreateTool.ApplicationSectionCreateToolName;
	private const string ListToolName = ApplicationGetListTool.ApplicationGetListToolName;
	private const string InfoToolName = ApplicationGetInfoTool.ApplicationGetInfoToolName;

	[Test]
	[Description("Advertises create-app-section in the MCP tool list so callers can discover the existing-app section creation tool.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create tool is advertised by the MCP server")]
	[AllureDescription("Starts the real clio MCP server and verifies that create-app-section appears in the advertised tool manifest.")]
	public async Task ApplicationSectionCreate_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(SectionCreateToolName,
			because: "create-app-section must be advertised so MCP callers can discover the existing-app section creation tool");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes create-app-section with an invalid environment, and verifies that the failure remains human-readable.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call create-app-section with an unknown environment name and verifies that the tool returns a structured readable error envelope.")]
	public async Task ApplicationSectionCreate_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string invalidEnvironmentName = $"missing-section-create-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionCreateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["application-code"] = "UsrMissingApp",
					["caption"] = "Orders"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured create-app-section failures should be returned in the payload instead of as MCP invocation errors. Actual result: {JsonSerializer.Serialize(new { callResult.IsError, callResult.StructuredContent, callResult.Content })}");
		response.Success.Should().BeFalse(
			because: "create-app-section should fail when the requested environment does not exist");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes create-app-section without application-code, and verifies that the tool returns a clear validation failure.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create rejects missing application-code")]
	[AllureDescription("Uses the real clio MCP server to call create-app-section without application-code and verifies that the failure explains the required target-app selector.")]
	public async Task ApplicationSectionCreate_Should_Reject_Missing_ApplicationCode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(SectionCreateToolName,
			because: "create-app-section must be advertised before the end-to-end validation calls can run");

		// Act
		CallToolResult missingSelectorCallResult = await session.CallToolAsync(
			SectionCreateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "sandbox",
					["caption"] = "Orders"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionContextResponseEnvelope missingSelectorResponse = ApplicationResultParser.ExtractSectionCreate(missingSelectorCallResult);

		// Assert
		missingSelectorCallResult.IsError.Should().NotBeTrue(
			because: "structured selector validation failures should stay inside the response payload");
		missingSelectorResponse.Success.Should().BeFalse(
			because: "section-create should reject requests that omit application-code");
		missingSelectorResponse.Error.Should().MatchRegex("(?is)(application-code|required)",
			because: "the failure should explain that application-code is required");
	}

	[Test]
	[Description("Starts the real clio MCP server, creates a section in a reachable sandbox environment, and verifies that structured section, entity, and page readback data is returned.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create returns structured readback data")]
	[AllureDescription("Uses the real clio MCP server to discover a reachable sandbox environment and installed application, then calls create-app-section and verifies that the response contains structured section metadata plus page readback data.")]
	public async Task ApplicationSectionCreate_Should_Return_Structured_Readback_Data() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run create-app-section end-to-end tests.");
		}

		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		string environmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName)) {
			ClioCliCommandResult configuredPingResult = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", configuredEnvironmentName]);
			environmentName = configuredPingResult.ExitCode == 0 ? configuredEnvironmentName : string.Empty;
		} else {
			environmentName = string.Empty;
		}

		if (string.IsNullOrWhiteSpace(environmentName)) {
			const string fallbackEnvironmentName = "d2";
			ClioCliCommandResult fallbackPingResult = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", fallbackEnvironmentName]);
			if (fallbackPingResult.ExitCode != 0) {
				Assert.Ignore(
					$"create-app-section MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
			}

			environmentName = fallbackEnvironmentName;
		}

		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(10));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ListToolName,
			because: "list-apps must be available for the end-to-end section-create setup");
		tools.Select(tool => tool.Name).Should().Contain(InfoToolName,
			because: "get-app-info must be available for the end-to-end section-create verification flow");
		tools.Select(tool => tool.Name).Should().Contain(SectionCreateToolName,
			because: "create-app-section must be available for the end-to-end mutation flow");
		CallToolResult listCallResult = await session.CallToolAsync(
			ListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName
				}
			},
			cancellationTokenSource.Token);
		ApplicationListResponseEnvelope listResponse = ApplicationResultParser.ExtractList(listCallResult);
		listResponse.Success.Should().BeTrue(
			because: "the setup step should return a structured application list");
		listResponse.Applications.Should().NotBeEmpty(
			because: "the sandbox environment should expose at least one installed application for section-create testing");
		ApplicationListItemEnvelope targetApplication = listResponse.Applications![0];
		string sectionCaption = $"Codex Section {Guid.NewGuid():N}"[..22];

		// Act
		CallToolResult sectionCreateCallResult = await session.CallToolAsync(
			SectionCreateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = targetApplication.Code,
					["caption"] = sectionCaption,
					["description"] = "Created by MCP E2E"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionContextResponseEnvelope sectionCreateResponse = ApplicationResultParser.ExtractSectionCreate(sectionCreateCallResult);

		// Assert
		sectionCreateCallResult.IsError.Should().NotBeTrue(
			because: $"a valid create-app-section request should return structured readback data. Actual result: {JsonSerializer.Serialize(new { sectionCreateCallResult.IsError, sectionCreateCallResult.StructuredContent, sectionCreateCallResult.Content })}");
		sectionCreateResponse.Success.Should().BeTrue(
			because: "successful section creation should return the standard success envelope");
		sectionCreateResponse.ApplicationCode.Should().Be(targetApplication.Code,
			because: "the create response should preserve the target application code");
		sectionCreateResponse.Section.Should().NotBeNull(
			because: "successful section creation should return section metadata");
		sectionCreateResponse.Section!.Caption.Should().Be(sectionCaption,
			because: "the section readback should preserve the requested section caption");
		sectionCreateResponse.Section.Code.Should().NotBeNullOrWhiteSpace(
			because: "the section readback should include the generated section code");
		sectionCreateResponse.PackageName.Should().NotBeNullOrWhiteSpace(
			because: "the create response should include the target primary package");
		sectionCreateResponse.Pages.Should().NotBeNull(
			because: "the create response should include page readback data even when the created page set is empty");
		sectionCreateResponse.Error.Should().BeNullOrWhiteSpace(
			because: "successful section creation should not return an error payload");
	}
}
