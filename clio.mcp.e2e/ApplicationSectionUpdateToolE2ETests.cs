using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
public sealed class ApplicationSectionUpdateToolE2ETests {
	private const string SectionCreateToolName = ApplicationSectionCreateTool.ApplicationSectionCreateToolName;
	private const string SectionUpdateToolName = ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName;
	private const string ListToolName = ApplicationGetListTool.ApplicationGetListToolName;
	private const string InfoToolName = ApplicationGetInfoTool.ApplicationGetInfoToolName;

	[Test]
	[Description("Advertises update-app-section in the MCP tool list so callers can discover the existing-section update tool.")]
	[AllureFeature(SectionUpdateToolName)]
	[AllureTag(SectionUpdateToolName)]
	[AllureName("Application section update tool is advertised by the MCP server")]
	[AllureDescription("Starts the real clio MCP server and verifies that update-app-section appears in the advertised tool manifest.")]
	public async Task ApplicationSectionUpdate_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(tool => tool.Name);

		// Assert
		toolNames.Should().Contain(SectionUpdateToolName,
			because: "update-app-section must be advertised so MCP callers can discover the existing-section update tool");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes update-app-section with an invalid environment, and verifies that the failure remains human-readable.")]
	[AllureFeature(SectionUpdateToolName)]
	[AllureTag(SectionUpdateToolName)]
	[AllureName("Application section update reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call update-app-section with an unknown environment name and verifies that the tool returns a structured readable error envelope.")]
	public async Task ApplicationSectionUpdate_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string invalidEnvironmentName = $"missing-section-update-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionUpdateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["application-code"] = "UsrMissingApp",
					["section-code"] = "UsrMissingSection",
					["caption"] = "Orders"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionUpdateContextResponseEnvelope response = ApplicationResultParser.ExtractSectionUpdate(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured update-app-section failures should be returned in the payload instead of as MCP invocation errors. Actual result: {JsonSerializer.Serialize(new { callResult.IsError, callResult.StructuredContent, callResult.Content })}");
		response.Success.Should().BeFalse(
			because: "update-app-section should fail when the requested environment does not exist");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should explain that the requested environment is missing");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes update-app-section without mutable fields, and verifies that the tool returns a clear validation failure.")]
	[AllureFeature(SectionUpdateToolName)]
	[AllureTag(SectionUpdateToolName)]
	[AllureName("Application section update rejects empty partial updates")]
	[AllureDescription("Uses the real clio MCP server to call update-app-section without any mutable fields and verifies that the failure explains the partial-update contract.")]
	public async Task ApplicationSectionUpdate_Should_Reject_Request_Without_Mutable_Fields() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionUpdateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "sandbox",
					["application-code"] = "UsrOrdersApp",
					["section-code"] = "UsrOrders"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionUpdateContextResponseEnvelope response = ApplicationResultParser.ExtractSectionUpdate(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "structured partial-update validation failures should stay inside the response payload");
		response.Success.Should().BeFalse(
			because: "section-update should reject requests that omit all mutable fields");
		response.Error.Should().MatchRegex("(?is)(at least one mutable field|caption|description|icon-id|icon-background)",
			because: "the failure should explain that section-update needs at least one field to change");
	}

	[Test]
	[Description("Starts the real clio MCP server, creates a temporary section, updates its broken heading caption, then updates its icon metadata without touching caption or description.")]
	[AllureFeature(SectionUpdateToolName)]
	[AllureTag(SectionUpdateToolName)]
	[AllureName("Application section update returns structured before-and-after readback data")]
	[AllureDescription("Uses the real clio MCP server to discover a reachable sandbox environment and installed application, creates a temporary section, then verifies caption repair and icon-only updates through update-app-section.")]
	public async Task ApplicationSectionUpdate_Should_Return_Structured_Readback_Data() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run update-app-section end-to-end tests.");
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
					$"update-app-section MCP E2E requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
			}

			environmentName = fallbackEnvironmentName;
		}

		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(10));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ListToolName,
			because: "list-apps must be available for the end-to-end section-update setup");
		tools.Select(tool => tool.Name).Should().Contain(InfoToolName,
			because: "get-app-info must be available for the end-to-end section-update flow");
		tools.Select(tool => tool.Name).Should().Contain(SectionCreateToolName,
			because: "create-app-section must be available for destructive section-update setup");
		tools.Select(tool => tool.Name).Should().Contain(SectionUpdateToolName,
			because: "update-app-section must be available for the end-to-end mutation flow");
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
			because: "the sandbox environment should expose at least one installed application for section-update testing");
		ApplicationListItemEnvelope targetApplication = listResponse.Applications![0];
		string createCaption = $"Codex Repair {Guid.NewGuid():N}"[..22];
		string[] words = Regex.Split(createCaption.Trim(), @"[^\p{L}\p{Nd}]+")
			.Where(item => !string.IsNullOrWhiteSpace(item))
			.ToArray();
		StringBuilder sectionCodeBuilder = new("Usr");
		foreach (string word in words) {
			string sanitizedValue = new(word.Where(char.IsLetterOrDigit).ToArray());
			if (string.IsNullOrWhiteSpace(sanitizedValue)) {
				continue;
			}

			sectionCodeBuilder.Append(sanitizedValue.Length == 1
				? sanitizedValue.ToUpperInvariant()
				: char.ToUpperInvariant(sanitizedValue[0]) + sanitizedValue[1..]);
		}

		if (char.IsDigit(sectionCodeBuilder[3])) {
			sectionCodeBuilder.Insert(3, "_");
		}

		string sectionCode = sectionCodeBuilder.ToString();
		CallToolResult sectionCreateCallResult = await session.CallToolAsync(
			SectionCreateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = targetApplication.Code,
					["caption"] = createCaption,
					["description"] = "Created by MCP E2E"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionContextResponseEnvelope sectionCreateResponse = ApplicationResultParser.ExtractSectionCreate(sectionCreateCallResult);
		sectionCreateResponse.Success.Should().BeTrue(
			because: "the destructive setup step should create a temporary section before the update flow starts");
		string repairedCaption = $"Updated {Guid.NewGuid():N}"[..20];

		// Act
		CallToolResult sectionUpdateCallResult = await session.CallToolAsync(
			SectionUpdateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = targetApplication.Code,
					["section-code"] = sectionCode,
					["caption"] = repairedCaption
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionUpdateContextResponseEnvelope sectionUpdateResponse = ApplicationResultParser.ExtractSectionUpdate(sectionUpdateCallResult);
		CallToolResult iconOnlyUpdateCallResult = await session.CallToolAsync(
			SectionUpdateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = targetApplication.Code,
					["section-code"] = sectionCode,
					["icon-background"] = "#123456"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionUpdateContextResponseEnvelope iconOnlyUpdateResponse = ApplicationResultParser.ExtractSectionUpdate(iconOnlyUpdateCallResult);

		// Assert
		sectionUpdateCallResult.IsError.Should().NotBeTrue(
			because: $"a valid update-app-section request should return structured before-and-after readback data. Actual result: {JsonSerializer.Serialize(new { sectionUpdateCallResult.IsError, sectionUpdateCallResult.StructuredContent, sectionUpdateCallResult.Content })}");
		sectionUpdateResponse.Success.Should().BeTrue(
			because: "successful section-update should return the standard success envelope");
		sectionUpdateResponse.ApplicationCode.Should().Be(targetApplication.Code,
			because: "the update response should preserve the target application code");
		sectionUpdateResponse.PreviousSection.Should().NotBeNull(
			because: "successful section-update should return the original section metadata");
		sectionUpdateResponse.Section.Should().NotBeNull(
			because: "successful section-update should return the updated section metadata");
		sectionUpdateResponse.PreviousSection!.Code.Should().Be(sectionCode,
			because: "the update response should identify the targeted section");
		sectionUpdateResponse.Section!.Caption.Should().Be(repairedCaption,
			because: "section-update should persist the new plain-text caption");
		iconOnlyUpdateCallResult.IsError.Should().NotBeTrue(
			because: "icon-only section updates should also return structured readback data");
		iconOnlyUpdateResponse.Success.Should().BeTrue(
			because: "icon-only section updates should succeed with the same target selectors");
		iconOnlyUpdateResponse.PreviousSection.Should().NotBeNull(
			because: "the icon-only update should return the section metadata before the icon change");
		iconOnlyUpdateResponse.Section.Should().NotBeNull(
			because: "the icon-only update should return the section metadata after the icon change");
		iconOnlyUpdateResponse.PreviousSection!.Caption.Should().Be(repairedCaption,
			because: "icon-only updates should preserve the repaired caption");
		iconOnlyUpdateResponse.Section!.Caption.Should().Be(repairedCaption,
			because: "icon-only updates should not touch the section caption");
		iconOnlyUpdateResponse.Section.IconBackground.Should().Be("#123456",
			because: "icon-only section updates should persist the new icon background");
	}
}
