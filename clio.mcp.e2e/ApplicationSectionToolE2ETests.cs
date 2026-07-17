using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[NonParallelizable]
public sealed class ApplicationSectionToolE2ETests {
	private const string SectionCreateToolName = ApplicationSectionCreateTool.ApplicationSectionCreateToolName;
	private const string SectionDeleteToolName = ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName;
	private const string ApplicationCode = "AutoTestClioMcp";

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Starts the real clio MCP server with an isolated settings file pointing at an unreachable Creatio URI and verifies that create-app-section returns the classified transport error envelope.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create classifies unreachable environments as transport failures")]
	[AllureDescription("Registers an environment whose URI cannot be reached, invokes create-app-section, and verifies the structured error-class/section-created/retry-guidance diagnostic fields from ENG-90679.")]
	public async Task ApplicationSectionCreate_Should_Return_Transport_Classified_Error_For_Unreachable_Environment() {
		// Arrange
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-section-transport-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			"""
			{
			  "ActiveEnvironmentKey": "unreachable-e2e",
			  "Environments": {
			    "unreachable-e2e": {
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
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		try {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				SectionCreateToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = "unreachable-e2e",
						["application-code"] = "UsrMissingApp",
						["caption"] = "Orders",
						["entity-schema-name"] = "UsrOrders"
					}
				},
				cancellationTokenSource.Token);
			ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "classified failures must stay inside the structured payload instead of becoming MCP invocation errors");
			response.Success.Should().BeFalse(
				because: "create-app-section cannot succeed against an unreachable environment");
			response.ErrorClass.Should().Be("transport",
				because: "an unreachable URI means the request never reached Creatio (ENG-90679 classification)");
			response.SectionCreated.Should().Be("false",
				because: "no side effect is possible when the server is unreachable");
			response.RetryGuidance.Should().NotBeNullOrWhiteSpace(
				because: "the agent needs an actionable next step instead of blind retries");
		}
		finally {
			TryDeleteDirectory(tempHome);
		}
	}

	[Test]
	[Description("Starts the real clio MCP server against a stalling endpoint with a 1s response deadline and verifies create-app-section returns the in-progress envelope before the client ceiling instead of a -32001 transport error.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create returns in-progress when the response deadline elapses")]
	[AllureDescription("Points the environment at a TCP endpoint that accepts the connection but never responds, sets CLIO_MCP_RESPONSE_DEADLINE_SECONDS=1, invokes create-app-section, and verifies the response deadline yields error-class=creatio-timeout / section-created=in-progress with poll guidance (ENG-91316) rather than letting the call ride to the client's hard ceiling.")]
	public async Task ApplicationSectionCreate_Should_Return_InProgress_When_Response_Deadline_Elapses() {
		// Arrange — a loopback listener that accepts the TCP connection but never sends a response,
		// so the backend call hangs past the tiny response deadline (a refused port would instead
		// fail fast as a transport error, classified before the deadline).
		using TcpListener stallListener = new(IPAddress.Loopback, 0);
		stallListener.Start();
		int stallPort = ((IPEndPoint)stallListener.LocalEndpoint).Port;
		using CancellationTokenSource acceptCts = new();
		List<TcpClient> heldConnections = new();
		Task acceptLoop = Task.Run(async () => {
			try {
				while (!acceptCts.IsCancellationRequested) {
					TcpClient client = await stallListener.AcceptTcpClientAsync(acceptCts.Token);
					heldConnections.Add(client); // hold the socket open and never write a response
				}
			}
			catch (OperationCanceledException) { /* expected on teardown */ }
			catch (ObjectDisposedException) { /* expected when the listener stops */ }
			catch (SocketException) { /* expected when the listener stops */ }
		});

		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-section-deadline-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		// Fresh clio MCP process reads this at startup, so the static default picks up the 1s override.
		settings.ProcessEnvironmentVariables[McpProgressHeartbeat.ResponseDeadlineOverrideEnvVar] = "1";
		using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			$$"""
			{
			  "ActiveEnvironmentKey": "stalling-e2e",
			  "Environments": {
			    "stalling-e2e": {
			      "Uri": "http://127.0.0.1:{{stallPort}}",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": false
			    }
			  }
			}
			""",
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		try {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				SectionCreateToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = "stalling-e2e",
						["application-code"] = "UsrStallApp",
						["caption"] = "Tasks"
					}
				},
				cancellationTokenSource.Token);
			ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: "a response-deadline timeout must stay a structured payload, never a -32001 MCP invocation error");
			response.Success.Should().BeFalse(
				because: "the section creation did not finish within the response deadline");
			response.ErrorClass.Should().Be("creatio-timeout",
				because: "the deadline path reuses the creatio-timeout class so existing client guidance applies");
			response.SectionCreated.Should().Be("in-progress",
				because: "exceeding the response deadline means the section is still being created server-side, not verification-failed");
			response.RetryGuidance.Should().Contain("list-app-sections",
				because: "the agent must be told to poll the read tools instead of retrying or falling back to create-page");
		}
		finally {
			// Join the accept loop before touching heldConnections: it is the only writer
			// (heldConnections.Add), so awaiting it first turns the subsequent iteration into a
			// single-threaded read and removes the latent List<T> data race.
			await acceptCts.CancelAsync();
			try {
				await acceptLoop;
			}
			catch (OperationCanceledException) { /* expected on teardown */ }
			foreach (TcpClient client in heldConnections) {
				client.Dispose();
			}

			stallListener.Stop();
			TryDeleteDirectory(tempHome);
		}
	}

	[Category("McpE2E.NoEnvironment")]
	[TestCase(null, TestName = "ApplicationSectionCreate_Should_Not_Hang_When_ElicitationCapableClient_Never_Answers(missing icon)")]
	[TestCase("not-a-color", TestName = "ApplicationSectionCreate_Should_Not_Hang_When_ElicitationCapableClient_Never_Answers(invalid icon)")]
	[Description("Starts the real clio MCP server with an elicitation-capable client that never answers and verifies create-app-section returns a structured response promptly instead of hanging to the client ceiling. Covers both a missing icon-background (resolution skipped) and an unrecognized one (resolution must reject without eliciting).")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create does not hang when an elicitation-capable client never answers")]
	[AllureDescription("Connects a client that advertises the elicitation capability but never answers elicitation requests, then calls create-app-section against an unreachable environment with either no icon-background or an unrecognized one. Verifies the tool returns a structured payload promptly rather than blocking on an unanswered elicitation until the client request ceiling.")]
	public async Task ApplicationSectionCreate_Should_Not_Hang_When_ElicitationCapableClient_Never_Answers(string? iconBackground) {
		// Arrange — an elicitation-capable client that NEVER answers. The unreachable URI means no
		// section can be created as a side effect: once icon resolution is bounded, the call falls
		// through to a fast transport failure.
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-section-elicit-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		settings.ProcessEnvironmentVariables[McpProgressHeartbeat.ResponseDeadlineOverrideEnvVar] = "2";
		TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			"""
			{
			  "ActiveEnvironmentKey": "elicit-e2e",
			  "Environments": {
			    "elicit-e2e": {
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

		// Never answer the elicitation: block until the request token is cancelled (the test's
		// safety-net cancellation), mirroring a client that silently drops the prompt.
		Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>> neverAnswers =
			async (_, handlerToken) => {
				await Task.Delay(Timeout.InfiniteTimeSpan, handlerToken);
				return new ElicitResult();
			};

		// Safety net well below the ~180s client ceiling: if the call hangs in an unbounded
		// elicitation it trips this token, and the timing assertion below fails loudly.
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromSeconds(30));
		McpServerSession session = await McpServerSession.StartAsync(
			settings, neverAnswers, cancellationTokenSource.Token);

		try {
			// Act — a missing or unrecognized icon-background is the case that would elicit on an
			// elicitation-capable client; verify it resolves without prompting.
			Stopwatch stopwatch = Stopwatch.StartNew();
			Exception captured = null!;
			CallToolResult callResult = null!;
			Dictionary<string, object?> sectionArgs = new() {
				["environment-name"] = "elicit-e2e",
				["application-code"] = "UsrElicitApp",
				["caption"] = "Tasks"
			};
			if (iconBackground is not null) {
				sectionArgs["icon-background"] = iconBackground;
			}
			try {
				callResult = await session.CallToolAsync(
					SectionCreateToolName,
					new Dictionary<string, object?> { ["args"] = sectionArgs },
					cancellationTokenSource.Token);
			}
			catch (Exception ex) {
				captured = ex;
			}
			stopwatch.Stop();

			// Assert
			captured.Should().BeNull(
				because: "an unanswered elicitation must not make create-app-section hang to the client ceiling; "
					+ $"the call must return a structured response. Elapsed: {stopwatch.Elapsed}. Exception: {captured}");
			if (captured is not null) {
				Assert.Fail($"create-app-section threw unexpectedly after {stopwatch.Elapsed}: {captured}");
			}
			stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(25),
				because: "icon resolution runs before the backend call and must be bounded well below the ~180s client ceiling");
			ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);
			callResult.IsError.Should().NotBeTrue(
				because: "a bounded create-app-section failure must stay a structured payload, never a -32001 MCP invocation error");
			response.Success.Should().BeFalse(
				because: "the section cannot be created against an unreachable environment");
		}
		finally {
			// Dispose in order: stop the clio process, restore the settings file (still under
			// tempHome), then delete tempHome. Deleting first would break the settings restore.
			await session.DisposeAsync();
			settingsOverride.Dispose();
			TryDeleteDirectory(tempHome);
		}
	}

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		}
		catch (IOException) { /* best-effort cleanup: a held handle must never fail the test */ }
		catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
	}

	[Category("McpE2E.Sandbox")]
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
		IReadOnlyCollection<string> reachableToolNames = await session.ListReachableToolNamesAsync(cancellationTokenSource.Token);
		reachableToolNames.Should().Contain(SectionCreateToolName,
			because: "create-app-section must be discoverable via the get-tool-contract compact index before the end-to-end validation calls can run");

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

	[Category("McpE2E.Sandbox")]
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

	[Category("McpE2E.Sandbox")]
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

	[Category("McpE2E.Sandbox")]
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
		await SeededApplicationResolver.ResolveOrIgnoreAsync(
			session, cancellationTokenSource.Token, environmentName!, ApplicationCode);
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

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Creates a section through the progress-capable overload and verifies the client observes the per-phase stage markers 'loading application info', 'creating section', and 'loading created section' (ENG-93087).")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Application section create streams per-phase progress markers")]
	[AllureDescription("Uses the real clio MCP server to call create-app-section with an IProgress sink and asserts the client observed the service-level stage markers 'loading application info', 'creating section', and 'loading created section', proving the per-phase progress path is wired end to end (ENG-93087).")]
	public async Task ApplicationSectionCreate_Should_Stream_PerPhase_Progress_Markers() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false — skipping destructive create-app-section progress-marker test.");
		}

		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to point at the seeded sandbox before running this test.");
		}

		string caption = $"E2E Progress {Guid.NewGuid():N}"[..24];
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		await SeededApplicationResolver.ResolveOrIgnoreAsync(
			session, cancellationTokenSource.Token, environmentName!, ApplicationCode);
		MessageCollectingProgress progress = new();
		string? createdSectionCode = null;
		try {
			// Act — invoke create-app-section through the progress-capable overload so the client observes
			// the service-level stage markers the tool streams as notifications/progress.
			CallToolResult callResult = await session.CallToolAsync(
				SectionCreateToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["application-code"] = ApplicationCode,
						["caption"] = caption
					}
				},
				progress,
				cancellationTokenSource.Token);

			// Diagnostic: surface the exact progress stream the client received so a failure shows the markers.
			foreach (string progressMessage in progress.Messages) {
				TestContext.Out.WriteLine($"[progress] {progressMessage}");
			}

			ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);
			createdSectionCode = response.Section?.Code;

			// Assert
			callResult.IsError.Should().NotBeTrue(
				because: $"a valid create-app-section request should return a structured payload. Actual: {DescribeCallResult(callResult)}");
			progress.Messages.Should().Contain(
				message => message.Contains("loading application info", StringComparison.Ordinal),
				because: "create-app-section must stream the 'loading application info' stage marker so the client can show the app-resolution phase (ENG-93087)");
			progress.Messages.Should().Contain(
				message => message.Contains("creating section", StringComparison.Ordinal),
				because: "create-app-section must stream the 'creating section' stage marker so the client can show the section-creation phase (ENG-93087)");
			progress.Messages.Should().Contain(
				message => message.Contains("loading created section", StringComparison.Ordinal),
				because: "create-app-section must stream the 'loading created section' stage marker so the client can show the readback phase (ENG-93087)");
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

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Creates a section reusing the platform Contact entity in a known installed application and verifies the structured read-back data. Contact is chosen because it ships in every Creatio product (unlike Case, which is absent from a bare Studio deploy and made this test stand-content gated). Covers ENG-88782: Creatio stores Code = EntitySchemaName for platform entity sections; the readback must match by entity schema name, not the caption-derived code sent in the INSERT.")]
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

		const string platformEntitySchemaName = "Contact";
		string caption = $"E2E Contact {Guid.NewGuid():N}"[..23];
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		await SeededApplicationResolver.ResolveOrIgnoreAsync(
			session, cancellationTokenSource.Token, environmentName!, ApplicationCode);
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

	[Category("McpE2E.Sandbox")]
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
			"(?is)(--code|explicit code|no Latin|Latin letters|non-Latin characters)",
			because: "the failure should be actionable: either ask the caller to supply an explicit Latin code, or report that the caption contains non-Latin characters. Which message fires is stand-culture-dependent (a live culture-resolver decides), so the assertion accepts both actionable forms");
	}

	[Category("McpE2E.Sandbox")]
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

	[Category("McpE2E.Sandbox")]
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
		MessageCollectingProgress progress = new();

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

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Fires several create-app-section calls CONCURRENTLY against ONE application through a single clio MCP server and verifies none returns the spurious detail-less 'InsertQuery failed' (error-class=contention) that parallel creation produced before ENG-93089. The in-process serialization guard plus verify+retry recovery must let every section be created. Cleans up all created sections. Long-running (sections are serialized ~90-100s each), destructive, and seeded-env gated — not in CI.")]
	[AllureFeature(SectionCreateToolName)]
	[AllureTag(SectionCreateToolName)]
	[AllureName("Concurrent create-app-section calls against one app do not produce a spurious contention failure")]
	[AllureDescription("Uses the real clio MCP server to fire multiple create-app-section calls at once against the same installed application and verifies that the per-application in-process serialization guard and the contention verify+retry recovery (ENG-93089) prevent the opaque 'InsertQuery failed' rejection, so every section is created exactly once.")]
	public async Task ApplicationSectionCreate_ConcurrentCallsAgainstOneApp_Should_Not_Produce_Contention_Failure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false — skipping destructive concurrent create-app-section test.");
		}

		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to point at the seeded sandbox before running this test.");
		}

		const int concurrentCount = 3;
		string runId = Guid.NewGuid().ToString("N")[..8];
		string[] captions = Enumerable.Range(1, concurrentCount)
			.Select(index => $"E2E Conc {runId} {index}")
			.ToArray();
		// Sections are serialized (~90-100s each), so allow a generous ceiling for the whole batch.
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(15));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		await SeededApplicationResolver.ResolveOrIgnoreAsync(
			session, cancellationTokenSource.Token, environmentName!, ApplicationCode);
		List<string> createdSectionCodes = new();
		try {
			// Act — fire every create-app-section call concurrently against the SAME application, on one
			// long-lived MCP server, exactly reproducing the parallel batch that produced the contention.
			Task<CallToolResult>[] calls = captions
				.Select(caption => session.CallToolAsync(
					SectionCreateToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["application-code"] = ApplicationCode,
							["caption"] = caption
						}
					},
					cancellationTokenSource.Token))
				.ToArray();
			CallToolResult[] results = await Task.WhenAll(calls);

			// Assert
			for (int i = 0; i < results.Length; i++) {
				CallToolResult callResult = results[i];
				callResult.IsError.Should().NotBeTrue(
					because: $"concurrent create-app-section '{captions[i]}' must not surface as an MCP-level error. Actual: {DescribeCallResult(callResult)}");
				ApplicationSectionContextResponseEnvelope response = ApplicationResultParser.ExtractSectionCreate(callResult);
				response.ErrorClass.Should().NotBe("contention",
					because: $"the in-process serialization guard plus verify+retry must prevent a spurious contention failure for '{captions[i]}' (ENG-93089)");
				(response.Error ?? string.Empty).Should().NotContain("InsertQuery failed",
					because: $"concurrent creation against one app must not abort with the opaque 'InsertQuery failed' for '{captions[i]}'");
				response.Success.Should().BeTrue(
					because: $"every serialized concurrent create-app-section must ultimately succeed. Error: {response.Error}");
				if (!string.IsNullOrWhiteSpace(response.Section?.Code)) {
					createdSectionCodes.Add(response.Section!.Code);
				}
			}

			createdSectionCodes.Should().HaveCount(concurrentCount,
				because: "each concurrently-requested section must be created exactly once, with no duplicate and no contention loss");
		} finally {
			foreach (string code in createdSectionCodes) {
				try {
					using CancellationTokenSource cleanupCts = new(TimeSpan.FromMinutes(1));
					await session.CallToolAsync(
						SectionDeleteToolName,
						new Dictionary<string, object?> {
							["args"] = new Dictionary<string, object?> {
								["environment-name"] = environmentName,
								["application-code"] = ApplicationCode,
								["section-code"] = code
							}
						},
						cleanupCts.Token);
				} catch (Exception ex) {
					await Console.Error.WriteLineAsync($"[cleanup] delete-app-section '{code}' failed: {ex.Message}");
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
