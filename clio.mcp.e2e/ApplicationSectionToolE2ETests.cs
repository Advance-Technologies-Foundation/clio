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
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[NonParallelizable]
public sealed class ApplicationSectionToolE2ETests {
	private const string SectionCreateToolName = ApplicationSectionCreateTool.ApplicationSectionCreateToolName;
	private const string SectionDeleteToolName = ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName;
	private const string ApplicationCode = "AutoTestClioMcp";

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
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
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
					["environment-name"] = environmentName,
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
	[Description("Starts the real clio MCP server, invokes create-app-section without caption, and verifies that the tool returns a clear validation failure.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create rejects missing caption")]
	[AllureDescription("Uses the real clio MCP server to call create-app-section without caption and verifies that the failure explains the required scalar caption contract.")]
	public async Task ApplicationSectionCreate_Should_Reject_Missing_Caption() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionCreateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = "UsrOrdersApp"
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "structured caption validation failures should stay inside the response payload");
		response.Success.Should().BeFalse(
			because: "section-create should reject requests that omit caption");
		response.Error.Should().MatchRegex("(?is)(caption|required)",
			because: "the failure should explain that caption is required");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes create-app-section with forbidden localization maps, and verifies that the tool returns a clear scalar-only validation failure.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create rejects localization maps")]
	[AllureDescription("Uses the real clio MCP server to call create-app-section with title-localizations and verifies that the tool rejects localization-map payloads before any create side effect is attempted.")]
	public async Task ApplicationSectionCreate_Should_Reject_Localization_Map_Fields() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionCreateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = "UsrOrdersApp",
					["caption"] = "Orders",
					["title-localizations"] = new Dictionary<string, object?> {
						["en-US"] = "Orders"
					}
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "structured scalar-only validation failures should stay inside the response payload");
		response.Success.Should().BeFalse(
			because: "section-create should reject localization maps before any remote create side effect is attempted");
		response.Error.Should().MatchRegex("(?is)(scalar-only|title-localizations|localizations)",
			because: "the failure should explain that localization maps are forbidden on create-app-section");
	}

	[Test]
	[Description("Creates a section with a brand-new custom entity in a known installed application and verifies the structured read-back data including the created section metadata.")]
	public async Task ApplicationSectionCreate_WithCustomEntity_Should_Return_Structured_Readback_Data() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false — skipping destructive create-app-section test.");
		}

		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to point at the seeded sandbox before running this test.");
		}

		string caption = $"E2E Custom {Guid.NewGuid():N}"[..24];
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string? createdSectionCode = null;
		try {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				SectionCreateToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["application-code"] = ApplicationCode,
						["caption"] = caption
					}
				},
				cancellationTokenSource.Token);
			ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: $"create-app-section with a custom entity should not throw an MCP-level error. Actual: {DescribeCallResult(callResult)}");
			response.Success.Should().BeTrue(
				because: $"create-app-section without entity-schema-name must succeed and auto-create the entity schema. Error: {response.Error}");
			response.Section.Should().NotBeNull(
				because: "a successful create-app-section must include the created section metadata in the readback");
			response.Section!.Code.Should().NotBeNullOrWhiteSpace(
				because: "the readback must expose the generated section code");
			response.Section.EntitySchemaName.Should().NotBeNullOrWhiteSpace(
				because: "the readback must expose the auto-created entity schema name");

			createdSectionCode = response.Section.Code;
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

	[Test]
	[Description("Creates a section reusing the platform Case entity in a known installed application and verifies the structured read-back data. Covers ENG-88782: Creatio stores Code = EntitySchemaName for platform entity sections; the readback must match by entity schema name, not the caption-derived code sent in the INSERT.")]
	public async Task ApplicationSectionCreate_WithPlatformEntity_Should_Return_Structured_Readback_Data() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false — skipping destructive create-app-section test.");
		}

		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to point at the seeded sandbox before running this test.");
		}

		const string platformEntitySchemaName = "Case";
		string caption = $"E2E Case {Guid.NewGuid():N}"[..23];
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string? createdSectionCode = null;
		try {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				SectionCreateToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["application-code"] = ApplicationCode,
						["caption"] = caption,
						["entity-schema-name"] = platformEntitySchemaName
					}
				},
				cancellationTokenSource.Token);
			ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: $"create-app-section with a platform entity should not throw an MCP-level error. Actual: {DescribeCallResult(callResult)}");
			response.Success.Should().BeTrue(
				because: $"create-app-section with entity-schema-name:{platformEntitySchemaName} must return success:true. " +
					"Creatio stores Code = EntitySchemaName for platform entity sections, so the readback poll must match " +
					$"by entity schema name, not the caption-derived code sent in the INSERT. Error: {response.Error}");
			response.Section.Should().NotBeNull(
				because: "a successful create-app-section must include the created section metadata in the readback");
			response.Section!.EntitySchemaName.Should().Be(platformEntitySchemaName,
				because: "the readback must preserve the platform entity schema name provided in the create request");

			createdSectionCode = response.Section.Code;
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

	[Test]
	[Description("Creates a section with a non-Latin caption and no explicit code, and verifies create-app-section returns an actionable failure that asks for an explicit code instead of the opaque 'InsertQuery failed.' message. Reproduces ENG-91212: a Cyrillic caption (\"Контакти\") produced an invalid non-ASCII section code that Creatio silently rejected.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create reports an actionable failure for a non-Latin caption without an explicit code")]
	[AllureDescription("Uses the real clio MCP server to call create-app-section with a non-Latin caption and no code, and verifies the failure points the caller at an explicit code instead of returning the opaque InsertQuery fallback.")]
	public async Task ApplicationSectionCreate_WithNonLatinCaptionAndNoCode_Should_Report_Actionable_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to point at the seeded sandbox before running this test.");
		}

		const string nonLatinCaption = "Контакти";
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionCreateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = ApplicationCode,
					["caption"] = nonLatinCaption
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured create-app-section failures should stay in the payload, not surface as MCP-level errors. Actual: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "a non-Latin caption cannot produce a valid section code and must fail when no explicit code is supplied");
		response.Error.Should().NotBe("InsertQuery failed.",
			because: "the opaque legacy fallback must be replaced with a diagnostic message");
		response.Error.Should().MatchRegex(
			"(?is)(--code|explicit code|no Latin|Latin letters)",
			because: "the failure should tell the caller to supply an explicit Latin code");
	}

	[Test]
	[Description("Reuses a non-existent entity schema and verifies create-app-section returns a descriptive 'does not exist' failure before any insert, instead of the opaque 'InsertQuery failed.' message. Covers ENG-91212: a missing existing-object target must be reported clearly.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create reports a descriptive failure when the existing object does not exist")]
	[AllureDescription("Uses the real clio MCP server to call create-app-section with an entity-schema-name that does not exist and verifies the failure explains that the object was not found instead of returning the opaque InsertQuery fallback.")]
	public async Task ApplicationSectionCreate_WithNonExistentEntity_Should_Report_Descriptive_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to point at the seeded sandbox before running this test.");
		}

		string missingEntitySchemaName = $"UsrMissing{Guid.NewGuid():N}"[..24];
		string caption = $"E2E Missing {Guid.NewGuid():N}"[..22];
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			SectionCreateToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = ApplicationCode,
					["caption"] = caption,
					["entity-schema-name"] = missingEntitySchemaName
				}
			},
			cancellationTokenSource.Token);
		ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"structured create-app-section failures should stay in the payload, not surface as MCP-level errors. Actual: {DescribeCallResult(callResult)}");
		response.Success.Should().BeFalse(
			because: "create-app-section must fail when the requested existing object does not exist");
		response.Error.Should().NotBe("InsertQuery failed.",
			because: "the opaque legacy fallback must be replaced with a diagnostic message");
		response.Error.Should().MatchRegex(
			$"(?is)(does not exist|{missingEntitySchemaName})",
			because: "the failure should explain that the requested object was not found");
	}

	[Test]
	[Description("Starts the real clio MCP server with a small heartbeat interval and verifies that a long-running application tool streams at least one notifications/progress message, so MCP clients reset their inactivity timeout instead of timing out mid-operation (ENG-91274).")]
	[AllureFeature("mcp-progress-heartbeat")]
	[AllureTag("mcp-progress-heartbeat")]
	[AllureName("Application tools stream progress notifications for long-running calls")]
	[AllureDescription("Forces a tiny CLIO_MCP_HEARTBEAT_INTERVAL_SECONDS, calls list-app-sections through the real clio MCP server with an IProgress sink, and asserts the client observed at least one progress notification — proving the keep-alive path is wired end to end.")]
	public async Task ApplicationTool_Should_Stream_Progress_For_LongRunning_Call() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		// Force a tiny heartbeat interval so a single backend round-trip deterministically yields a beat.
		settings.ProcessEnvironmentVariables[McpProgressHeartbeat.IntervalOverrideEnvVar] = "0.05";
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		CollectingProgress progress = new();

		// Act — list-app-sections is read-only and always performs a backend round-trip, so the
		// heartbeat fires while it works even when the application does not exist.
		CallToolResult callResult = await session.CallToolAsync(
			ApplicationSectionGetListTool.ApplicationSectionGetListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["application-code"] = ApplicationCode
				}
			},
			progress,
			cancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: $"a structured list-app-sections result should not surface as an MCP-level error. Actual: {DescribeCallResult(callResult)}");
		progress.Count.Should().BeGreaterThanOrEqualTo(1,
			because: "a long-running application tool must stream at least one progress notification so the client resets its inactivity timeout instead of timing out");
	}

	/// <summary>
	/// Thread-safe <see cref="IProgress{T}"/> sink that records progress notifications synchronously
	/// as the SDK delivers them, so the count is deterministic by the time the tool call returns.
	/// </summary>
	private sealed class CollectingProgress : IProgress<ProgressNotificationValue> {
		private int _count;

		public int Count => Volatile.Read(ref _count);

		public void Report(ProgressNotificationValue value) => Interlocked.Increment(ref _count);
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
