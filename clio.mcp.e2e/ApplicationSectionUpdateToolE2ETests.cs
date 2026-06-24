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
public sealed class ApplicationSectionUpdateToolE2ETests {
	private const string SectionUpdateToolName = ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName;
	private const string SectionCreateToolName = ApplicationSectionCreateTool.ApplicationSectionCreateToolName;
	private const string SectionDeleteToolName = ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName;
	private const string ApplicationCode = "AutoTestClioMcp";

	[Category("McpE2E.NoEnvironment")]
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

	[Category("McpE2E.NoEnvironment")]
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

	[Category("McpE2E.Sandbox")]
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
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionUpdateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
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

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Starts the real clio MCP server, invokes update-app-section without application-code, and verifies that the tool returns a clear validation failure.")]
	[AllureFeature(SectionUpdateToolName)]
	[AllureTag(SectionUpdateToolName)]
	[AllureName("Application section update rejects missing application-code")]
	[AllureDescription("Uses the real clio MCP server to call update-app-section without application-code and verifies that the failure explains the required app selector.")]
	public async Task ApplicationSectionUpdate_Should_Reject_Missing_ApplicationCode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionUpdateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["section-code"] = "UsrOrders",
					["caption"] = "Orders"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionUpdateContextResponseEnvelope response = ApplicationResultParser.ExtractSectionUpdate(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "structured selector validation failures should stay inside the response payload");
		response.Success.Should().BeFalse(
			because: "section-update should reject requests that omit application-code");
		response.Error.Should().MatchRegex("(?is)(application-code|required)",
			because: "the failure should explain that application-code is required");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Starts the real clio MCP server, invokes update-app-section without section-code, and verifies that the tool returns a clear validation failure.")]
	[AllureFeature(SectionUpdateToolName)]
	[AllureTag(SectionUpdateToolName)]
	[AllureName("Application section update rejects missing section-code")]
	[AllureDescription("Uses the real clio MCP server to call update-app-section without section-code and verifies that the failure explains the required section selector.")]
	public async Task ApplicationSectionUpdate_Should_Reject_Missing_SectionCode() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionUpdateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = "UsrOrdersApp",
					["caption"] = "Orders"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionUpdateContextResponseEnvelope response = ApplicationResultParser.ExtractSectionUpdate(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "structured selector validation failures should stay inside the response payload");
		response.Success.Should().BeFalse(
			because: "section-update should reject requests that omit section-code");
		response.Error.Should().MatchRegex("(?is)(section-code|required)",
			because: "the failure should explain that section-code is required");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Starts the real clio MCP server, invokes update-app-section with forbidden localization maps, and verifies that the tool returns a clear scalar-only validation failure.")]
	[AllureFeature(SectionUpdateToolName)]
	[AllureTag(SectionUpdateToolName)]
	[AllureName("Application section update rejects localization maps")]
	[AllureDescription("Uses the real clio MCP server to call update-app-section with title-localizations and verifies that the tool rejects localization-map payloads before any update side effect is attempted.")]
	public async Task ApplicationSectionUpdate_Should_Reject_Localization_Map_Fields() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionUpdateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = "UsrOrdersApp",
					["section-code"] = "UsrOrders",
					["caption"] = "Orders",
					["title-localizations"] = new Dictionary<string, object?> {
						["en-US"] = "Orders"
					}
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionUpdateContextResponseEnvelope response = ApplicationResultParser.ExtractSectionUpdate(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "structured scalar-only validation failures should stay inside the response payload");
		response.Success.Should().BeFalse(
			because: "section-update should reject localization maps before any remote update side effect is attempted");
		response.Error.Should().MatchRegex("(?is)(scalar-only|title-localizations|localizations)",
			because: "the failure should explain that localization maps are forbidden on update-app-section");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Starts the real clio MCP server, creates a section in the seeded installed application, updates its caption and description via update-app-section, and verifies that the structured before-and-after read-back exposes both the prior and updated values.")]
	[AllureFeature(SectionUpdateToolName)]
	[AllureTag(SectionUpdateToolName)]
	[AllureName("Application section update returns structured before-and-after readback data")]
	[AllureDescription("Uses the real clio MCP server to drive the full create → update → delete lifecycle for update-app-section against the configured seeded application, and verifies that the structured read-back exposes the prior caption/description in previous-section and the new caption/description in section.")]
	public async Task ApplicationSectionUpdate_Should_Return_Structured_Readback_Data() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false — skipping destructive update-app-section lifecycle test.");
		}

		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to point at the seeded sandbox before running this test.");
		}

		string initialCaption = $"E2E UpdBefore {Guid.NewGuid():N}"[..24];
		string updatedCaption = $"E2E UpdAfter {Guid.NewGuid():N}"[..24];
		const string updatedDescription = "E2E update lifecycle";
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string? createdSectionCode = null;
		try {
			// Act 1: create a section with the initial caption
			CallToolResult createResult = await session.CallToolAsync(
				SectionCreateToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["application-code"] = ApplicationCode,
						["caption"] = initialCaption
					}
				},
				cancellationTokenSource.Token);
			ApplicationSectionContextResponseEnvelope createResponse = ApplicationResultParser.ExtractSectionCreate(createResult);

			createResult.IsError.Should().NotBeTrue(
				because: $"create-app-section should not throw an MCP-level error. Actual: {DescribeCallResult(createResult)}");
			createResponse.Success.Should().BeTrue(
				because: $"create-app-section must succeed before the update lifecycle can be verified. Error: {createResponse.Error}");
			createResponse.Section.Should().NotBeNull(
				because: "create-app-section readback must include the created section metadata");
			createResponse.Section!.Code.Should().NotBeNullOrWhiteSpace(
				because: "the readback must expose the created section code so update-app-section can target it");

			createdSectionCode = createResponse.Section.Code;

			// Act 2: update the section's caption and description
			CallToolResult updateResult = await session.CallToolAsync(
				SectionUpdateToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["application-code"] = ApplicationCode,
						["section-code"] = createdSectionCode,
						["caption"] = updatedCaption,
						["description"] = updatedDescription
					}
				},
				cancellationTokenSource.Token);
			ApplicationSectionUpdateContextResponseEnvelope updateResponse = ApplicationResultParser.ExtractSectionUpdate(updateResult);

			// Assert
			updateResult.IsError.Should().NotBeTrue(
				because: $"update-app-section should not throw an MCP-level error. Actual: {DescribeCallResult(updateResult)}");
			updateResponse.Success.Should().BeTrue(
				because: $"update-app-section must succeed for the freshly created section. Error: {updateResponse.Error}");
			updateResponse.PreviousSection.Should().NotBeNull(
				because: "update-app-section must include the pre-update section snapshot so callers can diff before-and-after");
			updateResponse.PreviousSection!.Code.Should().Be(createdSectionCode,
				because: "the previous-section snapshot must identify the same section that was updated");
			updateResponse.PreviousSection.Caption.Should().Be(initialCaption,
				because: "the previous-section snapshot must preserve the caption that existed before update-app-section was invoked");
			updateResponse.Section.Should().NotBeNull(
				because: "update-app-section must include the post-update section state for the caller to confirm the new values landed");
			updateResponse.Section!.Code.Should().Be(createdSectionCode,
				because: "the post-update section must report the same code as the previous-section snapshot");
			updateResponse.Section.Caption.Should().Be(updatedCaption,
				because: "the post-update section must reflect the new caption that update-app-section was asked to apply");
			updateResponse.Section.Description.Should().Be(updatedDescription,
				because: "the post-update section must reflect the new description that update-app-section was asked to apply");
		} finally {
			if (!string.IsNullOrWhiteSpace(createdSectionCode)) {
				try {
					using CancellationTokenSource cleanupCts = new(TimeSpan.FromMinutes(1));
					await session.CallToolAsync(
						SectionDeleteToolName,
						new Dictionary<string, object?> {
							["args"] = new Dictionary<string, object?> {
								["environment-name"] = environmentName,
								["application-code"] = ApplicationCode,
								["section-code"] = createdSectionCode
							}
						},
						cleanupCts.Token);
				} catch (Exception ex) {
					await Console.Error.WriteLineAsync($"[cleanup] delete-app-section '{createdSectionCode}' failed: {ex.Message}");
				}
			}
		}
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

	private static string DescribeCallResult(CallToolResult callResult) {
		return JsonSerializer.Serialize(new {
			callResult.IsError,
			callResult.StructuredContent,
			callResult.Content
		});
	}
}
