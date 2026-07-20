using System.Text.Json;
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
/// Story 15d (ENG-93208, FR-10 / AC-05) no-regression e2e: with per-request credential passthrough
/// unused, <c>clio mcp</c> (stdio) and <c>clio mcp-http -e &lt;env&gt;</c> on loopback with NO platform
/// API key must behave exactly as the pre-passthrough 8.1.0.72 build. Treated as a core contract, not a
/// mere test.
/// <para>
/// MANUAL — NOT in CI (<c>[Category("E2E")]</c>). The stdio leg needs only a clio build; the
/// <c>mcp-http -e &lt;env&gt;</c> leg needs a pre-registered clio environment (skipped via
/// <see cref="Assert.Ignore(string)"/> when <c>CLIO_MCP_HTTP_E2E_REGISTERED_ENV</c> is absent).
/// </para>
/// </summary>
[TestFixture]
[Category("E2E")]
[NonParallelizable]
public sealed class McpHttpNoRegressionE2ETests {

	private McpE2ESettings _settings = null!;

	[SetUp]
	public void SetUp() {
		_settings = TestConfiguration.Load();
	}

	[Test]
	[Description("The stdio clio MCP server starts and advertises its resident tools exactly as the pre-passthrough build, proving the passthrough work did not regress the stdio transport (Story 15d / AC-05).")]
	public async Task Stdio_ShouldAdvertiseResidentTools_WhenPassthroughUnused() {
		// Arrange
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(_settings, cts.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cts.Token);

		// Assert
		tools.Should().NotBeEmpty(
			because: "the stdio MCP server must still advertise its resident tools unchanged by the passthrough work (Story 15d)");
	}

	[Test]
	[Category("McpE2E.Manual")]
	[Description("clio mcp-http bound to loopback with NO platform API key serves a pre-registered environment via -e-style resolution exactly as the pre-passthrough build, ignoring any credential header (Story 15d / AC-05).")]
	public async Task HttpWithRegisteredEnvironment_ShouldServeEnvironment_WhenNoPlatformApiKeyConfigured() {
		// Arrange
		string? registeredEnvironment = Environment.GetEnvironmentVariable("CLIO_MCP_HTTP_E2E_REGISTERED_ENV");
		if (string.IsNullOrWhiteSpace(registeredEnvironment)) {
			Assert.Ignore(
				"Set CLIO_MCP_HTTP_E2E_REGISTERED_ENV to a pre-registered clio environment name to run the "
				+ "mcp-http no-regression leg against a live stand (MANUAL, not in CI).");
		}

		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		// No platform API key → passthrough is fully disabled; the server behaves as pre-passthrough.
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, platformApiKey: null, cts.Token);
		await using McpClient client =
			await server.ConnectAsync(platformApiKey: null, integrationCredentialsBase64: null, cts.Token);

		// Act — resolve by the registered environment name, exactly as the pre-passthrough -e path.
		// describe-environment is a long-tail tool (absent from lazy-mode tools/list, ENG-90312/92761);
		// reach it via clio-run, exactly as a real client would.
		CallToolResult result = await client.CallToolAsync(
			ClioRunTool.ToolName,
			new Dictionary<string, object?> {
				["command"] = GetCreatioInfoTool.ToolName,
				["args"] = new Dictionary<string, object?> { ["environment-name"] = registeredEnvironment }
			},
			cancellationToken: cts.Token);

