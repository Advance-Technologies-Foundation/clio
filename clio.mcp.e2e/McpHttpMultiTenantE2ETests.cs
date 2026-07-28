using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Story 15 AC-07 / SM-01 (ENG-93208) multi-tenant e2e. A SINGLE <c>clio mcp-http</c> process with ZERO
/// pre-registered environments serves tool calls against two distinct Creatio URLs/users in one run,
/// using ONLY per-request <c>X-Integration-Credentials</c> headers, and both succeed.
/// <para>
/// MANUAL — NOT in CI. Needs a live stand with two distinct tenants and a clio mcp-http process
/// started with <c>--platform-api-key</c> (the sole passthrough gate); skipped via
/// <see cref="McpHttpPassthroughStand.RequireOrIgnore"/> when the live-stand env vars are absent.
/// </para>
/// </summary>
[TestFixture]
[Category("E2E")]
[Category("McpE2E.Manual")]
[NonParallelizable]
public sealed class McpHttpMultiTenantE2ETests {

	private McpE2ESettings _settings = null!;

	[SetUp]
	public void SetUp() {
		_settings = TestConfiguration.Load();
	}

	[Test]
	[Description("One mcp-http process with no pre-registered environments serves two distinct tenants in a single run via only X-Integration-Credentials, and both calls succeed (SM-01 / AC-07).")]
	public async Task SingleProcess_ShouldServeTwoTenants_WhenOnlyPerRequestCredentialsAreUsed() {
		// Arrange
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);

		// Act — sequential calls in ONE run, each authenticating purely by header (no -e, no registered env).
		CallToolResult tenantOne = await CallDescribeEnvironmentAsync(
			server, stand.PlatformApiKey, stand.TenantOneCredentialsBase64, cts.Token);
		CallToolResult tenantTwo = await CallDescribeEnvironmentAsync(
			server, stand.PlatformApiKey, stand.TenantTwoCredentialsBase64, cts.Token);

