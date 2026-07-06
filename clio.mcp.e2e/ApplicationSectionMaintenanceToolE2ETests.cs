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
	private const string SectionCreateToolName = ApplicationSectionCreateTool.ApplicationSectionCreateToolName;
	private const string ApplicationCode = "AutoTestClioMcp";

	[Category("McpE2E.Sandbox")]
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

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Starts the real clio MCP server, resolves the seeded installed application AutoTestClioMcp, and verifies that list-app-sections returns a structured section envelope for that application.")]
	[AllureFeature(SectionListToolName)]
	[AllureTag(SectionListToolName)]
	[AllureName("Application section list returns structured section metadata")]
	[AllureDescription("Uses the real clio MCP server to look up the configured seeded installed application via list-apps and verifies that list-app-sections returns the expected structured installed-application section envelope for that application.")]
	public async Task ApplicationSectionGetList_Should_Return_Structured_Section_List() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		ApplicationListItemEnvelope installedApplication = await SeededApplicationResolver.ResolveOrIgnoreAsync(
			session,
			cancellationTokenSource.Token,
			environmentName,
			ApplicationCode);

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
			because: "list-app-sections should succeed for the seeded installed application");
		response.ApplicationId.Should().Be(installedApplication.Id,
			because: "the section list envelope should resolve the same seeded installed application returned by list-apps");
		response.ApplicationCode.Should().Be(installedApplication.Code,
			because: "the section list envelope should preserve the seeded installed application code");
		response.ApplicationName.Should().Be(installedApplication.Name,
			because: "the section list envelope should preserve the seeded installed application name");
		response.Sections.Should().NotBeNull(
			because: "list-app-sections should always include the sections collection so clients can handle empty and populated applications uniformly");
		response.Error.Should().BeNullOrWhiteSpace(
			because: "successful list-app-sections calls should not include an error payload");
	}

	[Category("McpE2E.Sandbox")]
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

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Starts the real clio MCP server, creates a section in the seeded installed application via create-app-section, lists sections to confirm the new section appears, deletes the section via delete-app-section, and verifies that the deleted section is removed from the section list.")]
	[AllureFeature(SectionDeleteToolName)]
	[AllureTag(SectionDeleteToolName)]
	[AllureName("Application section delete removes a created section from the section list")]
	[AllureDescription("Uses the real clio MCP server to drive the full create → list → delete → re-list lifecycle for delete-app-section against the configured seeded application, and verifies that the deleted section no longer appears in the list-app-sections response.")]
	public async Task ApplicationSectionDelete_Should_Remove_Created_Section_From_Section_List() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false — skipping destructive delete-app-section lifecycle test.");
		}

		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run the delete-app-section lifecycle test.");
		}

		string caption = $"E2E Del {Guid.NewGuid():N}"[..24];
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string? createdSectionCode = null;
		try {
			// Act 1: create a new section in the seeded application
			CallToolResult createResult = await session.CallToolAsync(
				SectionCreateToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["application-code"] = ApplicationCode,
						["caption"] = caption
					}
				},
				cancellationTokenSource.Token);
			ApplicationSectionContextResponseEnvelope createResponse = ApplicationResultParser.ExtractSectionCreate(createResult);

			createResult.IsError.Should().NotBeTrue(
				because: $"create-app-section should not throw an MCP-level error. Actual: {DescribeCallResult(createResult)}");
			createResponse.Success.Should().BeTrue(
				because: $"create-app-section must succeed before the delete lifecycle can be verified. Error: {createResponse.Error}");
			createResponse.Section.Should().NotBeNull(
				because: "create-app-section readback must include the created section metadata");
			createResponse.Section!.Code.Should().NotBeNullOrWhiteSpace(
				because: "the readback must expose the created section code");

			createdSectionCode = createResponse.Section.Code;

			// Act 2: list sections and confirm the new section is present
			ApplicationSectionListContextResponseEnvelope listBefore = await CallListSectionsAsync(
				session, cancellationTokenSource.Token, environmentName!, ApplicationCode);

			listBefore.Success.Should().BeTrue(
				because: $"list-app-sections should succeed for the seeded application before deletion. Error: {listBefore.Error}");
			listBefore.Sections.Should().NotBeNull(
				because: "list-app-sections must return the sections collection so the test can assert membership");
			listBefore.Sections!.Should().Contain(section => section.Code == createdSectionCode,
				because: "the section created above should appear in list-app-sections before it is deleted");

			// Act 3: delete the created section
			CallToolResult deleteResult = await session.CallToolAsync(
				SectionDeleteToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["application-code"] = ApplicationCode,
						["section-code"] = createdSectionCode
					}
				},
				cancellationTokenSource.Token);
			ApplicationSectionDeleteContextResponseEnvelope deleteResponse = ApplicationResultParser.ExtractSectionDelete(deleteResult);

			deleteResult.IsError.Should().NotBeTrue(
				because: $"delete-app-section should not throw an MCP-level error. Actual: {DescribeCallResult(deleteResult)}");
			deleteResponse.Success.Should().BeTrue(
				because: $"delete-app-section must succeed for the section created above. Error: {deleteResponse.Error}");
			deleteResponse.DeletedSection.Should().NotBeNull(
				because: "the delete readback must include the deleted section metadata");
			deleteResponse.DeletedSection!.Code.Should().Be(createdSectionCode,
				because: "the delete readback must echo the deleted section code so callers can correlate the deletion");

			// Mark the section as cleaned up so the finally block does not double-delete
			createdSectionCode = null;

			// Act 4: re-list sections and confirm the section is gone
			ApplicationSectionListContextResponseEnvelope listAfter = await CallListSectionsAsync(
				session, cancellationTokenSource.Token, environmentName!, ApplicationCode);

			listAfter.Success.Should().BeTrue(
				because: $"list-app-sections should succeed for the seeded application after deletion. Error: {listAfter.Error}");
			listAfter.Sections.Should().NotBeNull(
				because: "list-app-sections must return the sections collection after deletion so the test can assert removal");
			listAfter.Sections!.Should().NotContain(section => section.Code == deleteResponse.DeletedSection!.Code,
				because: "the deleted section must no longer appear in list-app-sections after delete-app-section succeeds");
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

	private static async Task<ApplicationSectionListContextResponseEnvelope> CallListSectionsAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string applicationCode) {
		CallToolResult callResult = await session.CallToolAsync(
			SectionListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = applicationCode
				}
			},
			cancellationToken);
		return ApplicationResultParser.ExtractSectionList(callResult);
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
