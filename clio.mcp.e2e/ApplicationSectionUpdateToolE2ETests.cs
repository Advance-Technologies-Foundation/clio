using System;
using System.Collections.Generic;
using System.Diagnostics;
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

	[Category("McpE2E.NoEnvironment")]
	[TestCase(null, TestName = "ApplicationSectionUpdate_Should_Not_Hang_When_ElicitationCapableClient_Never_Answers(missing icon)")]
	[TestCase("not-a-color", TestName = "ApplicationSectionUpdate_Should_Not_Hang_When_ElicitationCapableClient_Never_Answers(invalid icon)")]
	[Description("Starts the real clio MCP server with an elicitation-capable client that never answers and verifies update-app-section returns a structured response promptly instead of hanging to the client ceiling. Mirrors the create-app-section regression: an unrecognized icon-background must be rejected without eliciting.")]
	[AllureFeature(SectionUpdateToolName)]
	[AllureTag(SectionUpdateToolName)]
	[AllureName("Application section update does not hang when an elicitation-capable client never answers")]
	[AllureDescription("Connects a client that advertises the elicitation capability but never answers elicitation requests, then calls update-app-section against an unreachable environment with either no icon-background or an unrecognized one. Verifies the tool returns a structured payload promptly rather than blocking on an unanswered elicitation until the client request ceiling.")]
	public async Task ApplicationSectionUpdate_Should_Not_Hang_When_ElicitationCapableClient_Never_Answers(string? iconBackground) {
		// Arrange — an elicitation-capable client that NEVER answers. The unreachable URI means the
		// call falls through to a fast transport failure once icon resolution is bounded.
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-section-update-elicit-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		settings.ProcessEnvironmentVariables[McpProgressHeartbeat.ResponseDeadlineOverrideEnvVar] = "2";
		TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			"""
			{
			  "ActiveEnvironmentKey": "elicit-update-e2e",
			  "Environments": {
			    "elicit-update-e2e": {
			      "Uri": "http://127.0.0.1:9",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": false
			    }
			  }
			}
			""",
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);

		// Never answer the elicitation: block until the request token is cancelled, mirroring a
		// client that silently drops the prompt.
		Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> neverAnswers =
			async (_, handlerToken) => {
				await Task.Delay(Timeout.InfiniteTimeSpan, handlerToken);
				return new ElicitResult();
			};

		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(30));
		McpServerSession session = await McpServerSession.StartAsync(
			settings, neverAnswers, cancellationTokenSource.Token);

		try {
			// Act — an unrecognized icon-background is the case that would elicit on an
			// elicitation-capable client; verify it resolves without prompting.
			Stopwatch stopwatch = Stopwatch.StartNew();
			Exception captured = null!;
			CallToolResult callResult = null!;
			Dictionary<string, object?> sectionArgs = new() {
				["environment-name"] = "elicit-update-e2e",
				["application-code"] = "UsrElicitApp",
				["section-code"] = "UsrElicitSection"
			};
			if (iconBackground is not null) {
				sectionArgs["icon-background"] = iconBackground;
			}
			try {
				callResult = await session.CallToolAsync(
					SectionUpdateToolName,
					new Dictionary<string, object?> { ["args"] = sectionArgs },
					cancellationTokenSource.Token);
			}
			catch (Exception ex) {
				captured = ex;
			}
			stopwatch.Stop();

			// Assert
			captured.Should().BeNull(
				because: "an unanswered elicitation must not make update-app-section hang to the client ceiling; "
					+ $"the call must return a structured response. Elapsed: {stopwatch.Elapsed}. Exception: {captured}");
			if (captured is not null) {
				Assert.Fail($"update-app-section threw unexpectedly after {stopwatch.Elapsed}: {captured}");
			}
			stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(25),
				because: "icon resolution runs before the backend call and must be bounded well below the client request ceiling");
			ApplicationSectionUpdateContextResponseEnvelope response = ApplicationResultParser.ExtractSectionUpdate(callResult);
			callResult.IsError.Should().NotBeTrue(
				because: "a bounded update-app-section failure must stay a structured payload, never a -32001 MCP invocation error");
			response.Success.Should().BeFalse(
				because: "the section cannot be updated against an unreachable environment");
		}
		finally {
			// Dispose in order: stop the clio process, restore the settings file (still under
			// tempHome), then delete tempHome.
			await session.DisposeAsync();
			settingsOverride.Dispose();
			try {
				if (Directory.Exists(tempHome)) {
					Directory.Delete(tempHome, recursive: true);
				}
			}
			catch (IOException) { /* best-effort cleanup */ }
			catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
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