		// Assert
		tenantOne.IsError.Should().NotBeTrue(
			because: "the first tenant must be served from its per-request credentials with no pre-registered environment (SM-01)");
		tenantTwo.IsError.Should().NotBeTrue(
			because: "the second, distinct tenant must be served from its per-request credentials in the same process run (SM-01)");
		// MANUAL-RUNNER FINDING (live-stand run, ENG-93347): describe-environment's response does NOT
		// echo the target host/URL — it reports a rich environment-metadata JSON (coreVersion,
		// workspace/user/userAccount GUIDs, db/framework info) with no URL field. Two distinct live
		// tenants matched on every seed-data GUID (workspace/user/userAccount are identical default seed
		// values from the same base image) but genuinely differed on coreVersion, so that is the
		// discriminator used here instead of the originally assumed host. If both tenants are ever
		// upgraded to the same coreVersion, pick a different differing field from a fresh live response.
		string tenantOneText = ExtractText(tenantOne);
		string tenantTwoText = ExtractText(tenantTwo);
		tenantOneText.Should().NotBe(tenantTwoText,
			because: "two distinct tenants must not produce byte-identical describe-environment responses");
		tenantOneText.Should().Contain("\"coreVersion\"",
			because: "the first response must report environment metadata, not an error or empty payload");
		tenantTwoText.Should().Contain("\"coreVersion\"",
			because: "the second response must report environment metadata, not an error or empty payload");
	}

	private static async Task<CallToolResult> CallDescribeEnvironmentAsync(
		McpHttpServerSession server, string platformApiKey, string credentialsBase64, CancellationToken cancellationToken) {
		await using McpClient client = await server.ConnectAsync(
			platformApiKey,
			credentialsBase64,
			cancellationToken);
		// describe-environment is a long-tail tool (absent from lazy-mode tools/list, ENG-90312/92761) —
		// it must be reached via clio-run, exactly as a real client would; a direct tools/call by name
		// returns "Unknown tool" here.
		return await CallViaClioRunAsync(client, GetCreatioInfoTool.ToolName,
			new Dictionary<string, object?>(), cancellationToken);
	}

	// Dispatches to a long-tail tool (hidden from lazy-mode tools/list) via clio-run, the same path a
	// real client must use. `toolArguments` is exactly what would have been sent as the direct
	// tools/call arguments payload — clio-run forwards its own "args" verbatim to the target tool's
	// SDK-native binding, so this preserves each tool's existing wrapped-record vs. flat-parameter shape
	// unchanged.
	private static async Task<CallToolResult> CallViaClioRunAsync(
		McpClient client, string toolName, Dictionary<string, object?> toolArguments, CancellationToken cancellationToken) =>
		await client.CallToolAsync(
			ClioRunTool.ToolName,
			new Dictionary<string, object?> { ["command"] = toolName, ["args"] = toolArguments },
			cancellationToken: cancellationToken);

	private static string ExtractText(CallToolResult callResult) {
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(callResult);
		return string.Join("\n", (envelope.Output ?? []).Select(message => message.Value ?? string.Empty));
	}

	// ---------------------------------------------------------------------------------------------
	// Story 15 AC-01 (ENG-93347, PRD FR-08 / ADR test-strategy rows 8-9): mandatory multi-tenant
	// cases for the four named targets. Each covers BOTH input shapes — header-only (executes
	// against the header tenant) and header + environment-name (mixed input, rejected before any
	// named-tenant lookup, PRD AC-06). All four share the single tenant-one connection pattern below;
	// a second tenant is not required here (that is AC-02's concern, in
	// McpHttpConcurrencyIsolationE2ETests).
	// ---------------------------------------------------------------------------------------------

	private const string MixedInputRejectionMarker = "not accepted when credential passthrough is enabled";

	[Test]
	[Description("A header-only list-apps call executes against the header tenant with no pre-registered environment (Story 15 AC-01; PRD FR-08).")]
	public async Task ApplicationGetList_ShouldExecuteAgainstHeaderTenant_WhenHeaderOnly() {
		// Arrange
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);
		await using McpClient client = await ConnectTenantOneAsync(server, stand, cts.Token);

		// Act
		CallToolResult callResult = await client.CallToolAsync(
			ApplicationGetListTool.ApplicationGetListToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>() },
			cancellationToken: cts.Token);
		ApplicationListResponse response = EntitySchemaStructuredResultParser.Extract<ApplicationListResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a header-only list-apps call must bind and execute, not fail at the MCP protocol level");
		response.Success.Should().BeTrue(
			because: "a header-only list-apps call must resolve and execute against the header tenant with no pre-registered environment (SM-01)");
	}

	[Test]
	[Description("A list-apps call carrying BOTH a credential-passthrough header and an explicit environment-name is rejected before any named-tenant lookup, proving no confused-deputy (Story 15 AC-01; PRD AC-06).")]
	public async Task ApplicationGetList_ShouldRejectMixedInput_WhenHeaderAndEnvironmentNameBothSupplied() {
		// Arrange
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);
		await using McpClient client = await ConnectTenantOneAsync(server, stand, cts.Token);

		// Act
		CallToolResult callResult = await client.CallToolAsync(
			ApplicationGetListTool.ApplicationGetListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["environment-name"] = "mixed-input-probe-environment" }
			},
			cancellationToken: cts.Token);
		ApplicationListResponse response = EntitySchemaStructuredResultParser.Extract<ApplicationListResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the mixed-input rejection is a structured tool-level failure, not an MCP protocol error");
		response.Success.Should().BeFalse(
			because: "a header + environment-name mixed-input call must be rejected before any named-tenant lookup (PRD AC-06)");
		response.Error.Should().Contain(MixedInputRejectionMarker,
			because: "the rejection must name the credential-passthrough contract, not silently ignore the extra environment-name (confused-deputy)");
	}

	[Test]
	[Description("A header-only create-app-section call against a REAL application resolves the nested caption-culture readback against the header tenant (Story 15 AC-01, Story 6; PRD FR-08).")]
	public async Task ApplicationSectionCreate_ShouldResolveHeaderTenantCulture_WhenHeaderOnly() {
		// Arrange
		// MANUAL-RUNNER ASSUMPTION: create-app-section's nested caption-culture path can only be
		// exercised against a REAL application, so this case is self-contained: it creates a
		// throwaway application (header-only, tenant one, itself one of the AC-02-mandated tools)
		// before exercising the section-creation path under test.
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(5));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);
		await using McpClient client = await ConnectTenantOneAsync(server, stand, cts.Token);
		string probeSuffix = Guid.NewGuid().ToString("N")[..8];
		string appCode = $"IsoProbeApp{probeSuffix}";
		ApplicationContextResponse createdApplication =
			await CreateIsolationProbeApplicationAsync(client, appCode, cts.Token);
		createdApplication.Success.Should().BeTrue(
			because: "the throwaway application must be created (header-only, tenant one) before the nested caption-culture path can be exercised");
		string caption = $"Isolation Probe {probeSuffix}";

		// Act — create-app-section is a long-tail tool (absent from lazy-mode tools/list); reach it via clio-run.
		CallToolResult callResult = await CallViaClioRunAsync(client, ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
			new Dictionary<string, object?> {
				["application-code"] = createdApplication.ApplicationCode ?? appCode,
				["caption"] = caption,
				["with-mobile-pages"] = false
			},
			cts.Token);
		ApplicationSectionContextResponse response =
			EntitySchemaStructuredResultParser.Extract<ApplicationSectionContextResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a header-only create-app-section call against a real application must not fail at the MCP protocol level");
		response.Success.Should().BeTrue(
			because: "a header-only create-app-section call against a real application must resolve the header tenant's caption culture and succeed");
		response.Section.Should().NotBeNull(
			because: "a successful create-app-section response must return the created section, including its caption-culture readback");
		response.Section!.Caption.Should().Be(caption,
			because: "the nested caption-culture readback must echo the SAME caption submitted for the header tenant, proving the culture-resolution path executed against the header tenant rather than being skipped (Story 6)");
	}

	[Test]
	[Description("A create-app-section call carrying BOTH a credential-passthrough header and an explicit environment-name is rejected before the nested caption-culture path (or any other Creatio-reaching call) runs (Story 15 AC-01; PRD AC-06).")]
	public async Task ApplicationSectionCreate_ShouldRejectMixedInput_WhenHeaderAndEnvironmentNameBothSupplied() {
		// Arrange
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);
		await using McpClient client = await ConnectTenantOneAsync(server, stand, cts.Token);

		// Act — the application-code is deliberately a non-existent placeholder: the rejection must fire
		// before ANY Creatio-reaching call in the nested graph, so the section never needs to exist.
		// create-app-section is a long-tail tool (absent from lazy-mode tools/list); reach it via clio-run.
		CallToolResult callResult = await CallViaClioRunAsync(client, ApplicationSectionCreateTool.ApplicationSectionCreateToolName,
			new Dictionary<string, object?> {
				["application-code"] = "mixed-input-probe-application",
				["caption"] = "Mixed Input Probe",
				["environment-name"] = "mixed-input-probe-environment"
			},
			cts.Token);
		ApplicationSectionContextResponse response =
			EntitySchemaStructuredResultParser.Extract<ApplicationSectionContextResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the mixed-input rejection is a structured tool-level failure, not an MCP protocol error");
		response.Success.Should().BeFalse(
			because: "a header + environment-name mixed-input call must be rejected before any named-tenant lookup, including the nested caption-culture/app-info calls (PRD AC-06)");
		response.Error.Should().Contain(MixedInputRejectionMarker,
			because: "the rejection must name the credential-passthrough contract, not silently ignore the extra environment-name (confused-deputy)");
	}

	[Test]
	[Description("A header-only get-user-culture call resolves the profile culture against the header tenant with no pre-registered environment (Story 15 AC-01, Story 10; PRD FR-08).")]
	public async Task GetUserCulture_ShouldResolveHeaderTenant_WhenHeaderOnly() {
		// Arrange
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);
		await using McpClient client = await ConnectTenantOneAsync(server, stand, cts.Token);

		// Act — get-user-culture is a long-tail tool (absent from lazy-mode tools/list); reach it via clio-run.
		CallToolResult callResult = await CallViaClioRunAsync(client, GetUserCultureTool.ToolName,
			new Dictionary<string, object?>(), cts.Token);
		GetUserCultureResponse response = EntitySchemaStructuredResultParser.Extract<GetUserCultureResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a header-only get-user-culture call must bind and execute, not fail at the MCP protocol level");
		response.Success.Should().BeTrue(
			because: "a header-only get-user-culture call must resolve the profile culture against the header tenant with no pre-registered environment (Story 10)");
		response.ResolvedFrom.Should().Be(GetUserCultureTool.ResolvedFromEnvironment,
			because: "a successful header-only resolution must report the environment tier, never a fallback the tool never attempts");
	}

	[Test]
	[Description("A get-user-culture call carrying BOTH a credential-passthrough header and an explicit environment-name is rejected before any named-tenant lookup, closing the active-tenant leak this story fixed (Story 15 AC-01, Story 10; PRD AC-06).")]
	public async Task GetUserCulture_ShouldRejectMixedInput_WhenHeaderAndEnvironmentNameBothSupplied() {
		// Arrange
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);
		await using McpClient client = await ConnectTenantOneAsync(server, stand, cts.Token);

		// Act — get-user-culture is a long-tail tool (absent from lazy-mode tools/list); reach it via clio-run.
		CallToolResult callResult = await CallViaClioRunAsync(client, GetUserCultureTool.ToolName,
			new Dictionary<string, object?> { ["environment-name"] = "mixed-input-probe-environment" },
			cts.Token);
		GetUserCultureResponse response = EntitySchemaStructuredResultParser.Extract<GetUserCultureResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the mixed-input rejection is a structured tool-level failure, not an MCP protocol error");
		response.Success.Should().BeFalse(
			because: "a header + environment-name mixed-input call must be rejected before any named-tenant lookup (PRD AC-06)");
		response.Culture.Should().BeNull(
			because: "a rejected call must never surface a fallback culture as if it were resolved");
		response.Reason.Should().Contain(MixedInputRejectionMarker,
			because: "the rejection must name the credential-passthrough contract, not silently ignore the extra environment-name (confused-deputy)");
	}

	[Test]
	[Description("A header-only link-from-repository-unlocked call is rejected with the uniform credential-passthrough guard message, because the tool always reaches Creatio via the registered environment's stored credentials (Story 15 AC-01, Story 1; PRD FR-08).")]
	public async Task LinkFromRepositoryUnlocked_ShouldReturnUniformRejection_WhenPassthroughActive() {
		// Arrange
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);
		await using McpClient client = await ConnectTenantOneAsync(server, stand, cts.Token);

		// Act — link-from-repository-unlocked is a long-tail tool (absent from lazy-mode tools/list);
		// reach it via clio-run. Flat (non-"args") parameters: this tool binds repoPath/environmentName
		// directly, not through a JsonPropertyName-kebab record — clio-run's own "args" field forwards
		// verbatim, so the flat shape is unchanged.
		CallToolResult callResult = await CallViaClioRunAsync(client, LinkFromRepositoryTool.LinkFromRepositoryUnlockedToolName,
			new Dictionary<string, object?> {
				["repoPath"] = Path.Combine(Path.GetTempPath(), $"clio-e2e-mixed-input-probe-{Guid.NewGuid():N}")
			},
			cts.Token);
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(callResult);

		// Assert
		envelope.ExitCode.Should().Be(1,
			because: "the passthrough guard returns the EXPECTED, caller-actionable validation exit code, never the unexpected-failure code");
		envelope.Output.Should().Contain(
			message => message.Value != null && message.Value.Contains("not supported under credential passthrough"),
			because: "link-from-repository-unlocked always reaches Creatio via the registered environment's stored credentials, so it must be rejected uniformly under passthrough even with no environment-name supplied");
	}

	[Test]
	[Description("A link-from-repository-unlocked call carrying BOTH a credential-passthrough header and an explicit environment-name still hits the SAME uniform guard rejection, proving no confused-deputy (Story 15 AC-01, Story 1; PRD AC-06).")]
	public async Task LinkFromRepositoryUnlocked_ShouldReturnUniformRejection_WhenHeaderAndEnvironmentNameBothSupplied() {
		// Arrange
		McpHttpPassthroughStand stand = McpHttpPassthroughStand.RequireOrIgnore();
		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(3));
		await using McpHttpServerSession server =
			await McpHttpServerSession.StartAsync(_settings, stand.PlatformApiKey, cts.Token);
		await using McpClient client = await ConnectTenantOneAsync(server, stand, cts.Token);

		// Act — link-from-repository-unlocked is a long-tail tool (absent from lazy-mode tools/list); reach it via clio-run.
		CallToolResult callResult = await CallViaClioRunAsync(client, LinkFromRepositoryTool.LinkFromRepositoryUnlockedToolName,
			new Dictionary<string, object?> {
				["repoPath"] = Path.Combine(Path.GetTempPath(), $"clio-e2e-mixed-input-probe-{Guid.NewGuid():N}"),
				["environmentName"] = "mixed-input-probe-environment"
			},
			cts.Token);
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(callResult);

		// Assert
		envelope.ExitCode.Should().Be(1,
			because: "the passthrough guard fires BEFORE the explicit non-passthrough environment-name requiredness check, so a mixed-input call is rejected with the SAME uniform message, not a different one");
		envelope.Output.Should().Contain(
			message => message.Value != null && message.Value.Contains("not supported under credential passthrough"),
			because: "the extra environment-name argument must not change the outcome — the tool is uniformly unsupported under passthrough regardless of what else is supplied (confused-deputy, Security mode iii)");
	}

	private static async Task<McpClient> ConnectTenantOneAsync(
		McpHttpServerSession server, McpHttpPassthroughStand stand, CancellationToken cancellationToken) =>
		await server.ConnectAsync(
			stand.PlatformApiKey,
			stand.TenantOneCredentialsBase64,
			cancellationToken);

	private static async Task<ApplicationContextResponse> CreateIsolationProbeApplicationAsync(
		McpClient client, string code, CancellationToken cancellationToken) {
		// create-app is a long-tail tool (absent from lazy-mode tools/list); reach it via clio-run.
		CallToolResult callResult = await CallViaClioRunAsync(client, ApplicationCreateTool.ApplicationCreateToolName,
			new Dictionary<string, object?> {
				["name"] = $"Story 15 Isolation Probe {code}",
				["code"] = code,
				["with-mobile-pages"] = false
			},
			cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<ApplicationContextResponse>(callResult);
	}
}