		// Assert
		result.IsError.Should().NotBeTrue(
			because: "a registered environment must be served over mcp-http with no api key exactly as the pre-passthrough build (Story 15d)");
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(result);
		envelope.ExitCode.Should().Be(0,
			because: "describe-environment against a registered environment must succeed unchanged by the passthrough work (Story 15d)");
	}

	// ---------------------------------------------------------------------------------------------
	// Story 15 AC-03 (ENG-93347, PRD AC-09 / SM-03): no-regression sweep for every one of the 15
	// touched tools (the 12 newly routed tools + the 3 link-from-repository-* tools) — this is also
	// the layer that proves the [Required] relaxations (Stories 1, 3-9, 12) did not break an existing
	// registered-env caller.
	//
	// The stdio leg needs only a locally built clio (no live stand) and is asserted directly — no
	// Assert.Ignore — mirroring Stdio_ShouldAdvertiseResidentTools above. The mcp-http -e <env> leg
	// needs a pre-registered clio environment and is gated exactly like
	// HttpWithRegisteredEnvironment_ShouldServeEnvironment_WhenNoPlatformApiKeyConfigured above.
	// ---------------------------------------------------------------------------------------------

	private static readonly string[] TouchedToolNames = [
		ApplicationGetListTool.ApplicationGetListToolName,
		ApplicationGetInfoTool.ApplicationGetInfoToolName,
		ApplicationCreateTool.ApplicationCreateToolName,
		ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
		ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName,
		ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName,
		ApplicationSectionGetListTool.ApplicationSectionGetListToolName,
		GetUserCultureTool.ToolName,
		PageUpdateTool.ToolName,
		PageSyncTool.ToolName,
		ComponentInfoTool.ToolName,
		BuildThemeTool.ToolName,
		LinkFromRepositoryTool.LinkFromRepositoryByEnvironmentToolName,
		LinkFromRepositoryTool.LinkFromRepositoryByEnvPackagePathToolName,
		LinkFromRepositoryTool.LinkFromRepositoryUnlockedToolName
	];

	// The subset of TouchedToolNames that is resident in lazy-mode tools/list (ENG-90312/92761) and
	// therefore directly callable by name. Every other tool in the set is long-tail and must be reached
	// via clio-run, exactly as a real client would — calling a long-tail tool by name returns
	// "Unknown tool", not a business-level outcome, which would otherwise silently pass this leg's
	// deliberately narrow assertion (it checks only for the ABSENCE of the passthrough-rejection text).
	private static readonly HashSet<string> ResidentToolNames = new(StringComparer.Ordinal) {
		ApplicationGetListTool.ApplicationGetListToolName,
		ApplicationGetInfoTool.ApplicationGetInfoToolName,
		ApplicationSectionGetListTool.ApplicationSectionGetListToolName,
		ComponentInfoTool.ToolName
	};

	[Test]
	[Description("Every one of the 15 touched tools (7 c1 + get-user-culture + 3 link-from-repository-* + 4 matrix tools) remains reachable via the stdio clio MCP server exactly as the pre-passthrough build, proving the [Required] relaxations did not regress stdio tool discovery (Story 15 AC-03; PRD AC-09/SM-03).")]
	public async Task Stdio_ShouldExposeTouchedTool_WhenPassthroughUnused(
		[ValueSource(nameof(TouchedToolNames))] string toolName) {
		// Arrange
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(_settings, cts.Token);

		// Act
		IReadOnlyCollection<string> reachableToolNames = await session.ListReachableToolNamesAsync(cts.Token);

		// Assert
		reachableToolNames.Should().Contain(toolName,
			because: $"'{toolName}' must remain reachable via the stdio clio MCP server exactly as the pre-passthrough build (Story 15 AC-03)");
	}

	[Test]
	[Category("McpE2E.Manual")]
	[Description("Every one of the 15 touched tools still executes against a registered environment over mcp-http (no platform API key, environment-name supplied) without hitting the credential-passthrough mixed-input rejection, proving environment-name selection is unregressed for a registered-env caller (Story 15 AC-03; PRD AC-09/SM-03).")]
	public async Task HttpWithRegisteredEnvironment_ShouldExecuteTouchedTool_WhenEnvironmentNameSupplied(
		[ValueSource(nameof(TouchedToolNames))] string toolName) {
		// Arrange
		string? registeredEnvironment = Environment.GetEnvironmentVariable("CLIO_MCP_HTTP_E2E_REGISTERED_ENV");
		if (string.IsNullOrWhiteSpace(registeredEnvironment)) {
			Assert.Ignore(
				"Set CLIO_MCP_HTTP_E2E_REGISTERED_ENV to a pre-registered clio environment name to run the "
				+ "mcp-http no-regression leg against a live stand (MANUAL, not in CI).");
		}
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		// No platform API key → passthrough is fully disabled; the server behaves as pre-passthrough.
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, platformApiKey: null, cts.Token);
		await using McpClient client =
			await server.ConnectAsync(platformApiKey: null, integrationCredentialsBase64: null, cts.Token);

		// Act — long-tail tools (absent from lazy-mode tools/list) are dispatched via clio-run; resident
		// tools are called directly, exactly as a real client would.
		IReadOnlyDictionary<string, object?> callArguments = BuildTouchedToolCallArguments(toolName, registeredEnvironment!);
		CallToolResult callResult = ResidentToolNames.Contains(toolName)
			? await client.CallToolAsync(toolName, callArguments, cancellationToken: cts.Token)
			: await client.CallToolAsync(
				ClioRunTool.ToolName,
				new Dictionary<string, object?> { ["command"] = toolName, ["args"] = callArguments },
				cancellationToken: cts.Token);
		string rawResponse = ExtractRawResponseJson(callResult);

		// Assert — the [Required] relaxation must not have broken a registered-env caller: the
		// credential-passthrough mixed-input rejection text may never appear here (no passthrough
		// context exists on this connection at all — no platform API key, no X-Integration-Credentials
		// header), which is exactly what would indicate environment-name selection stopped being
		// honored. A tool-specific business-level failure (unknown schema/app/package) is an
		// acceptable, orthogonal outcome — this assertion is scoped to the passthrough regression only.
		rawResponse.Should().NotContain("not accepted when credential passthrough is enabled",
			because: $"'{toolName}' must still execute against the registered environment exactly as the pre-passthrough build when environment-name is supplied (Story 15 AC-03)");
	}

	// Builds the minimal, tool-appropriate call arguments for the no-regression sweep. Values are
	// deliberately throwaway/non-existent placeholders where the tool would otherwise require live
	// business data (application/section/schema names) — this leg proves environment-name selection
	// is unregressed, not that the underlying business operation succeeds (see class remarks above).
	private static IReadOnlyDictionary<string, object?> BuildTouchedToolCallArguments(string toolName, string environmentName) {
		string probeId = Guid.NewGuid().ToString("N")[..8];
		string marker = $"e2enoreg{probeId}";
		if (toolName == LinkFromRepositoryTool.LinkFromRepositoryByEnvironmentToolName) {
			return new Dictionary<string, object?> {
				["repoPath"] = Path.Combine(Path.GetTempPath(), $"clio-e2e-noregression-repo-{probeId}"),
				["packages"] = "*",
				["environmentName"] = environmentName,
				["dryRun"] = true
			};
		}
		if (toolName == LinkFromRepositoryTool.LinkFromRepositoryByEnvPackagePathToolName) {
			// This tool selects its target by envPkgPath, not environment-name — skip-preparation=true
			// keeps the call local-only (no Creatio call at all), which is sufficient to prove its own
			// argument binding is unregressed; the passthrough-mixed-input regression this leg targets
			// does not apply to a call that never reaches Creatio.
			return new Dictionary<string, object?> {
				["envPkgPath"] = Path.Combine(Path.GetTempPath(), $"clio-e2e-noregression-envpkg-{probeId}"),
				["repoPath"] = Path.Combine(Path.GetTempPath(), $"clio-e2e-noregression-repo-{probeId}"),
				["packages"] = "*",
				["skipPreparation"] = true,
				["dryRun"] = true
			};
		}
		if (toolName == LinkFromRepositoryTool.LinkFromRepositoryUnlockedToolName) {
			return new Dictionary<string, object?> {
				["repoPath"] = Path.Combine(Path.GetTempPath(), $"clio-e2e-noregression-repo-{probeId}"),
				["environmentName"] = environmentName,
				["dryRun"] = true
			};
		}
		Dictionary<string, object?> innerArgs = new() { ["environment-name"] = environmentName };
		if (toolName == ApplicationGetInfoTool.ApplicationGetInfoToolName) {
			innerArgs["code"] = marker;
		}
		else if (toolName == ApplicationCreateTool.ApplicationCreateToolName) {
			innerArgs["name"] = $"E2E NoRegression Probe {marker}";
			innerArgs["code"] = marker;
			innerArgs["with-mobile-pages"] = false;
		}
		else if (toolName == ApplicationSectionCreateTool.ApplicationSectionCreateToolName) {
			innerArgs["application-code"] = marker;
			innerArgs["caption"] = $"E2E NoRegression Probe {marker}";
			innerArgs["with-mobile-pages"] = false;
		}
		else if (toolName == ApplicationSectionUpdateTool.ApplicationSectionUpdateToolName) {
			innerArgs["application-code"] = marker;
			innerArgs["section-code"] = marker;
			innerArgs["caption"] = $"E2E NoRegression Probe {marker}";
		}
		else if (toolName == ApplicationSectionDeleteTool.ApplicationSectionDeleteToolName) {
			innerArgs["application-code"] = marker;
			innerArgs["section-code"] = marker;
		}
		else if (toolName == ApplicationSectionGetListTool.ApplicationSectionGetListToolName) {
			innerArgs["application-code"] = marker;
		}
		else if (toolName == PageUpdateTool.ToolName) {
			innerArgs["schema-name"] = $"Usr{marker}_FormPage";
			innerArgs["body"] = "({});";
			innerArgs["dry-run"] = true;
		}
		else if (toolName == PageSyncTool.ToolName) {
			innerArgs["pages"] = new List<object?> {
				new Dictionary<string, object?> {
					["schema-name"] = $"Usr{marker}_FormPage",
					["body"] = "({});"
				}
			};
		}
		else if (toolName == ComponentInfoTool.ToolName) {
			innerArgs["component-type"] = "list";
		}
		else if (toolName == BuildThemeTool.ToolName) {
			innerArgs["primary"] = "#0058EF";
		}
		// list-apps and get-user-culture need no field beyond environment-name.
		return new Dictionary<string, object?> { ["args"] = innerArgs };
	}

	// Concatenates BOTH the structured and the plain-content channels of a CallToolResult into one
	// JSON string for a simple substring containment check — same simplified helper as
	// McpHttpConcurrencyIsolationE2ETests.ExtractRawResponseJson, duplicated locally because the two
	// fixtures are independent, self-contained test classes.
	private static string ExtractRawResponseJson(CallToolResult callResult) {
		string structured = callResult.StructuredContent is not null
			? JsonSerializer.Serialize(callResult.StructuredContent)
			: string.Empty;
		string content = JsonSerializer.Serialize(callResult.Content ?? []);
		return structured + "\n" + content;
	}
}
